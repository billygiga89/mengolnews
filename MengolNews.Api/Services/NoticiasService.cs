using HtmlAgilityPack;
using MengolNews.Api.Models;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.ServiceModel.Syndication;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace MengolNews.Api.Services
{
    public class NoticiasService
    {
        #region Campos e construtor

        private readonly HttpClient _http;
        private readonly CloudflareKvService _kv;
        private readonly ReescritorService _reescritor;

        // Cache da lista de notícias
        private List<NoticiaDto>? _cache;
        private DateTime _ultimaAtualizacao;
        private static readonly TimeSpan _cacheDuracao = TimeSpan.FromMinutes(10);

        // Garante que só uma coleta rode por vez
        private static readonly SemaphoreSlim _lockAtualizacao = new(1, 1);

        // Limita scraping/KV paralelo para não sobrecarregar
        private static readonly SemaphoreSlim _semaforo = new(5, 5);

        // Memória rápida das reescritas (evita ir ao KV ou à IA a cada atualização)
        private static readonly ConcurrentDictionary<string, NoticiaDto> _resumosReescritos = new();
        private static readonly ConcurrentDictionary<string, string> _corposReescritos = new();
        private static readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _corposEmAndamento = new();
        private static readonly SemaphoreSlim _lockReescrita = new(1, 1);
        private static readonly SemaphoreSlim _lockPreAquecimento = new(1, 1);
        private const int MaxResumosPorLote = 20;

        // Texto original que veio no RSS (plano B para reescrever o corpo)
        private static readonly ConcurrentDictionary<string, string> _textosOriginais = new();

        // link -> url da foto lida na página (evita perder a foto se a leitura falhar numa atualização futura)
        private static readonly ConcurrentDictionary<string, string> _imagensConhecidas = new();

        private readonly bool _reescritaHabilitada;

        public NoticiasService(HttpClient http, CloudflareKvService kv, ReescritorService reescritor, IConfiguration config)
        {
            _http = http;
            _kv = kv;
            _reescritor = reescritor;
            _reescritaHabilitada = config.GetValue("Reescrita:Habilitada", true);
            if (!_reescritaHabilitada)
                Console.WriteLine("[IA] ⏸️ Reescrita por IA desligada (Reescrita:Habilitada = false)");
            _http.Timeout = TimeSpan.FromSeconds(15);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36"
            );
            _http.DefaultRequestHeaders.Add("Accept", "application/rss+xml, application/xml, text/xml, */*");
            _http.DefaultRequestHeaders.Add("Accept-Language", "pt-BR,pt;q=0.9,en;q=0.8");
        }

        #endregion

        #region Lista de notícias (cache e coleta)

        public async Task<List<NoticiaDto>> GetTodasNoticias()
        {
            var cacheAtual = _cache;

            // 1) cache ainda fresco
            if (cacheAtual != null && DateTime.Now - _ultimaAtualizacao < _cacheDuracao)
                return cacheAtual;

            // 2) cache vencido: entrega o antigo agora e atualiza por trás
            if (cacheAtual != null)
            {
                _ = AtualizarEmSegundoPlanoAsync();
                return cacheAtual;
            }

            // 3) sem cache (primeira abertura): todo mundo espera a mesma coleta
            await _lockAtualizacao.WaitAsync();
            try
            {
                if (_cache != null) return _cache;
                return await ColetarEAtualizarAsync();
            }
            finally
            {
                _lockAtualizacao.Release();
            }
        }

        private async Task AtualizarEmSegundoPlanoAsync()
        {
            // se já tem uma atualização rodando, não começa outra
            if (!await _lockAtualizacao.WaitAsync(0)) return;

            try
            {
                await ColetarEAtualizarAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro ao atualizar notícias em segundo plano: {ex.Message}");
            }
            finally
            {
                _lockAtualizacao.Release();
            }
        }

        private async Task<List<NoticiaDto>> ColetarEAtualizarAsync()
        {
            var tarefas = new List<Task<List<NoticiaDto>>>
            {
                GetEspnNoticias(),
                GetNetFla(),
                GetColunaDoFla(),
                GetUrubuInterativo(),
                GetFlamengoRj(),
                GetLanceNoticias(),
                GetPlacar(),
                GetBolEsporte(),
            };

            var resultados = await Task.WhenAll(tarefas.Select(async t =>
            {
                try
                {
                    var r = await t;
                    Console.WriteLine($"✅ Fonte retornou {r.Count} notícias");
                    return r;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Erro em fonte: {ex.Message}");
                    return new List<NoticiaDto>();
                }
            }));

            var noticiasBrutas = resultados
                .SelectMany(r => r.Take(15))
                .Where(n => n.Data >= DateTime.Now.AddDays(-30))
                .OrderByDescending(n => n.Data)
                .ToList();

            var noticias = RemoverDuplicadas(noticiasBrutas)
                .Take(50)
                .ToList();

            if (!noticias.Any())
            {
                Console.WriteLine("⚠️ Nenhuma notícia encontrada, retornando cache antigo");
                return _cache ?? new List<NoticiaDto>();
            }

            // Capas já resolvidas (RSS, página ou memória): agora a foto repetida fica só na notícia mais recente
            RemoverImagensRepetidas(noticias);

            Console.WriteLine($"TOTAL FINAL: {noticias.Count}");

            // Reescreve títulos/resumos com IA (usa memória/KV; só chama a IA para o que for novo).
            // O arquivamento no KV acontece aqui dentro, já com a versão reescrita.
            await AplicarReescritaAsync(noticias);

            _cache = noticias;
            _ultimaAtualizacao = DateTime.Now;

            PreAquecerCorposEmSegundoPlano(noticias);

            return noticias;
        }

        #endregion

        #region Fontes

        // Fontes cuja imagem do RSS é uma arte com a logo/título dela: ignora a imagem do RSS
        // e busca a foto real na página da notícia (og:image).
        private static readonly HashSet<string> FontesComCapaDeMarca =
            new(StringComparer.OrdinalIgnoreCase) { "NETFLA" };

        private Task<List<NoticiaDto>> GetEspnNoticias()
            => LerRss("https://www.espn.com.br/rss/flamengo.xml", "ESPN", filtrarFlamengo: true);

        private Task<List<NoticiaDto>> GetColunaDoFla()
            => LerRss("https://colunadofla.com/feed", "COLUNA DO FLA", filtrarFlamengo: false);

        private Task<List<NoticiaDto>> GetUrubuInterativo()
            => LerRss("https://noticiasfla.com.br/feed", "NOTÍCIAS FLA", filtrarFlamengo: false);

        private Task<List<NoticiaDto>> GetLanceNoticias()
            => LerRss("https://br.bolavip.com/rss/flamengo", "BOLAVIP", filtrarFlamengo: false);

        private Task<List<NoticiaDto>> GetNetFla()
            => LerRss("https://netfla.com.br/feed", "NETFLA", filtrarFlamengo: true);

        private Task<List<NoticiaDto>> GetFlamengoRj()
            => LerRss("https://urubuinterativo.com/feed/", "URUBU INTERATIVO", filtrarFlamengo: false);

        private Task<List<NoticiaDto>> GetPlacar()
            => LerRss("https://placar.com.br/feed/", "PLACAR", filtrarFlamengo: true);

        private Task<List<NoticiaDto>> GetBolEsporte()
            => LerRss("http://rss.bol.uol.com.br/noticias/esporte/rss.xml", "BOL ESPORTE", filtrarFlamengo: true);

        #endregion

        #region Leitor RSS

        private Task<List<NoticiaDto>> LerRss(string url, string fonte, bool filtrarFlamengo)
            => LerRssComHeaders(url, fonte, null, filtrarFlamengo);

        private async Task<List<NoticiaDto>> LerRssComHeaders(
            string url,
            string fonte,
            Dictionary<string, string>? headersExtras,
            bool filtrarFlamengo = true)
        {
            var lista = new List<NoticiaDto>();

            try
            {
                const int maxTentativas = 2;
                HttpResponseMessage? response = null;

                for (int tentativa = 1; tentativa <= maxTentativas; tentativa++)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);

                    if (headersExtras != null)
                        foreach (var kv in headersExtras)
                            request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);

                    response?.Dispose();
                    response = await _http.SendAsync(request);

                    Console.WriteLine($"[{fonte}] Status: {(int)response.StatusCode} (tentativa {tentativa}/{maxTentativas})");

                    if (response.IsSuccessStatusCode)
                        break;

                    if (tentativa < maxTentativas)
                        await Task.Delay(TimeSpan.FromSeconds(1.5));
                }

                using var _ = response;

                if (response == null || !response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[{fonte}] ❌ Falhou com status {(int)(response?.StatusCode ?? 0)} após {maxTentativas} tentativas");
                    return lista;
                }

                var xml = await response.Content.ReadAsStringAsync();

                // Normaliza a versão do RSS (ex.: "0.92" -> "2.0") para o parser aceitar
                xml = Regex.Replace(
                    xml,
                    @"(<rss[^>]*\bversion\s*=\s*"")[^""]+("")",
                    "${1}2.0${2}",
                    RegexOptions.IgnoreCase);

                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse };
                using var stringReader = new StringReader(xml);
                using var reader = XmlReader.Create(stringReader, settings);

                var feed = SyndicationFeed.Load(reader);
                if (feed == null) return lista;

                var itensBase = new List<(SyndicationItem item, string titulo, string descricao, string link)>();

                foreach (var item in feed.Items)
                {
                    var titulo = item.Title?.Text ?? "";
                    var contentEncoded = item.ElementExtensions
                        .ReadElementExtensions<XmlElement>("encoded", "http://purl.org/rss/1.0/modules/content/")
                        .FirstOrDefault()?.InnerText ?? "";

                    var descricaoBruta = !string.IsNullOrWhiteSpace(contentEncoded)
                        ? contentEncoded
                        : item.Summary?.Text ?? "";

                    var link = item.Links.FirstOrDefault()?.Uri.ToString() ?? "";

                    if (filtrarFlamengo && !EhRelacionadoAoFlamengo(titulo, descricaoBruta))
                        continue;

                    var descricao = LimparTextoRss(LimparHtml(descricaoBruta));

                    itensBase.Add((item, titulo, descricao, link));
                }

                Console.WriteLine($"[{fonte}] ✅ {itensBase.Count} itens após filtro");

                var tarefasImagem = itensBase.Select(async entry =>
                {
                    var (item, titulo, descricao, link) = entry;

                    var imagem = await ResolverCapaAsync(item, url, link, fonte);

                    var dataUtc = item.PublishDate.UtcDateTime == DateTime.MinValue
                        ? DateTime.UtcNow
                        : item.PublishDate.UtcDateTime;

                    return new NoticiaDto
                    {
                        Titulo = titulo,
                        Descricao = descricao,
                        Conteudo = descricao,
                        Link = link,
                        Fonte = fonte,
                        Data = ConverterParaBrasilia(dataUtc),
                        Imagem = imagem
                    };
                });

                lista = (await Task.WhenAll(tarefasImagem)).ToList();
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine($"[{fonte}] ⏱️ Timeout — fonte demorou demais, pulando");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{fonte}] ❌ Erro: {ex.Message}");
            }

            return lista;
        }

        private static DateTime ConverterParaBrasilia(DateTime utc)
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
            }
            catch
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
                    return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
                }
                catch
                {
                    return utc.AddHours(-3);
                }
            }
        }

        #endregion

        #region Filtro Flamengo

        private static readonly Regex RegexFlamengo = new(
            @"\b(Flamengo|Fla|Meng[ãa]o|Mengo|Rubro-Negro|CRF)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static bool EhRelacionadoAoFlamengo(string titulo, string descricao)
            => RegexFlamengo.IsMatch(titulo) || RegexFlamengo.IsMatch(descricao);

        #endregion

        #region Deduplicação (título e imagem)

        private static readonly List<string> PrioridadeFontes = new()
        {
            "ESPN",
            "COLUNA DO FLA",
            "URUBU INTERATIVO",
            "NETFLA",
            "BOLAVIP",
            "PLACAR",
            "BOL ESPORTE",
            "NOTÍCIAS FLA",
        };

        private const double LimiarSimilaridadeTitulo = 0.5;

        private static readonly HashSet<string> StopWordsTitulo = new(StringComparer.OrdinalIgnoreCase)
        {
            "a","o","os","as","de","da","do","das","dos","e","em","no","na","nos","nas",
            "para","por","com","um","uma","que","é","ao","à","se","sobre","apos","antes",
            "flamengo","fla"
        };

        // --- por título ---

        private static List<NoticiaDto> RemoverDuplicadas(List<NoticiaDto> noticias)
        {
            var resultado = new List<NoticiaDto>();

            foreach (var noticia in noticias)
            {
                var tokensAtual = TokenizarTitulo(noticia.Titulo);

                NoticiaDto? duplicata = null;
                foreach (var existente in resultado)
                {
                    var similaridade = CalcularSimilaridade(tokensAtual, TokenizarTitulo(existente.Titulo));
                    if (similaridade >= LimiarSimilaridadeTitulo)
                    {
                        duplicata = existente;
                        break;
                    }
                }

                if (duplicata == null)
                {
                    resultado.Add(noticia);
                }
                else if (EhMelhorVersao(noticia, duplicata))
                {
                    var idx = resultado.IndexOf(duplicata);
                    resultado[idx] = noticia;
                }
            }

            return resultado;
        }

        private static HashSet<string> TokenizarTitulo(string titulo)
        {
            var texto = RemoverAcentos(titulo.ToLowerInvariant());
            texto = Regex.Replace(texto, @"[^a-z0-9\s]", " ");

            return texto
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 2 && !StopWordsTitulo.Contains(t))
                .ToHashSet();
        }

        private static double CalcularSimilaridade(HashSet<string> a, HashSet<string> b)
        {
            if (a.Count == 0 || b.Count == 0) return 0;

            var intersecao = a.Intersect(b).Count();
            var uniao = a.Union(b).Count();

            return (double)intersecao / uniao;
        }

        private static string RemoverAcentos(string texto)
        {
            var normalizado = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (var c in normalizado)
            {
                var categoria = CharUnicodeInfo.GetUnicodeCategory(c);
                if (categoria != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static int PrioridadeFonte(string fonte)
        {
            var idx = PrioridadeFontes.FindIndex(f => string.Equals(f, fonte, StringComparison.OrdinalIgnoreCase));
            return idx == -1 ? PrioridadeFontes.Count : idx;
        }

        private static bool EhMelhorVersao(NoticiaDto candidata, NoticiaDto atual)
        {
            var prioridadeCandidata = PrioridadeFonte(candidata.Fonte);
            var prioridadeAtual = PrioridadeFonte(atual.Fonte);

            if (prioridadeCandidata != prioridadeAtual)
                return prioridadeCandidata < prioridadeAtual;

            var pontosCandidata = (string.IsNullOrWhiteSpace(candidata.Imagem) ? 0 : 1)
                + (candidata.Descricao?.Length ?? 0) / 100;

            var pontosAtual = (string.IsNullOrWhiteSpace(atual.Imagem) ? 0 : 1)
                + (atual.Descricao?.Length ?? 0) / 100;

            return pontosCandidata > pontosAtual;
        }

        // --- por imagem ---

        private static readonly Regex RegexInicioUrl =
            new(@"^https?://(www\.)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RegexSufixoTamanho =
            new(@"-\d{2,4}x\d{2,4}(?=\.[a-z]{3,4}$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// "Identidade" da foto: a mesma foto vinda de fontes/pastas diferentes dá a mesma chave.
        /// Remove o embrulho da URL, parâmetros e sufixo de tamanho (-1024x683). Se o nome do arquivo for
        /// específico (longo), ele sozinho é a identidade (o Globo muda host e pasta para a mesma foto).
        /// </summary>
        private static string IdentidadeDaFoto(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";

            var u = url.Trim();

            // URL embrulhada: fica só com a URL de dentro
            var embrulhada = Math.Max(
                u.LastIndexOf("https://", StringComparison.OrdinalIgnoreCase),
                u.LastIndexOf("http://", StringComparison.OrdinalIgnoreCase));
            if (embrulhada > 0) u = u[embrulhada..];

            var corte = u.IndexOfAny(new[] { '?', '#' });
            if (corte >= 0) u = u[..corte];

            u = RegexInicioUrl.Replace(u, "");
            u = RegexSufixoTamanho.Replace(u, "");
            u = u.ToLowerInvariant();

            // Nome do arquivo específico (ex.: 744700-treino-no-ninho-07-10-2026-046.jpg) = mesma foto.
            // Nomes curtos/genéricos (foto.jpg, image.jpg) não servem: nesses casos compara a URL inteira.
            var nome = u[(u.LastIndexOf('/') + 1)..];
            if (Path.GetFileNameWithoutExtension(nome).Length >= 20)
                return "arquivo:" + nome;

            return u;
        }

        /// <summary>
        /// A primeira notícia da lista (a mais recente) mantém a foto; as seguintes com a mesma foto
        /// ficam sem imagem e o site usa o FallbackImages nelas.
        /// </summary>
        private static void RemoverImagensRepetidas(List<NoticiaDto> noticias)
        {
            var vistas = new HashSet<string>();
            var removidas = 0;

            foreach (var n in noticias)
            {
                var chave = IdentidadeDaFoto(n.Imagem);
                if (chave == "") continue;

                if (!vistas.Add(chave))
                {
                    n.Imagem = "";
                    removidas++;
                }
            }

            if (removidas > 0)
                Console.WriteLine($"[IMG] {removidas} imagem(ns) repetida(s) trocada(s) por fallback");
        }

        #endregion

        #region Imagens (capa, validação e leitura)

        private static readonly string[] PadroesImagemInvalida = new[]
        {
            // placeholders genéricos
            "noimg.jpg",
            "no-image",
            "sem-imagem",
            "sem-foto",
            "semfoto",
            "placeholder",
            "default.jpg",
            "tiktokcdn",
            "futbolsites.net/generic",

            // logos e artes de marca dos sites de origem (vira fallback do MengolNews)
            "netfla.com.br/img/",
            "/logo", "logo.", "-logo", "_logo", "logo-", "logo_",
            "favicon",
            "/brand",
            "og-default", "default-og",
            "default-share", "share-default", "social-default",
            "/avatar",
        };

        private static bool EhImagemInvalida(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return true;
            return PadroesImagemInvalida.Any(p => url.Contains(p, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Descobre a capa: RSS -> foto já conhecida -> foto lida na página da notícia.</summary>
        private async Task<string> ResolverCapaAsync(SyndicationItem item, string urlFeed, string link, string fonte)
        {
            // Fontes com capa de marca (ex.: NETFLA): ignora a imagem do RSS e vai direto à foto da página
            var capaDeMarca = FontesComCapaDeMarca.Contains(fonte);
            var imagem = capaDeMarca ? "" : NormalizarImagem(ExtrairImagem(item), urlFeed);

            if (!EhImagemInvalida(imagem)) return imagem;
            if (string.IsNullOrWhiteSpace(link)) return "";

            // 1) já descobrimos a foto dessa notícia antes?
            var salva = await ImagemSalvaAsync(link);
            if (!EhImagemInvalida(salva)) return salva!;

            // 2) lê a foto da página da notícia
            await _semaforo.WaitAsync();
            try
            {
                var imgPagina = NormalizarImagem(await ExtrairImagemDaPaginaAsync(link), link);

                if (!EhImagemInvalida(imgPagina))
                {
                    GuardarImagem(link, imgPagina);
                    return imgPagina;
                }
            }
            finally
            {
                _semaforo.Release();
            }

            return "";
        }

        // --- imagem dentro do item do RSS ---

        private static string? ExtrairImagem(SyndicationItem item)
        {
            // 1) media:content
            var media = item.ElementExtensions
                .ReadElementExtensions<XmlElement>("content", "http://search.yahoo.com/mrss/")
                .FirstOrDefault();

            if (media?.HasAttribute("url") == true)
                return media.GetAttribute("url");

            // 2) media:thumbnail
            var thumb = item.ElementExtensions
                .ReadElementExtensions<XmlElement>("thumbnail", "http://search.yahoo.com/mrss/")
                .FirstOrDefault();

            if (thumb?.HasAttribute("url") == true)
                return thumb.GetAttribute("url");

            // 3) enclosure de imagem
            var enclosure = item.Links.FirstOrDefault(l =>
                l.RelationshipType == "enclosure" &&
                (l.MediaType?.StartsWith("image") == true));

            if (enclosure != null)
                return enclosure.Uri.ToString();

            // 4) primeira <img> do HTML: content:encoded primeiro, depois o resumo
            var encoded = item.ElementExtensions
                .ReadElementExtensions<XmlElement>("encoded", "http://purl.org/rss/1.0/modules/content/")
                .FirstOrDefault()?.InnerText;

            foreach (var html in new[] { encoded, item.Summary?.Text })
            {
                if (string.IsNullOrWhiteSpace(html)) continue;

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var imgs = doc.DocumentNode.SelectNodes("//img");
                if (imgs == null) continue;

                foreach (var img in imgs)
                {
                    var src = img.GetAttributeValue("src", null)
                              ?? img.GetAttributeValue("data-src", null)
                              ?? img.GetAttributeValue("data-lazy-src", null);

                    if (!string.IsNullOrWhiteSpace(src) && !src.StartsWith("data:"))
                        return src;
                }
            }

            return null;
        }

        // --- imagem lida na página da notícia ---

        private async Task<string?> ExtrairImagemDaPaginaAsync(string url)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var resp = await _http.GetAsync(url, cts.Token);

                if (!resp.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[IMG] Página respondeu HTTP {(int)resp.StatusCode}: {url}");
                    return null;
                }

                var html = await resp.Content.ReadAsStringAsync(cts.Token);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var ogImage = doc.DocumentNode
                    .SelectSingleNode("//meta[@property='og:image'] | //meta[@name='og:image']");

                if (ogImage != null)
                {
                    var content = ogImage.GetAttributeValue("content", null);
                    if (!string.IsNullOrWhiteSpace(content))
                        return WebUtility.HtmlDecode(content);
                }

                var twitterImage = doc.DocumentNode
                    .SelectSingleNode("//meta[@name='twitter:image']");

                if (twitterImage != null)
                {
                    var content = twitterImage.GetAttributeValue("content", null);
                    if (!string.IsNullOrWhiteSpace(content))
                        return WebUtility.HtmlDecode(content);
                }

                var img = doc.DocumentNode
                    .SelectSingleNode("//article//img | //div[contains(@class,'content')]//img");

                return PegarImagem(img);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IMG] Falha ao ler imagem da página ({url}): {ex.Message}");
                return null;
            }
        }

        private static string? PegarImagem(HtmlNode? img)
        {
            if (img == null) return null;

            return img.GetAttributeValue("src", null)
                ?? img.GetAttributeValue("data-src", null)
                ?? img.GetAttributeValue("data-lazy-src", null);
        }

        private static string NormalizarImagem(string? url, string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";

            if (url.StartsWith("//")) return "https:" + url;

            if (url.StartsWith("/"))
            {
                try
                {
                    var uri = new Uri(baseUrl);
                    return $"{uri.Scheme}://{uri.Host}{url}";
                }
                catch { return ""; }
            }

            if (url.StartsWith("data:") || url.StartsWith("blob:")) return "";

            return url;
        }

        // --- fotos já descobertas (memória + KV) ---

        private static string ChaveImagemSalva(string link) => $"img:{HashLink(link)}";

        private async Task<string?> ImagemSalvaAsync(string link)
        {
            if (_imagensConhecidas.TryGetValue(link, out var emMemoria))
                return emMemoria;

            await _semaforo.WaitAsync();
            try
            {
                var salva = await _kv.GetAsync(ChaveImagemSalva(link));
                if (!string.IsNullOrWhiteSpace(salva))
                {
                    _imagensConhecidas[link] = salva;
                    return salva;
                }
            }
            catch { /* best-effort */ }
            finally
            {
                _semaforo.Release();
            }

            return null;
        }

        private void GuardarImagem(string link, string imagem)
        {
            if (string.IsNullOrWhiteSpace(link) || string.IsNullOrWhiteSpace(imagem)) return;

            if (_imagensConhecidas.Count > 3000) _imagensConhecidas.Clear();
            if (_imagensConhecidas.TryGetValue(link, out var atual) && atual == imagem) return;

            _imagensConhecidas[link] = imagem;

            _ = Task.Run(async () =>
            {
                try { await _kv.SetAsync(ChaveImagemSalva(link), imagem); } catch { }
            });
        }

        #endregion

        #region Arquivo permanente (Cloudflare KV)

        private static string HashLink(string link)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(link))).ToLowerInvariant();
        }

        private static string ChaveArquivo(string link) => $"noticia:{HashLink(link)}";
        private static string ChaveReescrita(string link) => $"reesc:{HashLink(link)}";
        private static string ChaveCorpo(string link) => $"texto:{HashLink(link)}";

        public static string IdDoLink(string link) => HashLink(link);

        private static readonly Regex RegexId = new(@"^[0-9a-f]{64}$", RegexOptions.Compiled);

        private static NoticiaDto? LerNoticia(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<NoticiaDto>(json); }
            catch { return null; }
        }

        public async Task<NoticiaDto?> BuscarNoArquivoAsync(string link)
        {
            if (string.IsNullOrWhiteSpace(link)) return null;

            // 1) versão já reescrita
            var dto = LerNoticia(await _kv.GetAsync(ChaveReescrita(link)));
            if (dto != null) return dto;

            // 2) arquivo antigo (texto original da fonte): reescreve agora e passa a usar a versão nova
            dto = LerNoticia(await _kv.GetAsync(ChaveArquivo(link)));
            if (dto != null)
                await AplicarReescritaAsync(new List<NoticiaDto> { dto });

            return dto;
        }

        /// <summary>Descobre o link original a partir do Id (lista recente ou arquivo no KV).</summary>
        public async Task<string?> LinkPorIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;

            id = id.Trim().ToLowerInvariant();
            if (!RegexId.IsMatch(id)) return null;

            // 1) lista recente
            var lista = _cache ?? await GetTodasNoticias();
            var achada = lista.FirstOrDefault(n => IdDoLink(n.Link) == id);
            if (achada != null) return achada.Link;

            // 2) arquivo (KV): as chaves usam o mesmo hash do link
            var dto = LerNoticia(await _kv.GetAsync($"reesc:{id}"))
                      ?? LerNoticia(await _kv.GetAsync($"noticia:{id}"));

            return dto?.Link;
        }

        #endregion

        #region Reescrita por IA — título e resumo

        private static void GuardarOriginal(NoticiaDto n)
        {
            var texto = n.Conteudo ?? n.Descricao;
            if (string.IsNullOrWhiteSpace(n.Link) || string.IsNullOrWhiteSpace(texto)) return;

            if (_textosOriginais.Count > 300) _textosOriginais.Clear();

            // só guarda se for maior que o que já tem (não troca o texto longo por um resumo curto)
            if (!_textosOriginais.TryGetValue(n.Link, out var atual) || texto.Length > atual.Length)
                _textosOriginais[n.Link] = texto;
        }

        private async Task AplicarReescritaAsync(List<NoticiaDto> noticias)
        {
            await _lockReescrita.WaitAsync();
            try
            {
                if (_resumosReescritos.Count > 2000) _resumosReescritos.Clear();

                foreach (var n in noticias) GuardarOriginal(n);

                // 1) o que já foi reescrito antes (memória ou KV)
                var achados = await Task.WhenAll(noticias.Select(TentarAplicarSalvoAsync));

                var pendentes = new List<NoticiaDto>();
                for (int i = 0; i < noticias.Count; i++)
                    if (!achados[i] && !string.IsNullOrWhiteSpace(noticias[i].Link))
                        pendentes.Add(noticias[i]);

                if (pendentes.Count == 0) return;
                Console.WriteLine($"[IA] ✍️ Reescrevendo {pendentes.Count} títulos/resumos novos");

                if (pendentes.Count == 0) return;
                if (!_reescritor.Disponivel) return; // as já reescritas (memória/KV) continuam sendo aplicadas; só não chama a IA

                // 2) o que é novo vai para a IA, em lotes
                await Task.WhenAll(pendentes.Chunk(MaxResumosPorLote).Select(async lote =>
                {
                    var itens = lote.Select(n => (n.Titulo, n.Descricao ?? "")).ToList();
                    var novos = await _reescritor.ReescreverResumosAsync(itens);

                    for (int i = 0; i < lote.Length; i++)
                    {
                        if (!novos.TryGetValue(i, out var novo)) continue; // falhou: fica o original, tenta de novo depois

                        var n = lote[i];
                        n.Titulo = novo.Titulo;
                        n.Descricao = novo.Descricao;
                        n.Conteudo = novo.Descricao;

                        var salvo = new NoticiaDto
                        {
                            Titulo = n.Titulo,
                            Descricao = n.Descricao,
                            Conteudo = n.Conteudo,
                            Link = n.Link,
                            Fonte = n.Fonte,
                            Data = n.Data,
                            Imagem = n.Imagem
                        };

                        _resumosReescritos[n.Link] = salvo;
                        _ = SalvarReescritaAsync(salvo);
                    }
                }));
            }
            finally
            {
                _lockReescrita.Release();
            }
        }

        private async Task<bool> TentarAplicarSalvoAsync(NoticiaDto n)
        {
            if (string.IsNullOrWhiteSpace(n.Link)) return false;

            NoticiaDto? salvo;
            if (!_resumosReescritos.TryGetValue(n.Link, out salvo))
            {
                await _semaforo.WaitAsync();
                try { salvo = LerNoticia(await _kv.GetAsync(ChaveReescrita(n.Link))); }
                catch { salvo = null; }
                finally { _semaforo.Release(); }

                if (salvo == null || string.IsNullOrWhiteSpace(salvo.Titulo)) return false;
                _resumosReescritos[n.Link] = salvo;
            }

            n.Titulo = salvo!.Titulo;
            n.Descricao = salvo.Descricao;
            n.Conteudo = salvo.Descricao;
            return true;
        }

        private async Task SalvarReescritaAsync(NoticiaDto n)
        {
            try { await _kv.SetAsync(ChaveReescrita(n.Link), JsonSerializer.Serialize(n)); }
            catch { /* best-effort: nunca atrapalha a listagem */ }
        }

        #endregion

        #region Reescrita por IA — corpo da matéria (sob demanda)

        public async Task<string?> ObterConteudoReescritoAsync(string link)
        {
            if (string.IsNullOrWhiteSpace(link)) return null;
            if (_corposReescritos.TryGetValue(link, out var pronto)) return pronto;

            // se duas pessoas abrirem a mesma notícia ao mesmo tempo, só uma chamada à IA é feita
            var tarefa = _corposEmAndamento.GetOrAdd(link,
                l => new Lazy<Task<string?>>(() => GerarCorpoReescritoAsync(l)));

            try { return await tarefa.Value; }
            finally { _corposEmAndamento.TryRemove(link, out _); }
        }

        private void PreAquecerCorposEmSegundoPlano(List<NoticiaDto> noticias)
        {
            if (!_reescritor.Disponivel) return;

            _ = Task.Run(async () =>
            {
                // se já tem um pré-aquecimento rodando, não começa outro
                if (!await _lockPreAquecimento.WaitAsync(0)) return;

                try
                {
                    foreach (var n in noticias.Take(15))
                    {
                        if (!_reescritor.Disponivel) break;
                        if (string.IsNullOrWhiteSpace(n.Link)) continue;
                        if (_corposReescritos.ContainsKey(n.Link)) continue; // já está pronto

                        try { await ObterConteudoReescritoAsync(n.Link); }
                        catch { /* best-effort */ }

                        // pausa curta entre uma e outra, para respeitar o limite por minuto do Gemini
                        await Task.Delay(TimeSpan.FromSeconds(4));
                    }
                }
                finally
                {
                    _lockPreAquecimento.Release();
                }
            });
        }

        private async Task<string?> GerarCorpoReescritoAsync(string link)
        {
            var chave = ChaveCorpo(link);

            // 1) já foi reescrito antes? (KV)
            try
            {
                var salvo = await _kv.GetAsync(chave);
                if (!string.IsNullOrWhiteSpace(salvo))
                {
                    _corposReescritos[link] = salvo;
                    return salvo;
                }
            }
            catch { }

            // com a IA desligada, não faz mais nada (nem baixa a página da fonte)
            if (!_reescritor.Disponivel) return null;

            // 2) título (já reescrito) só para dar contexto à IA
            var titulo = _cache?.FirstOrDefault(n => n.Link == link)?.Titulo
                         ?? (await BuscarNoArquivoAsync(link))?.Titulo
                         ?? "";

            // 3) texto original: primeiro a página da fonte, depois o que veio no RSS
            var original = await ExtrairConteudoDaPaginaAsync(link);
            var tamanhoPagina = original?.Length ?? 0;

            if (tamanhoPagina < 300 && _textosOriginais.TryGetValue(link, out var doFeed) && doFeed.Length > tamanhoPagina)
                original = doFeed;

            Console.WriteLine($"[IA] Corpo: página={tamanhoPagina} chars, usado={original?.Length ?? 0} chars");

            if (string.IsNullOrWhiteSpace(original) || original.Length < 300) return null;

            // 4) reescreve
            var novo = await _reescritor.ReescreverCorpoAsync(titulo, original);
            if (string.IsNullOrWhiteSpace(novo)) return null; // não guarda falha: tenta de novo na próxima abertura

            if (_corposReescritos.Count > 500) _corposReescritos.Clear();
            _corposReescritos[link] = novo;

            _ = Task.Run(async () =>
            {
                try { await _kv.SetAsync(chave, novo); } catch { }
            });

            return novo;
        }

        #endregion

        #region Conteúdo da página da fonte

        public async Task<string?> ExtrairConteudoDaPaginaAsync(string url)
        {
            try
            {
                using var resp = await _http.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[IA] Página da fonte respondeu HTTP {(int)resp.StatusCode}");
                    return null;
                }

                var html = await resp.Content.ReadAsStringAsync();
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // 1) Muitos sites entregam o texto inteiro no JSON-LD (dados estruturados para o Google)
                var corpo = ArticleBodyDoJsonLd(doc);

                // 2) Senão, lê os parágrafos do container principal do artigo
                if (string.IsNullOrWhiteSpace(corpo))
                    corpo = ParagrafosDoArtigo(doc);

                return string.IsNullOrWhiteSpace(corpo) ? null : LimparTextoRss(corpo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IA] Erro ao ler página da fonte: {ex.Message}");
                return null;
            }
        }

        private static string? ArticleBodyDoJsonLd(HtmlDocument doc)
        {
            var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
            if (scripts == null) return null;

            foreach (var s in scripts)
            {
                try
                {
                    using var json = JsonDocument.Parse(s.InnerText);
                    var corpo = ProcurarArticleBody(json.RootElement);
                    if (!string.IsNullOrWhiteSpace(corpo) && corpo.Length > 300)
                        return WebUtility.HtmlDecode(corpo);
                }
                catch { /* JSON-LD malformado: segue para o próximo */ }
            }

            return null;
        }

        private static string? ProcurarArticleBody(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("articleBody", out var ab) && ab.ValueKind == JsonValueKind.String)
                    return ab.GetString();

                foreach (var p in el.EnumerateObject())
                {
                    var r = ProcurarArticleBody(p.Value);
                    if (r != null) return r;
                }
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                {
                    var r = ProcurarArticleBody(item);
                    if (r != null) return r;
                }
            }

            return null;
        }

        private static string? ParagrafosDoArtigo(HtmlDocument doc)
        {
            // tira o que nunca é texto de matéria
            foreach (var xp in new[] { "//script", "//style", "//nav", "//footer", "//aside", "//form", "//noscript" })
            {
                var nos = doc.DocumentNode.SelectNodes(xp);
                if (nos == null) continue;
                foreach (var n in nos.ToList()) n.Remove();
            }

            var containers = new[]
            {
                "//div[contains(@class,'entry-content')]",
                "//div[contains(@class,'article-body')]",
                "//div[contains(@class,'post-content')]",
                "//div[contains(@class,'content-text')]",
                "//div[contains(@class,'td-post-content')]",
                "//article",
                "//main",
            };

            foreach (var xp in containers)
            {
                var container = doc.DocumentNode.SelectSingleNode(xp);
                if (container == null) continue;

                var texto = JuntarParagrafos(container.SelectNodes(".//p"));
                if (texto.Length >= 300) return texto;
            }

            // último recurso: todos os parágrafos longos da página
            var geral = JuntarParagrafos(doc.DocumentNode.SelectNodes("//p"), minimo: 60);
            return geral.Length >= 300 ? geral : null;
        }

        private static string JuntarParagrafos(HtmlNodeCollection? paragrafos, int minimo = 40)
        {
            if (paragrafos == null) return "";

            return string.Join("\n\n",
                paragrafos
                    .Select(p => WebUtility.HtmlDecode(p.InnerText).Trim())
                    .Where(t => t.Length >= minimo));
        }

        #endregion

        #region Limpeza de texto

        private static string LimparHtml(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";
            return Regex.Replace(texto, "<.*?>", "").Trim();
        }

        private static readonly string[] PadroesLixoRss = new[]
        {
            @"Reprodu[çc][aã]o\s*/[^\n\.]{0,60}",
            @"pic\.twitter\.com\/\S+",
            @"—\s*[^\(@\n]+\(@\w+\)\s+\w+\s+\d{1,2},\s+\d{4}",
            @"@\w{3,}",
            @"O post .+ apareceu primeiro em .+\.",
            @"The post .+ appeared first on .+\.",
            @"Continua após a publicidade.*",
            @"Leia (mais|a matéria) (completa |)n[oa] .+\.",
            @"Acesse o .+ e confira.*",
            @"Veja (mais |)n[oa] .+\.",
            @"Publicado (primeiro |)em .+\.",
            @"^ATENÇÃO:\s*",
            @"\s*ATENÇÃO:\s*$",
            @"🔴?\s*Veja o retrospecto completo de .+",
            @"🔴?\s*Quer saber quem joga\?.+",
            @"🔴?\s*Veja também .+",
            @"📅?\s*Veja também .+",
            @"[\p{So}\p{Sm}]\s*(Veja|Confira|Leia|Quer).{0,80}",
            @"Veja (o retrospecto|também|mais sobre).{0,80}",
            @"Quer saber .{0,80}\?[^\n]*",
            @"Confira (o elenco|o calendário|a tabela).{0,80}",
            @"Fique Atento!.{0,200}",
            @"Qual o horário .+\?",
            @"Como assistir .+\?",
            @"Onde comprar .+\?",
            @"(Veja|Assista|Confira|Olha|Aperte o play (n[oa])?)\s+(o|a|os|as|esse|essa|este|esta|nesse|nessa)?\s*(v[ií]deos?|reels?|stor(y|ies))\b[^\n\.]{0,120}\.?",
        };

        private static string LimparTextoRss(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";

            texto = WebUtility.HtmlDecode(texto);

            var resultado = texto;

            foreach (var padrao in PadroesLixoRss)
                resultado = Regex.Replace(resultado, padrao, "", RegexOptions.IgnoreCase | RegexOptions.Multiline).Trim();

            resultado = Regex.Replace(resultado, @"^\s*[\p{So}\p{Cs}\p{Sm}]+\s*$", "", RegexOptions.Multiline);
            resultado = Regex.Replace(resultado, @"[ \t]{2,}", " ");
            resultado = Regex.Replace(resultado, @"\n{3,}", "\n\n");

            return resultado.Trim();
        }

        #endregion
    }
}