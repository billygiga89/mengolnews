using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace MengolNews.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MatchDebugController : ControllerBase
{
	private readonly IHttpClientFactory _factory;
	private readonly IConfiguration _config;

	public MatchDebugController(IHttpClientFactory factory, IConfiguration config)
	{
		_factory = factory;
		_config = config;
	}

	/// <summary>
	/// GET /api/matchdebug?fdId=554851
	/// Mostra passo a passo o que está acontecendo na busca.
	/// REMOVA esse controller após resolver o problema!
	/// </summary>
	[HttpGet]
	public async Task<IActionResult> Debug([FromQuery] int fdId)
	{
		var log = new List<string>();
		var fdKey = _config["FootballData:ApiKey"] ?? "";
		var afKey = _config["ApiFootball:ApiKey"] ?? "";

		log.Add($"FdKey configurado: {(string.IsNullOrEmpty(fdKey) ? "NÃO" : "SIM")}");
		log.Add($"ApiFootball Key configurada: {(string.IsNullOrEmpty(afKey) ? "NÃO" : "SIM")}");

		// ── 1. Busca na football-data.org ──
		var httpFd = _factory.CreateClient();
		httpFd.DefaultRequestHeaders.Add("X-Auth-Token", fdKey);

		string? utcDate = null;
		string? homeNameFd = null;
		string? awayNameFd = null;
		string? competitionCode = null;

		try
		{
			var fdRes = await httpFd.GetAsync($"https://api.football-data.org/v4/matches/{fdId}");
			log.Add($"[FD] Status: {(int)fdRes.StatusCode} {fdRes.StatusCode}");

			var fdBody = await fdRes.Content.ReadAsStringAsync();

			if (fdRes.IsSuccessStatusCode)
			{
				var fd = JsonSerializer.Deserialize<JsonElement>(fdBody);
				utcDate = fd.TryGetProperty("utcDate", out var ud) ? ud.GetString() : null;
				homeNameFd = fd.TryGetProperty("homeTeam", out var ht)
					&& ht.TryGetProperty("name", out var htn) ? htn.GetString() : null;
				awayNameFd = fd.TryGetProperty("awayTeam", out var at)
					&& at.TryGetProperty("name", out var atn) ? atn.GetString() : null;

				// Pega código da competição para saber qual liga é
				competitionCode = fd.TryGetProperty("competition", out var comp)
					&& comp.TryGetProperty("code", out var cc) ? cc.GetString() : null;

				log.Add($"[FD] Data: {utcDate}");
				log.Add($"[FD] Home: {homeNameFd}");
				log.Add($"[FD] Away: {awayNameFd}");
				log.Add($"[FD] Competição code: {competitionCode}");
			}
			else
			{
				log.Add($"[FD] Erro body: {fdBody[..Math.Min(300, fdBody.Length)]}");
				return Ok(new { log });
			}
		}
		catch (Exception ex)
		{
			log.Add($"[FD] Exception: {ex.Message}");
			return Ok(new { log });
		}

		if (utcDate == null || !DateTime.TryParse(utcDate, out var matchDate))
		{
			log.Add("[FD] Não conseguiu parsear a data.");
			return Ok(new { log });
		}

		var dateStr = matchDate.ToString("yyyy-MM-dd");
		log.Add($"[AF] Buscando fixtures para data: {dateStr}");

		// ── 2. Tenta múltiplas ligas e temporadas na API-Football ──
		var httpAf = _factory.CreateClient();
		httpAf.BaseAddress = new Uri("https://v3.football.api-sports.io/");
		httpAf.DefaultRequestHeaders.Add("x-apisports-key", afKey);

		// Mapa de competições football-data → league IDs da API-Football
		// Adicione mais se necessário
		var leaguesToTry = new List<(int leagueId, int season)>
		{
			(71, 2025),   // Brasileirão Série A 2025
            (71, 2024),   // Brasileirão Série A 2024
            (2, 2024),    // Champions League 2024/25
            (3, 2024),    // Europa League 2024/25
            (39, 2024),   // Premier League 2024/25
            (140, 2024),  // La Liga 2024/25
            (135, 2024),  // Serie A Italiana 2024/25
            (61, 2024),   // Ligue 1 2024/25
            (78, 2024),   // Bundesliga 2024/25
            (13, 2024),   // Libertadores 2025
            (11, 2024),   // Sul-Americana 2025
        };

		foreach (var (leagueId, season) in leaguesToTry)
		{
			try
			{
				var url = $"fixtures?date={dateStr}&league={leagueId}&season={season}";
				var afRes = await httpAf.GetAsync(url);
				var afBody = await afRes.Content.ReadAsStringAsync();
				var afDoc = JsonSerializer.Deserialize<JsonElement>(afBody);

				if (!afDoc.TryGetProperty("response", out var arr)) continue;
				var fixtures = arr.EnumerateArray().ToList();
				log.Add($"[AF] Liga {leagueId} temporada {season}: {fixtures.Count} jogos encontrados");

				foreach (var f in fixtures)
				{
					var teams = f.GetProperty("teams");
					var hName = teams.GetProperty("home").GetProperty("name").GetString() ?? "";
					var aName = teams.GetProperty("away").GetProperty("name").GetString() ?? "";
					log.Add($"    → {hName} x {aName}");

					// Verifica se bate com o jogo buscado
					if (NamesMatch(hName, homeNameFd ?? "") && NamesMatch(aName, awayNameFd ?? ""))
					{
						var fixtureId = f.GetProperty("fixture").GetProperty("id").GetInt32();
						log.Add($"[AF] ✅ MATCH ENCONTRADO! FixtureId={fixtureId}, Liga={leagueId}, Season={season}");
						return Ok(new { log, fixtureId, leagueId, season });
					}
				}
			}
			catch (Exception ex)
			{
				log.Add($"[AF] Erro liga {leagueId}/{season}: {ex.Message}");
			}
		}

		log.Add("[AF] ❌ Nenhuma correspondência encontrada em nenhuma liga.");
		log.Add($"[AF] Times buscados: '{homeNameFd}' x '{awayNameFd}'");
		return Ok(new { log });
	}

	private static bool NamesMatch(string a, string b)
	{
		if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
		a = Normalize(a); b = Normalize(b);
		return a.Contains(b) || b.Contains(a);
	}

	private static string Normalize(string s) =>
		s.ToLowerInvariant()
		 .Replace("athletico", "atletico")
		 .Replace("atlético", "atletico")
		 .Replace("-", " ")
		 .Trim();
}
