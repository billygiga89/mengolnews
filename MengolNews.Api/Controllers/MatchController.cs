using Microsoft.AspNetCore.Mvc;
using MengolNews.Api.Services;

namespace MengolNews.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MatchController : ControllerBase
{
	private readonly MatchService _matchService;

	public MatchController(MatchService matchService)
		=> _matchService = matchService;

	[HttpGet("{fdId:int}")]
	public async Task<IActionResult> GetMatch(int fdId)
	{
		var match = await _matchService.GetMatchByFdIdAsync(fdId);
		if (match is null)
			return NotFound(new { error = "Jogo não encontrado." });
		return Ok(match);
	}

	[HttpGet("live")]
	public async Task<IActionResult> GetLiveMatches()
	{
		var matches = await _matchService.GetLiveMatchesAsync();
		if (matches is null)
			return StatusCode(502, new { error = "Erro ao buscar jogos ao vivo." });
		return Ok(matches);
	}
}