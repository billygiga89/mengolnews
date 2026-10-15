using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MengolNews.Api.Controllers
{
    /// <summary>
    /// Diagnóstico temporário das fontes. Não altera nada do site: só testa e mostra um relatório.
    /// Uso: /api/diagnostico?key=CHAVE_DO_WORKER   (apague este arquivo quando terminar)
    /// </summary>
    [ApiController]
    [Route("api/diagnostico")]
    public class DiagnosticoController : ControllerBase
    {
        private const string UaNavegador =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

        private const string UaLeitor =
            "Feedly/1.0 (+http://www.feedly.com/fetcher.html; like FeedFetcher-Google)";

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

        private readonly string? _proxyUrl;
        private readonly string? _chave;

        private sealed record Modo(string Rotulo, bool Leitor, bool Proxy);

        private static readonly Modo[] Modos =
        {
            new("direto/navegador", false, false),
            new("direto/leitor", true, false),
            new("proxy/navegador", false, true),
            new("proxy/leitor", true, true),
        };

        private static readonly (string Nome, string Url)[] Fontes =
        {
            ("ESPN", "https://www.espn.com.br/rss/flamengo.xml"),
            ("NETFLA", "https://netfla.com.br/feed"),
            ("COLUNA DO FLA", "https://colunadofla.com/feed"),
            ("COLUNA DO FLA (com barra)", "https://colunadofla.com/feed/"),
            ("NOTÍCIAS FLA", "https://noticiasfla.com.br/feed"),
            ("URUBU INTERATIVO", "https://urubuinterativo.com/feed/"),
            ("PLACAR", "https://placar.com.br/feed/"),
            ("BOLAVIP", "https://br.bolavip.com/rss/flamengo"),
            ("BOL ESPORTE", "http://rss.bol.uol.com.br/noticias/esporte/rss.xml"),
        };

        public DiagnosticoController(IConfiguration config)
        {
            _proxyUrl = config["Feeds:ProxyUrl"];

            // usa a mesma chave do Worker (que já está dentro do Feeds__ProxyUrl) para proteger a rota
            var m = Regex.Match(_proxyUrl ?? "", @"[?&]key=([^&]+)");
            _chave = m.Success ? Uri.UnescapeDataString(m.Groups[1].Value) : null;
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string? key)
        {
            if (string.IsNullOrEmpty(_chave) || key != _chave)
                return NotFound();

            var resultados = await Task.WhenAll(Fontes.Select(Diagnosticar));

            var sb = new StringBuilder();
            sb.AppendLine($"DIAGNÓSTICO DAS FONTES - {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC");
            sb.AppendLine();
            sb.AppendLine("RESUMO (por quais caminhos cada fonte funciona)");
            foreach (var r in resultados) sb.AppendLine(r.Resumo);
            sb.AppendLine();
            sb.AppendLine("DETALHES");
            sb.AppendLine();
            foreach (var r in resultados) sb.AppendLine(r.Detalhe);

            return Content(sb.ToString(), "text/plain; charset=utf-8");
        }

        private async Task<(string Detalhe, string Resumo)> Diagnosticar((string Nome, string Url) fonte)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {fonte.Nome} ===");
            sb.AppendLine($"FEED  {fonte.Url}");

            var feeds = await Task.WhenAll(Modos.Select(m => Baixar(fonte.Url, true, m)));
            var feedOk = new List<string>();
            string? xml = null;

            for (var i = 0; i < Modos.Length; i++)
            {
                sb.AppendLine($"  {Modos[i].Rotulo,-18} {DescreverFeed(feeds[i].Status, feeds[i].Corpo)}");

                if (EhFeed(feeds[i].Corpo))
                {
                    feedOk.Add(Modos[i].Rotulo);
                    xml ??= feeds[i].Corpo;
                }
            }

            var paginaOk = new List<string>();
            var textoPagina = "não testada";

            if (xml == null)
            {
                sb.AppendLine("  -> nenhum caminho abriu o feed; a página da matéria não foi testada");
            }
            else
            {
                sb.AppendLine($"  texto do 1o item dentro do feed: {TextoDoPrimeiroItem(xml):N0} chars");

                var link = PrimeiroLink(xml);
                if (link == null)
                {
                    sb.AppendLine("  -> não achei o link da 1a matéria no feed");
                }
                else
                {
                    sb.AppendLine($"PÁGINA  {link}");

                    var paginas = await Task.WhenAll(Modos.Select(m => Baixar(link, false, m)));

                    for (var i = 0; i < Modos.Length; i++)
                    {
                        var (texto, ok) = DescreverPagina(paginas[i].Status, paginas[i].Corpo);
                        sb.AppendLine($"  {Modos[i].Rotulo,-18} {texto}");
                        if (ok) paginaOk.Add(Modos[i].Rotulo);
                    }

                    textoPagina = Lista(paginaOk);
                }
            }

            var resumo = $"{fonte.Nome,-26} feed: {Lista(feedOk)} | página: {textoPagina}";
            return (sb.ToString(), resumo);
        }

        // ---------- requisições ----------

        private HttpRequestMessage Requisicao(string url, bool feed, Modo modo)
        {
            var alvo = modo.Proxy ? _proxyUrl + Uri.EscapeDataString(url) : url;
            var req = new HttpRequestMessage(HttpMethod.Get, alvo);

            req.Headers.TryAddWithoutValidation("User-Agent", modo.Leitor ? UaLeitor : UaNavegador);
            req.Headers.TryAddWithoutValidation("Accept-Language", "pt-BR,pt;q=0.9,en;q=0.8");

            if (feed)
            {
                req.Headers.TryAddWithoutValidation("Accept",
                    "application/rss+xml, application/atom+xml, application/xml;q=0.9, text/xml;q=0.8, */*;q=0.5");
            }
            else
            {
                req.Headers.TryAddWithoutValidation("Accept",
                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");

                if (!modo.Leitor)
                {
                    req.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
                    req.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
                }
            }

            if (!modo.Leitor)
                req.Headers.Referrer = new Uri("https://www.google.com/");

            return req;
        }

        private async Task<(string Status, string Corpo)> Baixar(string url, bool feed, Modo modo)
        {
            try
            {
                using var req = Requisicao(url, feed, modo);
                using var resp = await _http.SendAsync(req);

                var bytes = await resp.Content.ReadAsByteArrayAsync();
                return (((int)resp.StatusCode).ToString(), Encoding.UTF8.GetString(bytes));
            }
            catch (TaskCanceledException)
            {
                return ("timeout", "");
            }
            catch (Exception ex)
            {
                return ("erro: " + ex.GetType().Name, "");
            }
        }

        // ---------- leitura dos resultados ----------

        private static bool EhFeed(string corpo)
            => corpo.Contains("<rss", StringComparison.OrdinalIgnoreCase)
               || corpo.Contains("<feed", StringComparison.OrdinalIgnoreCase)
               || corpo.Contains("<rdf:RDF", StringComparison.OrdinalIgnoreCase);

        private static string DescreverFeed(string status, string corpo)
        {
            if (corpo.Length == 0) return status;

            if (EhFeed(corpo))
            {
                var itens = Regex.Matches(corpo, @"<(item|entry)[\s>]", RegexOptions.IgnoreCase).Count;
                return $"{status}  FEED, {itens} itens, {corpo.Length:N0} bytes";
            }

            var previa = Regex.Replace(corpo, @"\s+", " ").Trim();
            if (previa.Length > 90) previa = previa[..90];
            return $"{status}  NÃO É FEED ({corpo.Length:N0} bytes): {previa}";
        }

        private static string? PrimeiroLink(string xml)
        {
            var m = Regex.Match(xml,
                @"<(?:item|entry)[\s>].*?<link[^>]*>\s*(?:<!\[CDATA\[)?\s*(https?://[^<\]\s]+)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
        }

        private static int TextoDoPrimeiroItem(string xml)
        {
            var item = Regex.Match(xml, @"<item[\s>].*?</item>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline).Value;

            var enc = Regex.Match(item, @"<content:encoded>(.*?)</content:encoded>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var desc = Regex.Match(item, @"<description>(.*?)</description>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            var bruto = enc.Success ? enc.Groups[1].Value : desc.Success ? desc.Groups[1].Value : "";

            bruto = bruto.Replace("<![CDATA[", "").Replace("]]>", "");
            bruto = WebUtility.HtmlDecode(bruto);
            bruto = Regex.Replace(bruto, "<.*?>", " ");
            bruto = Regex.Replace(bruto, @"\s+", " ").Trim();

            return bruto.Length;
        }

        private static (string Texto, bool Ok) DescreverPagina(string status, string corpo)
        {
            if (corpo.Length == 0) return (status, false);

            var doc = new HtmlDocument();
            doc.LoadHtml(corpo);

            var paragrafos = doc.DocumentNode.SelectNodes("//p");
            var uteis = paragrafos == null
                ? new List<string>()
                : paragrafos
                    .Select(p => WebUtility.HtmlDecode(p.InnerText).Trim())
                    .Where(t => t.Length >= 40)
                    .ToList();

            var chars = uteis.Sum(t => t.Length);
            var temArticleBody = corpo.Contains("\"articleBody\"");

            var titulo = WebUtility.HtmlDecode(doc.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "").Trim();
            if (titulo.Length > 60) titulo = titulo[..60];

            var extra = temArticleBody ? ", tem articleBody" : "";
            var texto = $"{status}  {corpo.Length:N0} bytes, {uteis.Count} parágrafos úteis ({chars:N0} chars){extra} | título: {titulo}";

            return (texto, chars >= 300 || temArticleBody);
        }

        private static string Lista(List<string> itens)
            => itens.Count == 0 ? "NENHUM" : string.Join(", ", itens);
    }
}
