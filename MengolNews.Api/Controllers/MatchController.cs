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
	/// GET /api/match/{id}
	/// Returns full match data: score, events, lineups, stats
	/// </summary>
	[HttpGet("{id:int}")]
	public async Task<IActionResult> GetMatch(int id)
	{
		var match = await _matchService.GetMatchAsync(id);
		if (match is null) return NotFound(new { error = "Jogo não encontrado" });
		return Ok(match);
	}

	/// <summary>
	/// GET /api/match/live
	/// Returns all currently live matches
	/// </summary>
	[HttpGet("live")]
	public async Task<IActionResult> GetLiveMatches()
	{
		var matches = await _matchService.GetLiveMatchesAsync();
		if (matches is null) return StatusCode(502, new { error = "Erro ao buscar jogos ao vivo" });
		return Ok(matches);
	}

	/// <summary>
	/// GET /api/match/today
	/// Returns all matches today (live + scheduled + finished)
	/// </summary>
	[HttpGet("today")]
	public async Task<IActionResult> GetTodayMatches()
	{
		var matches = await _matchService.GetTodayMatchesAsync();
		if (matches is null) return StatusCode(502, new { error = "Erro ao buscar jogos de hoje" });
		return Ok(matches);
	}
}
