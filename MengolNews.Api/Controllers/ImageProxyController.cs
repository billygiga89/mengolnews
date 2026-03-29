using Microsoft.AspNetCore.Mvc;

namespace MengolNews.Api.Controllers
{
	[ApiController]
	[Route("api/imagem")]
	public class ImageProxyController : ControllerBase
	{
		private readonly HttpClient _http;

		public ImageProxyController(IHttpClientFactory factory)
		{
			_http = factory.CreateClient("default");

			_http.DefaultRequestHeaders.UserAgent.ParseAdd(
				"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
				"(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
			);

			_http.DefaultRequestHeaders.Accept.ParseAdd(
				"image/avif,image/webp,image/apng,image/*,*/*;q=0.8"
			);

			_http.DefaultRequestHeaders.AcceptLanguage.ParseAdd(
				"pt-BR,pt;q=0.9,en;q=0.8"
			);
		}

		[HttpGet]
		public async Task<IActionResult> Get([FromQuery] string url)
		{
			if (string.IsNullOrWhiteSpace(url))
				return BadRequest("URL inválida");

			try
			{
				var request = new HttpRequestMessage(HttpMethod.Get, url);

				// 🔥 ESSENCIAL (resolve 80% dos bloqueios)
				request.Headers.Referrer = new Uri("https://www.google.com");

				// 🔥 ALGUNS SITES EXIGEM
				request.Headers.Add("Origin", "https://www.google.com");

				using var response = await _http.SendAsync(
					request,
					HttpCompletionOption.ResponseHeadersRead
				);

				if (!response.IsSuccessStatusCode)
					return NotFound();

				var contentType = response.Content.Headers.ContentType?.ToString();

				if (string.IsNullOrWhiteSpace(contentType) || !contentType.StartsWith("image/"))
					return NotFound();

				var stream = await response.Content.ReadAsStreamAsync();

				return File(stream, contentType);
			}
			catch
			{
				return NotFound();
			}
		}
	}
}
