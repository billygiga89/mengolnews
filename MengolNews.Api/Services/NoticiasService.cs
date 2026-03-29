using HtmlAgilityPack;
using MengolNews.Api.Models;
using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Xml;

namespace MengolNews.Api.Services
{
	public class NoticiasService
	{
		private readonly HttpClient _http;

		// CACHE (instância — compatível com Singleton)
		private List<NoticiaDto>? _cache;
		private DateTime _ultimaAtualizacao;
		private static readonly TimeSpan _cacheDuracao = TimeSpan.FromMinutes(10);

		// Limita scraping paralelo para não sobrecarregar
		private static readonly SemaphoreSlim _semaforo = new(5, 5);

		public NoticiasService(HttpClient http)
		{
			_http = http;
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
				GetPlacarNoticias(),
				GetFalandoDeFlaRss(),
				GetBrazilFootyNoticias(),
			};

			var resultados = await Task.WhenAll(tarefas.Select(async t =>
			{
				try { return await t; }
				catch (Exception ex)
				{
#if DEBUG
					Console.WriteLine($"Erro em fonte: {ex.Message}");
#endif
					return new List<NoticiaDto>();
				}
			}));

			var noticias = resultados
				.SelectMany(r => r.Take(15))
				.GroupBy(n => n.Titulo.ToLower().Trim())
				.Select(g => g.First())
				.OrderByDescending(n => n.Data)
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
           FONTES
        ======================= */

		private Task<List<NoticiaDto>> GetEspnNoticias()
			=> LerRss("https://www.espn.com.br/rss/flamengo.xml", "ESPN", filtrarFlamengo: true);

		private Task<List<NoticiaDto>> GetPlacarNoticias()
			=> LerRss("https://placar.com.br/feed", "PLACAR", filtrarFlamengo: true);

		private Task<List<NoticiaDto>> GetFalandoDeFlaRss()
			=> LerRss("https://falandodeflamengo.wordpress.com/feed/", "FALANDO DE FLA", filtrarFlamengo: false);

		private Task<List<NoticiaDto>> GetBrazilFootyNoticias()
			=> LerRss("https://brazilfooty.com/feed", "BRAZIL FOOTY", filtrarFlamengo: false);

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
				using var request = new HttpRequestMessage(HttpMethod.Get, url);

				if (headersExtras != null)
					foreach (var kv in headersExtras)
						request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);

				using var response = await _http.SendAsync(request);

#if DEBUG
				Console.WriteLine($"[{fonte}] Status: {(int)response.StatusCode}");
#endif

				if (!response.IsSuccessStatusCode)
					return lista;

				using var stream = await response.Content.ReadAsStreamAsync();
				using var reader = XmlReader.Create(stream);

				var feed = SyndicationFeed.Load(reader);
				if (feed == null) return lista;

				var itensBase = new List<(SyndicationItem item, string titulo, string descricao, string link)>();

				foreach (var item in feed.Items)
				{
					var titulo = item.Title?.Text ?? "";
					var descricaoBruta = item.Summary?.Text ?? "";
					var link = item.Links.FirstOrDefault()?.Uri.ToString() ?? "";

					if (filtrarFlamengo && !EhRelacionadoAoFlamengo(titulo, descricaoBruta))
						continue;

					// ✅ Limpa HTML e textos automáticos do WordPress
					var descricao = LimparTextoRss(LimparHtml(descricaoBruta));

					itensBase.Add((item, titulo, descricao, link));
				}

				// Scraping de imagem em paralelo com limite de concorrência
				var tarefasImagem = itensBase.Select(async entry =>
				{
					var (item, titulo, descricao, link) = entry;

					var imagem = NormalizarImagem(ExtrairImagem(item), url);

					if (string.IsNullOrWhiteSpace(imagem) && !string.IsNullOrWhiteSpace(link))
					{
						await _semaforo.WaitAsync();
						try
						{
							var imgPagina = await ExtrairImagemDaPaginaAsync(link);
							if (!string.IsNullOrWhiteSpace(imgPagina))
								imagem = imgPagina;
						}
						finally
						{
							_semaforo.Release();
						}
					}

					return new NoticiaDto
					{
						Titulo = titulo,
						Descricao = descricao,
						Conteudo = descricao,
						Link = link,
						Fonte = fonte,
						Data = item.PublishDate.UtcDateTime == DateTime.MinValue
							? DateTime.UtcNow
							: item.PublishDate.UtcDateTime,
						Imagem = string.IsNullOrWhiteSpace(imagem)
							? "/images/escudo.png"
							: imagem
					};
				});

				lista = (await Task.WhenAll(tarefasImagem)).ToList();
			}
			catch (Exception ex)
			{
#if DEBUG
				Console.WriteLine($"Erro ao ler RSS {fonte}: {ex.Message}");
#endif
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
				else if (url.Contains("placar.com.br"))
					paragrafos = doc.DocumentNode.SelectNodes("//div[contains(@class,'content-text')]//p");
				else if (url.Contains("wordpress.com") || url.Contains("falandodeflamengo"))
					paragrafos = doc.DocumentNode.SelectNodes("//div[contains(@class,'entry-content')]//p");
				else if (url.Contains("brazilfooty.com"))
					paragrafos = doc.DocumentNode.SelectNodes("//div[contains(@class,'entry-content')]//p");
				else
					paragrafos = doc.DocumentNode.SelectNodes("//article//p | //div[contains(@class,'content')]//p");

				if (paragrafos == null) return null;

				var conteudo = string.Join("\n\n",
					paragrafos
						.Select(p => p.InnerText.Trim())
						.Where(t => t.Length > 50)
				);

				// ✅ Limpa textos automáticos do conteúdo também
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
				var web = new HtmlWeb();
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

			var padroes = new[]
			{
				@"O post .+ apareceu primeiro em .+\.",
				@"The post .+ appeared first on .+\.",
				@"Continua após a publicidade.*",
				@"Leia (mais|a matéria) (completa |)n[oa] .+\.",
				@"Acesse o .+ e confira.*",
				@"Veja (mais |)n[oa] .+\.",
				@"Publicado (primeiro |)em .+\.",
			};

			var resultado = texto;
			foreach (var padrao in padroes)
				resultado = Regex.Replace(resultado, padrao, "", RegexOptions.IgnoreCase).Trim();

			return resultado;
		}
	}
}