using Microsoft.AspNetCore.Mvc;
using MengolNews.Api.Services;

namespace MengolNews.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SquadController : ControllerBase
{
	private readonly SquadService _squadService;

	public SquadController(SquadService squadService)
	{
		_squadService = squadService;
	}

	/// <summary>
	/// GET /api/squad/flamengo
	/// Retorna elenco atual do Flamengo (nome, posição, foto, número, idade)
	/// </summary>
	[HttpGet("flamengo")]
	public async Task<IActionResult> GetFlamengoSquad()
	{
		var squad = await _squadService.GetFlamengoSquadAsync();
		if (squad is null) return StatusCode(502, new { error = "Erro ao buscar elenco" });
		return Ok(squad);
	}

	/// <summary>
	/// GET /api/squad/player/{id}
	/// Retorna estatísticas completas da temporada de um jogador
	/// </summary>
	[HttpGet("player/{id:int}")]
	public async Task<IActionResult> GetPlayerStats(int id)
	{
		var stats = await _squadService.GetPlayerStatsAsync(id);
		if (stats is null) return NotFound(new { error = "Jogador não encontrado" });
		return Ok(stats);
	}
}

