namespace MengolNews.Services
{
	public static class ImageHelper
	{
		public static string ApiBase { get; set; } = "https://mengolnews-api.onrender.com";

        public static string Proxy(string? url, string fallback = "/images/Flamengo.png")
        {
            if (string.IsNullOrWhiteSpace(url))
                return fallback;

            if (url.StartsWith("/"))
                return url;

            return $"{ApiBase}/api/imagem?url={Uri.EscapeDataString(url)}";
        }
    }
}
