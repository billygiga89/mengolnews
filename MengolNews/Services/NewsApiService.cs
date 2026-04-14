using MengolNews.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace MengolNews.Services
{
	public class NewsApiService
	{
		private readonly HttpClient _http;

		private List<Noticia>? cache;
		private DateTime cacheTime;

		private readonly TimeSpan cacheDuration = TimeSpan.FromMinutes(5);

		public NewsApiService(HttpClient http)
		{
			_http = http;
		}

		public async Task<List<Noticia>> GetNoticiasAsync()
		{
			if (cache != null && DateTime.Now - cacheTime < cacheDuration)
				return cache;

			const int maxTentativas = 3;
			for (int tentativa = 1; tentativa <= maxTentativas; tentativa++)
			{
				try
				{
					using var response = await _http.GetAsync("api/noticias");
					if (!response.IsSuccessStatusCode)
					{
						if (tentativa < maxTentativas)
							await Task.Delay(TimeSpan.FromSeconds(2));
						continue;
					}

					var stream = await response.Content.ReadAsStreamAsync();
					cache = await JsonSerializer.DeserializeAsync<List<Noticia>>(stream,
						new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
						?? new List<Noticia>();
					cacheTime = DateTime.Now;
					return cache;
				}
				catch
				{
					if (tentativa < maxTentativas)
						await Task.Delay(TimeSpan.FromSeconds(3 * tentativa));
				}
			}

			return cache ?? new List<Noticia>();
		}
	}
}



