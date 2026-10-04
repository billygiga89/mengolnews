using MengolNews.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MengolNews.Api.Controllers
{
    [ApiController]
    [Route("api/noticias")]
    public class NoticiasController : ControllerBase
    {
        private readonly NoticiasService _service;
        private readonly ILogger<NoticiasController> _logger;

        public NoticiasController(NoticiasService service, ILogger<NoticiasController> logger)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>
        /// 🔥 Lista todas as notícias (com cache interno)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            try
            {
                var noticias = await _service.GetTodasNoticias();

                if (noticias == null || !noticias.Any())
                {
                    _logger.LogWarning("Nenhuma notícia encontrada.");
                    return NoContent();
                }

                return Ok(noticias);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao buscar notícias.");
                return StatusCode(500, "Erro interno ao buscar notícias.");
            }
        }

        /// <summary>
        /// 🔥 Força atualização ignorando cache
        /// </summary>
        [HttpGet("refresh")]
        public async Task<IActionResult> Refresh()
        {
            try
            {
                _logger.LogInformation("Atualização forçada das notícias.");

                var noticias = await _service.GetTodasNoticias();

                return Ok(new
                {
                    total = noticias.Count,
                    atualizadoEm = DateTime.Now,
                    noticias
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao atualizar notícias.");
                return StatusCode(500, "Erro ao atualizar notícias.");
            }
        }

        /// <summary>
        /// 🔥 Debug rápido (ver se API está viva)
        /// </summary>
        [HttpGet("ping")]
        [HttpHead("ping")]
        public IActionResult Ping()
        {
            return Ok(new
            {
                status = "ok",
                hora = DateTime.Now
            });
        }

        /// <summary>
        /// 🔥 Metadados de uma notícia específica pelo link (usado pelo dynamic rendering / bots)
        /// </summary>
        private async Task<string?> ResolverLinkAsync(string? id, string? url)
        {
            if (!string.IsNullOrWhiteSpace(id))
                return await _service.LinkPorIdAsync(id);

            return string.IsNullOrWhiteSpace(url) ? null : url;
        }

        [HttpGet("meta")]
        public async Task<IActionResult> GetMeta([FromQuery] string? id, [FromQuery] string? url)
        {
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(url))
                return BadRequest("Notícia não informada.");

            try
            {
                var link = await ResolverLinkAsync(id, url);
                if (link == null) return NotFound();

                var noticias = await _service.GetTodasNoticias();
                var noticia = noticias.FirstOrDefault(n => n.Link == link)
                              ?? await _service.BuscarNoArquivoAsync(link);

                if (noticia == null) return NotFound();

                return Ok(new
                {
                    id = NoticiasService.IdDoLink(noticia.Link),
                    titulo = noticia.Titulo,
                    descricao = noticia.Descricao,
                    imagem = noticia.Imagem,
                    data = noticia.Data,
                    fonte = noticia.Fonte,   // mantido por compatibilidade com a Function; vamos tirar depois
                    link = noticia.Link      // idem
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao buscar metadados da notícia.");
                return StatusCode(500, "Erro ao buscar metadados.");
            }
        }

        [HttpGet("conteudo")]
        public async Task<IActionResult> GetConteudo([FromQuery] string? id, [FromQuery] string? url)
        {
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(url))
                return BadRequest("Notícia não informada.");

            try
            {
                var link = await ResolverLinkAsync(id, url);
                if (link == null) return NotFound();

                var conteudo = await _service.ObterConteudoReescritoAsync(link);

                if (string.IsNullOrWhiteSpace(conteudo))
                    return NoContent();

                return Ok(new { conteudo });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao buscar conteúdo da notícia.");
                return StatusCode(500, "Erro ao buscar conteúdo.");
            }
        }

        [HttpGet("arquivo")]
        public async Task<IActionResult> GetDoArquivo([FromQuery] string? id, [FromQuery] string? url)
        {
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(url))
                return BadRequest("Notícia não informada.");

            try
            {
                var link = await ResolverLinkAsync(id, url);
                if (link == null) return NotFound();

                var noticia = await _service.BuscarNoArquivoAsync(link);
                if (noticia == null) return NotFound();

                return Ok(noticia);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao buscar notícia arquivada.");
                return StatusCode(500, "Erro ao buscar notícia arquivada.");
            }
        }
    }
}