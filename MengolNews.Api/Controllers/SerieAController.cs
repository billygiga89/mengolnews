// MengolNews.Api/Controllers/SerieAController.cs
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class SerieAController : ControllerBase
{
	private readonly SerieAService _service;

	public SerieAController(SerieAService service) => _service = service;

	[HttpGet("standings")]
	public async Task<IActionResult> GetStandings()
	{
		try { return Ok(await _service.GetStandingsAsync()); }
		catch (Exception ex) { return StatusCode(500, ex.Message); }
	}

	[HttpGet("matches")]
	public async Task<IActionResult> GetMatches([FromQuery] int? matchday = null)
	{
		try { return Ok(await _service.GetMatchesAsync(matchday)); }
		catch (Exception ex) { return StatusCode(500, ex.Message); }
	}
}
