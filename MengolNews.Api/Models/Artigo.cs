namespace MengolNews.Api.Models;

public class Artigo
{
    public string Slug { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Imagem { get; set; } = "";
    public DateTime DataPublicacao { get; set; }
    public string ConteudoMarkdown { get; set; } = "";
}
