using MengolNews.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MengolNews.Api.Controllers
{
	[ApiController]
	[Route("api/videos")]
	public class VideosController : ControllerBase
	{
		private readonly VideosService _service;
		private readonly ILogger<VideosController> _logger;

		public VideosController(VideosService service, ILogger<VideosController> logger)
		{
			_service = service;
			_logger = logger;
		}

		/// <summary>
		/// Lista os vídeos mais recentes do canal do YouTube
		/// </summary>
		[HttpGet]
		public async Task<IActionResult> Get([FromQuery] int max = 20)
		{
			try
			{
				var videos = await _service.GetVideosDoCanal(max);
				if (videos == null || !videos.Any())
				{
					_logger.LogWarning("Nenhum vídeo encontrado.");
					return NoContent();
				}
				return Ok(videos);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Erro ao buscar vídeos.");
				return StatusCode(500, "Erro interno ao buscar vídeos.");
			}
		}

		/// <summary>
		/// Força atualização ignorando cache
		/// </summary>
		[HttpGet("refresh")]
		public async Task<IActionResult> Refresh()
		{
			try
			{
				_logger.LogInformation("Atualização forçada dos vídeos.");
				var videos = await _service.GetVideosDoCanal(20, forcarAtualizacao: true);
				return Ok(new
				{
					total = videos.Count,
					atualizadoEm = DateTime.Now,
					videos
				});
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Erro ao atualizar vídeos.");
				return StatusCode(500, "Erro ao atualizar vídeos.");
			}
		}

		/// <summary>
		/// Debug rápido
		/// </summary>
		[HttpGet("ping")]
		public IActionResult Ping() => Ok(new { status = "ok", hora = DateTime.Now });
	}
}

