using HtmlAgilityPack;
using MengolNews.Api.Models;
using System.Globalization;
using System.Net;
using System.ServiceModel.Syndication;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace MengolNews.Api.Services
{
    public class NoticiasService
    {
        private readonly HttpClient _http;

        // CACHE
        private List<NoticiaDto>? _cache;
        private DateTime _ultimaAtualizacao;
        private static readonly TimeSpan _cacheDuracao = TimeSpan.FromMinutes(10);

        // Limita scraping paralelo para não sobrecarregar
        private static readonly SemaphoreSlim _semaforo = new(5, 5);

        public NoticiasService(HttpClient http)
        {
            _http = http;
            _http.Timeout = TimeSpan.FromSeconds(15);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36"
            );
            _http.DefaultRequestHeaders.Add("Accept", "application/rss+xml, application/xml, text/xml, */*");
            _http.DefaultRequestHeaders.Add("Accept-Language", "pt-BR,pt;q=0.9,en;q=0.8");
        }

        public async Task<List<NoticiaDto>> GetTodasNoticias()
        {
            if (_cache != null && DateTime.Now - _ultimaAtualizacao < _cacheDuracao)
                return _cache;

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

            Console.WriteLine($"TOTAL FINAL: {noticias.Count}");

            _cache = noticias;
            _ultimaAtualizacao = DateTime.Now;

            return noticias;
        }

        /* =======================
           TIMEZONE BRASÍLIA
        ======================= */

        private static DateTime ConverterParaBrasilia(DateTime utc)
        {
            try
            {
                // Windows
                var tz = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
            }
            catch
            {
                try
                {
                    // Linux/Docker
                    var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
                    return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
                }
                catch
                {
                    return utc.AddHours(-3); // fallback manual
                }
            }
        }

        /* =======================
           FONTES
        ======================= */

        private Task<List<NoticiaDto>> GetEspnNoticias()
            => LerRss("https://www.espn.com.br/rss/flamengo.xml", "ESPN", filtrarFlamengo: true);

        private Task<List<NoticiaDto>> GetColunaDoFla()
            => LerRss("https://colunadofla.com/feed", "COLUNA DO FLA", filtrarFlamengo: false);

        private Task<List<NoticiaDto>> GetUrubuInterativo()
            => LerRss("https://noticiasfla.com.br/feed", "NOTÍCIAS FLA", filtrarFlamengo: false);

        private Task<List<NoticiaDto>> GetLanceNoticias()
            => LerRss("https://br.bolavip.com/rss/flamengo", "BOLAVIP", filtrarFlamengo: false);

        private Task<List<NoticiaDto>> GetNetFla()
            => LerRss("https://netfla.com.br/feed", "NETFLA", filtrarFlamengo: false);

        private Task<List<NoticiaDto>> GetFlamengoRj()
             => LerRss("https://urubuinterativo.com/feed/", "URUBU INTERATIVO", filtrarFlamengo: false);

        // Fontes gerais de esporte — precisam filtrar só notícias do Flamengo
        private Task<List<NoticiaDto>> GetPlacar()
            => LerRss("https://placar.com.br/feed/", "PLACAR", filtrarFlamengo: true);

        private Task<List<NoticiaDto>> GetBolEsporte()
            => LerRss("http://rss.bol.uol.com.br/noticias/esporte/rss.xml", "BOL ESPORTE", filtrarFlamengo: true);

        /* =======================
           LEITOR RSS
        ======================= */

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

                using var _ = response; // garante Dispose ao sair do escopo

                if (response == null || !response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[{fonte}] ❌ Falhou com status {(int)(response?.StatusCode ?? 0)} após {maxTentativas} tentativas");
                    return lista;
                }

                var xml = await response.Content.ReadAsStringAsync();

                // O Rss20FeedFormatter só aceita version="2.0". Feeds antigos (ex: BOL Esporte)
                // ainda usam RSS 0.9x, que é compatível o suficiente nos campos que usamos
                // (title, link, description, pubDate) — só normalizamos o atributo de versão.
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

                    var imagem = NormalizarImagem(ExtrairImagem(item), url);

                    if (EhImagemInvalida(imagem))
                    {
                        imagem = ""; // descarta placeholder da fonte (ex: noimg.jpg do NetFla)

                        if (!string.IsNullOrWhiteSpace(link))
                        {
                            await _semaforo.WaitAsync();
                            try
                            {
                                var imgPagina = await ExtrairImagemDaPaginaAsync(link);
                                if (!EhImagemInvalida(imgPagina))
                                    imagem = imgPagina!;
                            }
                            finally
                            {
                                _semaforo.Release();
                            }
                        }
                    }

                    // ✅ Converte data para horário de Brasília
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
                        Imagem = string.IsNullOrWhiteSpace(imagem) ? "" : imagem
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

        /* =======================
           FILTRO FLAMENGO
        ======================= */

        private bool EhRelacionadoAoFlamengo(string titulo, string descricao)
        {
            var palavras = new[] { "Flamengo", "Fla", "Mengão", "Rubro-Negro", "CRF" };

            return palavras.Any(p =>
                titulo.Contains(p, StringComparison.OrdinalIgnoreCase) ||
                descricao.Contains(p, StringComparison.OrdinalIgnoreCase)
            );
        }

        /* =======================
           DEDUPLICAÇÃO POR SIMILARIDADE DE TÍTULO
        ======================= */

        // Ordem de preferência de fontes usada como critério de desempate quando
        // duas notícias de fontes diferentes são consideradas a mesma matéria.
        // Quanto mais no topo, mais prioridade. Reordene à vontade.
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

        // Similaridade mínima (Jaccard) entre os tokens de dois títulos para
        // serem tratados como a mesma notícia. Suba para 0.7 se estiver
        // agrupando notícias diferentes demais; desça para 0.5 se ainda
        // passarem duplicatas.
        private const double LimiarSimilaridadeTitulo = 0.5;

        private static readonly HashSet<string> StopWordsTitulo = new(StringComparer.OrdinalIgnoreCase)
        {
            "a","o","os","as","de","da","do","das","dos","e","em","no","na","nos","nas",
            "para","por","com","um","uma","que","é","ao","à","se","sobre","apos","antes",
            "flamengo","fla"
        };

        private List<NoticiaDto> RemoverDuplicadas(List<NoticiaDto> noticias)
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

        private HashSet<string> TokenizarTitulo(string titulo)
        {
            var texto = RemoverAcentos(titulo.ToLowerInvariant());
            texto = Regex.Replace(texto, @"[^a-z0-9\s]", " ");

            return texto
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 2 && !StopWordsTitulo.Contains(t))
                .ToHashSet();
        }

        private double CalcularSimilaridade(HashSet<string> a, HashSet<string> b)
        {
            if (a.Count == 0 || b.Count == 0) return 0;

            var intersecao = a.Intersect(b).Count();
            var uniao = a.Union(b).Count();

            return (double)intersecao / uniao; // índice de Jaccard
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

        private int PrioridadeFonte(string fonte)
        {
            var idx = PrioridadeFontes.FindIndex(f => string.Equals(f, fonte, StringComparison.OrdinalIgnoreCase));
            return idx == -1 ? PrioridadeFontes.Count : idx; // fontes não listadas ficam por último
        }

        private bool EhMelhorVersao(NoticiaDto candidata, NoticiaDto atual)
        {
            var prioridadeCandidata = PrioridadeFonte(candidata.Fonte);
            var prioridadeAtual = PrioridadeFonte(atual.Fonte);

            // Fonte com maior prioridade (número menor) vence direto.
            if (prioridadeCandidata != prioridadeAtual)
                return prioridadeCandidata < prioridadeAtual;

            // Mesma prioridade: desempata por qualidade de conteúdo (imagem + descrição).
            var pontosCandidata = (string.IsNullOrWhiteSpace(candidata.Imagem) ? 0 : 1)
                + (candidata.Descricao?.Length ?? 0) / 100;

            var pontosAtual = (string.IsNullOrWhiteSpace(atual.Imagem) ? 0 : 1)
                + (atual.Descricao?.Length ?? 0) / 100;

            return pontosCandidata > pontosAtual;
        }

        /* =======================
           DETECTA IMAGEM PLACEHOLDER DA FONTE
        ======================= */

        private static readonly string[] PadroesImagemInvalida = new[]
        {
            "noimg.jpg",
            "no-image",
            "sem-imagem",
            "placeholder",
            "default.jpg",
        };

        private bool EhImagemInvalida(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return true;
            return PadroesImagemInvalida.Any(p => url.Contains(p, StringComparison.OrdinalIgnoreCase));
        }

        /* =======================
           CONTEÚDO DA PÁGINA
        ======================= */

        public async Task<string?> ExtrairConteudoDaPaginaAsync(string url)
        {
            try
            {
                var web = new HtmlWeb();
                var doc = await web.LoadFromWebAsync(url);

                HtmlNodeCollection? paragrafos = null;

                if (url.Contains("espn.com.br"))
                    paragrafos = doc.DocumentNode.SelectNodes("//div[contains(@class,'article-body')]//p");
                else if (url.Contains("ge.globo.com"))
                    paragrafos = doc.DocumentNode.SelectNodes("//div[contains(@class,'content-text')]//p");
                else if (url.Contains("lance.com.br"))
                    paragrafos = doc.DocumentNode.SelectNodes("//div[contains(@class,'content')]//p");
                else if (url.Contains("colunadofla.com"))
                    paragrafos = doc.DocumentNode.SelectNodes("//div[contains(@class,'entry-content')]//p");
                else if (url.Contains("urubuinterativo.com"))
                    paragrafos = doc.DocumentNode.SelectNodes("//div[contains(@class,'entry-content')]//p");
                else if (url.Contains("flanoticias.com.br"))
                    paragrafos = doc.DocumentNode.SelectNodes("//div[contains(@class,'entry-content')]//p");
                else if (url.Contains("placar.com.br"))
                    paragrafos = doc.DocumentNode.SelectNodes("//div[contains(@class,'entry-content')]//p | //article//p");
                else
                    paragrafos = doc.DocumentNode.SelectNodes("//article//p | //div[contains(@class,'content')]//p");

                if (paragrafos == null) return null;

                var conteudo = string.Join("\n\n",
                    paragrafos
                        .Select(p => System.Net.WebUtility.HtmlDecode(p.InnerText.Trim()))
                        .Where(t => t.Length > 50)
                );

                return LimparTextoRss(conteudo);
            }
            catch
            {
                return null;
            }
        }

        /* =======================
           IMAGEM DA PÁGINA
        ======================= */

        private async Task<string?> ExtrairImagemDaPaginaAsync(string url)
        {
            try
            {
                var web = new HtmlWeb { };
                web.PreRequest += req => { req.Timeout = 5000; return true; };
                var doc = await web.LoadFromWebAsync(url);

                var ogImage = doc.DocumentNode
                    .SelectSingleNode("//meta[@property='og:image'] | //meta[@name='og:image']");

                if (ogImage != null)
                {
                    var content = ogImage.GetAttributeValue("content", null);
                    if (!string.IsNullOrWhiteSpace(content))
                        return content;
                }

                var twitterImage = doc.DocumentNode
                    .SelectSingleNode("//meta[@name='twitter:image']");

                if (twitterImage != null)
                {
                    var content = twitterImage.GetAttributeValue("content", null);
                    if (!string.IsNullOrWhiteSpace(content))
                        return content;
                }

                var img = doc.DocumentNode
                    .SelectSingleNode("//article//img | //div[contains(@class,'content')]//img");

                return PegarImagem(img);
            }
            catch
            {
                return null;
            }
        }

        private string? PegarImagem(HtmlNode? img)
        {
            if (img == null) return null;

            return img.GetAttributeValue("src", null)
                ?? img.GetAttributeValue("data-src", null)
                ?? img.GetAttributeValue("data-lazy-src", null);
        }

        /* =======================
           IMAGEM DO RSS
        ======================= */

        private string? ExtrairImagem(SyndicationItem item)
        {
            var media = item.ElementExtensions
                .ReadElementExtensions<XmlElement>("content", "http://search.yahoo.com/mrss/")
                .FirstOrDefault();

            if (media?.HasAttribute("url") == true)
                return media.GetAttribute("url");

            var enclosure = item.Links.FirstOrDefault(l =>
                l.RelationshipType == "enclosure" &&
                (l.MediaType?.StartsWith("image") == true));

            if (enclosure != null)
                return enclosure.Uri.ToString();

            var html = item.Summary?.Text;
            if (string.IsNullOrWhiteSpace(html)) return null;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var img = doc.DocumentNode.SelectSingleNode("//img");
            return img?.GetAttributeValue("src", null);
        }

        /* =======================
           NORMALIZA IMAGEM
        ======================= */

        private string NormalizarImagem(string? url, string baseUrl)
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

        /* =======================
           LIMPAR HTML
        ======================= */

        private string LimparHtml(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";
            return Regex.Replace(texto, "<.*?>", "").Trim();
        }

        /* =======================
           LIMPAR TEXTO RSS
        ======================= */

        private string LimparTextoRss(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";
            texto = WebUtility.HtmlDecode(texto);
            var padroes = new[]
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
            };

            var resultado = texto;

            foreach (var padrao in padroes)
                resultado = Regex.Replace(resultado, padrao, "", RegexOptions.IgnoreCase | RegexOptions.Multiline).Trim();

            resultado = Regex.Replace(resultado, @"^\s*[\p{So}\p{Cs}\p{Sm}]+\s*$", "", RegexOptions.Multiline);
            resultado = Regex.Replace(resultado, @"[ \t]{2,}", " ");
            resultado = Regex.Replace(resultado, @"\n{3,}", "\n\n");

            return resultado.Trim();
        }
    }
}