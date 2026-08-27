using System.Text.Json;
using Markdig;
using Microsoft.AspNetCore.Mvc;
using MengolNews.Api.Models;

namespace MengolNews.Api.Controllers;

[ApiController]
[Route("api/artigos")]
public class ArtigosController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public ArtigosController(IWebHostEnvironment env)
    {
        _env = env;
    }

    private List<Artigo> CarregarArtigos()
    {
        var caminho = Path.Combine(_env.ContentRootPath, "Data", "artigos.json");
        var json = System.IO.File.ReadAllText(caminho);
        return JsonSerializer.Deserialize<List<Artigo>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new List<Artigo>();
    }

    [HttpGet]
    public IActionResult ObterTodos()
    {
        var artigos = CarregarArtigos()
            .OrderByDescending(a => a.DataPublicacao)
            .Select(a => new { a.Slug, a.Titulo, a.Imagem, a.DataPublicacao })
            .ToList();

        return Ok(artigos);
    }

    [HttpGet("{slug}")]
    public IActionResult ObterPorSlug(string slug)
    {
        var artigo = CarregarArtigos()
            .FirstOrDefault(a => a.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));

        if (artigo == null)
            return NotFound();

        return Ok(new
        {
            artigo.Slug,
            artigo.Titulo,
            artigo.Imagem,
            artigo.DataPublicacao,
            ConteudoHtml = Markdown.ToHtml(artigo.ConteudoMarkdown, Pipeline)
        });
    }
}
