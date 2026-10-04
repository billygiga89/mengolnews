namespace MengolNews.Api.Models
{
	public class NoticiaDto
	{
		public string Titulo { get; set; } = "";
		public string Descricao { get; set; } = "";
		public string Conteudo { get; set; } = "";
		public string Fonte { get; set; } = "";
		public DateTime Data { get; set; }
		public string Link { get; set; } = "";
		public string? Imagem { get; set; }
        public string Id => Convert.ToHexString(
			System.Security.Cryptography.SHA256.HashData(
				System.Text.Encoding.UTF8.GetBytes(Link ?? ""))).ToLowerInvariant();
    }
}
