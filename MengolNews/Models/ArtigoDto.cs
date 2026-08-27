namespace MengolNews.Models;

public class ArtigoDto
{
    public string Slug { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Imagem { get; set; } = "";
    public DateTime DataPublicacao { get; set; }
    public string ConteudoHtml { get; set; } = "";
}
