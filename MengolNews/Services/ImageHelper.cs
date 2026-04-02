namespace MengolNews.Services
{
	public static class ImageHelper
	{
		public static string ApiBase { get; set; } = "https://mengolnews-api.onrender.com";

		public static string Proxy(string? url, string fallback = "/images/escudo.png")
		{
			if (string.IsNullOrWhiteSpace(url) || url.StartsWith("/"))
				return fallback;

			return $"{ApiBase}/api/imagem?url={Uri.EscapeDataString(url)}";
		}
	}
}
