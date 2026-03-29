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
	}
}
