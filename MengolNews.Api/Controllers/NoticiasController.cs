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
		public IActionResult Ping()
		{
			return Ok(new
			{
				status = "ok",
				hora = DateTime.Now
			});
		}

		/// <summary>
		/// 🔥 Busca conteúdo completo de uma notícia pelo link
		/// </summary>
		[HttpGet("conteudo")]
		public async Task<IActionResult> GetConteudo([FromQuery] string url)
		{
			if (string.IsNullOrWhiteSpace(url))
				return BadRequest("URL não informada.");

			try
			{
				var conteudo = await _service.ExtrairConteudoDaPaginaAsync(url);

				if (string.IsNullOrWhiteSpace(conteudo))
					return NoContent();

				return Ok(new { conteudo });
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Erro ao buscar conteúdo da URL: {url}", url);
				return StatusCode(500, "Erro ao buscar conteúdo.");
			}
		}
	}
}

