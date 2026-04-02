using System.Text.Json;

namespace MengolNews.Api.Services
{
	public class VideoItem
	{
		public string VideoId { get; set; } = "";
		public string Titulo { get; set; } = "";
		public string Descricao { get; set; } = "";
		public string Thumbnail { get; set; } = "";
		public string PublicadoEm { get; set; } = "";
		public string UrlEmbed => $"https://www.youtube.com/embed/{VideoId}?controls=1&modestbranding=1&rel=0&showinfo=0";
	}

	public class VideosService
	{
		private readonly HttpClient _http;
		private readonly IConfiguration _config;
		private readonly ILogger<VideosService> _logger;

		private List<VideoItem>? _cache;
		private DateTime _cacheExpira = DateTime.MinValue;
		private readonly TimeSpan _cacheDuracao = TimeSpan.FromMinutes(30);

		public VideosService(HttpClient http, IConfiguration config, ILogger<VideosService> logger)
		{
			_http = http;
			_config = config;
			_logger = logger;
		}

		public async Task<List<VideoItem>> GetVideosDoCanal(int max = 20, bool forcarAtualizacao = false)
		{
			if (!forcarAtualizacao && _cache != null && DateTime.Now < _cacheExpira)
			{
				_logger.LogInformation("Retornando vídeos do cache.");
				return _cache;
			}

			var apiKey = _config["YouTube:ApiKey"];
			var canalId = _config["YouTube:ChannelId"];

			var url = $"https://www.googleapis.com/youtube/v3/search" +
					  $"?key={apiKey}" +
					  $"&channelId={canalId}" +
					  $"&part=snippet" +
					  $"&order=date" +
					  $"&type=video" +
					  $"&maxResults={max}";

			var response = await _http.GetAsync(url);
			response.EnsureSuccessStatusCode();

			var json = await response.Content.ReadAsStringAsync();
			var doc = JsonDocument.Parse(json);
			var items = doc.RootElement.GetProperty("items");

			var videos = new List<VideoItem>();

			foreach (var item in items.EnumerateArray())
			{
				var snippet = item.GetProperty("snippet");
				var videoId = item.GetProperty("id").GetProperty("videoId").GetString() ?? "";

				var thumbs = snippet.GetProperty("thumbnails");
				var thumb = thumbs.TryGetProperty("maxres", out var maxres) ? maxres :
							thumbs.TryGetProperty("high", out var high) ? high :
							thumbs.GetProperty("medium");

				videos.Add(new VideoItem
				{
					VideoId = videoId,
					Titulo = snippet.GetProperty("title").GetString() ?? "",
					Descricao = snippet.GetProperty("description").GetString() ?? "",
					Thumbnail = thumb.GetProperty("url").GetString() ?? "",
					PublicadoEm = snippet.GetProperty("publishedAt").GetString() ?? ""
				});
			}

			_cache = videos;
			_cacheExpira = DateTime.Now.Add(_cacheDuracao);

			_logger.LogInformation("Vídeos buscados do YouTube: {Total}", videos.Count);
			return videos;
		}
	}
}

