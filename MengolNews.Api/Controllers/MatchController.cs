using Microsoft.AspNetCore.Mvc;
using MengolNews.Api.Services;

namespace MengolNews.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MatchController : ControllerBase
{
	private readonly MatchService _matchService;

	public MatchController(MatchService matchService)
	{
		_matchService = matchService;
	}

	/// <summary>
	/// GET /api/match/lookup?fdId={id}
	/// Recebe ID da football-data.org, encontra o fixture na API-Football e retorna dados completos.
	/// É esse endpoint que o SerieA.razor deve usar ao clicar num jogo.
	/// </summary>
	[HttpGet("lookup")]
	public async Task<IActionResult> Lookup([FromQuery] int fdId)
	{
		var match = await _matchService.GetMatchByFdIdAsync(fdId);
		if (match is null)
			return NotFound(new { error = "Jogo não encontrado na API-Football." });
		return Ok(match);
	}

	/// <summary>
	/// GET /api/match/{id}
	/// Busca diretamente por fixture ID da API-Football.
	/// </summary>
	[HttpGet("{id:int}")]
	public async Task<IActionResult> GetMatch(int id)
	{
		var match = await _matchService.GetMatchAsync(id);
		if (match is null)
			return NotFound(new { error = "Jogo não encontrado." });
		return Ok(match);
	}

	/// <summary>
	/// GET /api/match/live
	/// Jogos ao vivo no momento.
	/// </summary>
	[HttpGet("live")]
	public async Task<IActionResult> GetLiveMatches()
	{
		var matches = await _matchService.GetLiveMatchesAsync();
		if (matches is null)
			return StatusCode(502, new { error = "Erro ao buscar jogos ao vivo." });
		return Ok(matches);
	}

	/// <summary>
	/// GET /api/match/today
	/// Todos os jogos do dia.
	/// </summary>
	[HttpGet("today")]
	public async Task<IActionResult> GetTodayMatches()
	{
		var matches = await _matchService.GetTodayMatchesAsync();
		if (matches is null)
			return StatusCode(502, new { error = "Erro ao buscar jogos de hoje." });
		return Ok(matches);
	}
}
