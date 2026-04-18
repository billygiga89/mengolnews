using System.Text.Json;

namespace MengolNews.Api.Services;

public class MatchService
{
	private readonly HttpClient _http;        // API-Football
	private readonly HttpClient _httpFd;      // football-data.org (para lookup)
	private readonly string _apiKey;
	private readonly string _fdKey;

	public MatchService(IHttpClientFactory factory, IConfiguration config)
	{
		_apiKey = config["ApiFootball:ApiKey"]
			   ?? throw new Exception("ApiFootball:ApiKey not configured");
		_fdKey = config["FootballData:ApiKey"]
			   ?? throw new Exception("FootballData:ApiKey not configured");

		_http = factory.CreateClient("apifootball");
		_http.BaseAddress = new Uri("https://v3.football.api-sports.io/");
		_http.DefaultRequestHeaders.Add("x-apisports-key", _apiKey);

		_httpFd = factory.CreateClient("footballdata");
		_httpFd.DefaultRequestHeaders.Add("X-Auth-Token", _fdKey);
	}

	// ─────────────────────────────────────────────────────────────
	// LOOKUP: recebe ID da football-data.org → devolve MatchDto
	// Fluxo: busca data+times na football-data → acha fixture na
	//        API-Football por data+time → busca dados completos
	// ─────────────────────────────────────────────────────────────
	public async Task<MatchDto?> GetMatchByFdIdAsync(int fdMatchId)
	{
		// 1. Busca o jogo na football-data.org para pegar data e nomes
		JsonElement? fdDoc = null;
		try
		{
			var fdRes = await _httpFd.GetAsync(
				$"https://api.football-data.org/v4/matches/{fdMatchId}");
			if (!fdRes.IsSuccessStatusCode) return null;
			fdDoc = JsonSerializer.Deserialize<JsonElement>(
				await fdRes.Content.ReadAsStringAsync());
		}
		catch { return null; }

		if (fdDoc == null) return null;
		var fd = fdDoc.Value;

		// Extrai data UTC e nomes dos times
		var utcDate = fd.TryGetProperty("utcDate", out var ud) ? ud.GetString() ?? "" : "";
		if (!DateTime.TryParse(utcDate, out var matchDate)) return null;

		var homeNameFd = fd.TryGetProperty("homeTeam", out var ht)
			&& ht.TryGetProperty("name", out var htn) ? htn.GetString() ?? "" : "";
		var awayNameFd = fd.TryGetProperty("awayTeam", out var at)
			&& at.TryGetProperty("name", out var atn) ? atn.GetString() ?? "" : "";

		// 2. Busca fixtures na API-Football pela data
		var dateStr = matchDate.ToString("yyyy-MM-dd");
		var fixtures = await FetchAsync($"fixtures?date={dateStr}&league=71&season=2025");

		// Fallback: tenta temporada 2026 também
		if (fixtures == null || GetResponse(fixtures.Value).Count == 0)
			fixtures = await FetchAsync($"fixtures?date={dateStr}&league=71&season=2026");

		if (fixtures == null) return null;

		var fixturesList = GetResponse(fixtures.Value);
		if (fixturesList.Count == 0) return null;

		// 3. Encontra o fixture correto comparando nomes dos times
		int fixtureId = -1;
		foreach (var f in fixturesList)
		{
			var teams = f.GetProperty("teams");
			var homeName = teams.GetProperty("home").GetProperty("name").GetString() ?? "";
			var awayName = teams.GetProperty("away").GetProperty("name").GetString() ?? "";

			// Compara de forma flexível (contém parte do nome)
			if (NamesMatch(homeName, homeNameFd) && NamesMatch(awayName, awayNameFd))
			{
				fixtureId = f.GetProperty("fixture").GetProperty("id").GetInt32();
				break;
			}
		}

		// Fallback: tenta match só pelo away (times com nomes muito diferentes entre APIs)
		if (fixtureId == -1)
		{
			foreach (var f in fixturesList)
			{
				var teams = f.GetProperty("teams");
				var homeName = teams.GetProperty("home").GetProperty("name").GetString() ?? "";
				var awayName = teams.GetProperty("away").GetProperty("name").GetString() ?? "";

				if (NamesMatch(homeName, homeNameFd) || NamesMatch(awayName, awayNameFd))
				{
					fixtureId = f.GetProperty("fixture").GetProperty("id").GetInt32();
					break;
				}
			}
		}

		if (fixtureId == -1) return null;

		// 4. Busca dados completos pelo fixture ID da API-Football
		return await GetMatchAsync(fixtureId);
	}

	// ─────────────────────────────────────────────────────────────
	// GET /api/match/{fixtureId}  (ID da API-Football)
	// ─────────────────────────────────────────────────────────────
	public async Task<MatchDto?> GetMatchAsync(int fixtureId)
	{
		var fixtureTask = FetchAsync($"fixtures?id={fixtureId}");
		var eventsTask = FetchAsync($"fixtures/events?fixture={fixtureId}");
		var lineupsTask = FetchAsync($"fixtures/lineups?fixture={fixtureId}");
		var statsTask = FetchAsync($"fixtures/statistics?fixture={fixtureId}");

		await Task.WhenAll(fixtureTask, eventsTask, lineupsTask, statsTask);

		var fixtureDoc = await fixtureTask;
		var eventsDoc = await eventsTask;
		var lineupsDoc = await lineupsTask;
		var statsDoc = await statsTask;

		if (fixtureDoc == null) return null;

		var fixtureArr = GetResponse(fixtureDoc.Value);
		if (fixtureArr.Count == 0) return null;
		var fixture = fixtureArr[0];

		var fix = fixture.GetProperty("fixture");
		var league = fixture.GetProperty("league");
		var teams = fixture.GetProperty("teams");
		var goals = fixture.GetProperty("goals");
		var score = fixture.GetProperty("score");

		var dto = new MatchDto();

		dto.HomeTeamId = teams.GetProperty("home").GetProperty("id").GetInt32();
		dto.AwayTeamId = teams.GetProperty("away").GetProperty("id").GetInt32();

		dto.HomeTeam = new TeamDto
		{
			Id = dto.HomeTeamId,
			Name = teams.GetProperty("home").GetProperty("name").GetString() ?? "",
			Short = teams.GetProperty("home").TryGetProperty("code", out var hc) ? hc.GetString() ?? "" : "",
			Logo = teams.GetProperty("home").GetProperty("logo").GetString() ?? "",
		};
		dto.AwayTeam = new TeamDto
		{
			Id = dto.AwayTeamId,
			Name = teams.GetProperty("away").GetProperty("name").GetString() ?? "",
			Short = teams.GetProperty("away").TryGetProperty("code", out var ac) ? ac.GetString() ?? "" : "",
			Logo = teams.GetProperty("away").GetProperty("logo").GetString() ?? "",
		};

		dto.Competition = league.TryGetProperty("name", out var ln) ? ln.GetString() ?? "" : "";
		dto.Round = league.TryGetProperty("round", out var rn) ? rn.GetString() ?? "" : "";

		var statusEl = fix.GetProperty("status");
		dto.Status = statusEl.TryGetProperty("short", out var ss) ? ss.GetString() ?? "" : "";
		dto.StatusLong = statusEl.TryGetProperty("long", out var sl) ? sl.GetString() ?? "" : "";
		dto.Minute = statusEl.TryGetProperty("elapsed", out var el)
						  && el.ValueKind == JsonValueKind.Number ? el.GetInt32() : 0;

		dto.HomeScore = goals.TryGetProperty("home", out var gh)
						  && gh.ValueKind == JsonValueKind.Number ? gh.GetInt32() : 0;
		dto.AwayScore = goals.TryGetProperty("away", out var ga)
						  && ga.ValueKind == JsonValueKind.Number ? ga.GetInt32() : 0;

		dto.DateUtc = fix.TryGetProperty("date", out var dt) ? dt.GetString() ?? "" : "";

		// ── Eventos ──
		dto.Events = new List<EventDto>();
		if (eventsDoc != null)
		{
			foreach (var ev in GetResponse(eventsDoc.Value))
			{
				var timeEl = ev.GetProperty("time");
				var teamEl = ev.GetProperty("team");
				var playerEl = ev.GetProperty("player");
				var evType = ev.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
				var evDetail = ev.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "";
				var teamId = teamEl.TryGetProperty("id", out var tid) ? tid.GetInt32() : -1;
				var assistEl = ev.TryGetProperty("assist", out var ass) ? ass : default;

				dto.Events.Add(new EventDto
				{
					Minute = timeEl.TryGetProperty("elapsed", out var me)
								  && me.ValueKind == JsonValueKind.Number ? me.GetInt32() : 0,
					MinuteExtra = timeEl.TryGetProperty("extra", out var mx)
								  && mx.ValueKind == JsonValueKind.Number ? mx.GetInt32() : 0,
					TeamId = teamId,
					IsHome = teamId == dto.HomeTeamId,
					TeamName = teamEl.TryGetProperty("name", out var tn) ? tn.GetString() ?? "" : "",
					Player = playerEl.TryGetProperty("name", out var pn) ? pn.GetString() ?? "" : "",
					PlayerId = playerEl.TryGetProperty("id", out var pid)
								  && pid.ValueKind == JsonValueKind.Number ? pid.GetInt32() : 0,
					Assist = assistEl.ValueKind != JsonValueKind.Undefined
								  && assistEl.TryGetProperty("name", out var an)
								  && an.ValueKind != JsonValueKind.Null
								  ? an.GetString() ?? "" : "",
					Type = evType,
					Detail = evDetail,
				});
			}
		}

		// ── Escalação ──
		dto.HomeLineup = new LineupDto();
		dto.AwayLineup = new LineupDto();

		if (lineupsDoc != null)
		{
			foreach (var lu in GetResponse(lineupsDoc.Value))
			{
				var luTeamId = lu.TryGetProperty("team", out var lut)
					&& lut.TryGetProperty("id", out var lutid) ? lutid.GetInt32() : -1;
				var isHome = luTeamId == dto.HomeTeamId;
				var target = isHome ? dto.HomeLineup : dto.AwayLineup;

				target.Formation = lu.TryGetProperty("formation", out var form) ? form.GetString() ?? "" : "";
				target.Coach = lu.TryGetProperty("coach", out var coach)
					&& coach.TryGetProperty("name", out var cn) ? cn.GetString() ?? "" : "";

				target.StartXI = new List<LineupPlayerDto>();
				if (lu.TryGetProperty("startXI", out var startXI))
					foreach (var entry in startXI.EnumerateArray())
						if (entry.TryGetProperty("player", out var p))
							target.StartXI.Add(ParseLineupPlayer(p));

				target.Substitutes = new List<LineupPlayerDto>();
				if (lu.TryGetProperty("substitutes", out var subs))
					foreach (var entry in subs.EnumerateArray())
						if (entry.TryGetProperty("player", out var p))
							target.Substitutes.Add(ParseLineupPlayer(p));
			}
		}

		// ── Estatísticas ──
		dto.Stats = new List<StatDto>();
		if (statsDoc != null)
		{
			var homeStats = new Dictionary<string, string>();
			var awayStats = new Dictionary<string, string>();

			foreach (var teamStats in GetResponse(statsDoc.Value))
			{
				var tsId = teamStats.TryGetProperty("team", out var tst)
					&& tst.TryGetProperty("id", out var tsid) ? tsid.GetInt32() : -1;
				var dict = tsId == dto.HomeTeamId ? homeStats : awayStats;

				if (!teamStats.TryGetProperty("statistics", out var statList)) continue;
				foreach (var stat in statList.EnumerateArray())
				{
					var stype = stat.TryGetProperty("type", out var st) ? st.GetString() ?? "" : "";
					var sval = stat.TryGetProperty("value", out var sv)
						? sv.ValueKind == JsonValueKind.Null ? "0" : sv.ToString() : "0";
					dict[stype] = sval;
				}
			}

			var statNames = new[]
			{
				"Ball Possession", "Total Shots", "Shots on Goal", "Shots off Goal",
				"Corner Kicks", "Fouls", "Yellow Cards", "Red Cards",
				"Offsides", "Passes accurate", "Total passes", "Goalkeeper Saves",
			};

			foreach (var name in statNames)
			{
				homeStats.TryGetValue(name, out var hv);
				awayStats.TryGetValue(name, out var av);
				var hNum = ParseStatNum(hv);
				var aNum = ParseStatNum(av);
				if (hNum > 0 || aNum > 0)
					dto.Stats.Add(new StatDto
					{
						Name = name,
						HomeValue = hNum,
						AwayValue = aNum,
						Unit = name == "Ball Possession" ? "%" : "",
					});
			}
		}

		return dto;
	}

	// ─────────────────────────────────────────────────────────────
	// Live e Today
	// ─────────────────────────────────────────────────────────────
	public async Task<List<MatchSummaryDto>?> GetLiveMatchesAsync()
	{
		var doc = await FetchAsync("fixtures?live=all");
		return doc == null ? null : ParseSummaries(doc.Value);
	}

	public async Task<List<MatchSummaryDto>?> GetTodayMatchesAsync()
	{
		var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
		var doc = await FetchAsync($"fixtures?date={today}");
		return doc == null ? null : ParseSummaries(doc.Value);
	}

	// ─────────────────────────────────────────────────────────────
	// Helpers
	// ─────────────────────────────────────────────────────────────
	private async Task<JsonElement?> FetchAsync(string url)
	{
		try
		{
			var res = await _http.GetAsync(url);
			if (!res.IsSuccessStatusCode) return null;
			return JsonSerializer.Deserialize<JsonElement>(
				await res.Content.ReadAsStringAsync());
		}
		catch { return null; }
	}

	private static List<JsonElement> GetResponse(JsonElement doc)
	{
		if (!doc.TryGetProperty("response", out var arr)) return new();
		return arr.EnumerateArray().ToList();
	}

	private static LineupPlayerDto ParseLineupPlayer(JsonElement p) => new()
	{
		Id = p.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt32() : 0,
		Name = p.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
		Number = p.TryGetProperty("number", out var num) && num.ValueKind == JsonValueKind.Number ? num.GetInt32() : 0,
		Position = p.TryGetProperty("pos", out var pos) ? pos.GetString() ?? "" : "",
		Grid = p.TryGetProperty("grid", out var g) && g.ValueKind != JsonValueKind.Null
				   ? g.GetString() ?? "" : "",
	};

	private static int ParseStatNum(string? val)
	{
		if (string.IsNullOrEmpty(val)) return 0;
		return int.TryParse(val.Replace("%", "").Trim(), out var n) ? n : 0;
	}

	private static List<MatchSummaryDto> ParseSummaries(JsonElement doc)
	{
		var result = new List<MatchSummaryDto>();
		foreach (var item in GetResponse(doc))
		{
			var fix = item.GetProperty("fixture");
			var league = item.GetProperty("league");
			var teams = item.GetProperty("teams");
			var goals = item.GetProperty("goals");
			var status = fix.GetProperty("status");

			result.Add(new MatchSummaryDto
			{
				Id = fix.GetProperty("id").GetInt32(),
				Status = status.TryGetProperty("short", out var ss) ? ss.GetString() ?? "" : "",
				Minute = status.TryGetProperty("elapsed", out var el)
							 && el.ValueKind == JsonValueKind.Number ? el.GetInt32() : 0,
				HomeTeam = teams.GetProperty("home").GetProperty("name").GetString() ?? "",
				HomeLogo = teams.GetProperty("home").GetProperty("logo").GetString() ?? "",
				AwayTeam = teams.GetProperty("away").GetProperty("name").GetString() ?? "",
				AwayLogo = teams.GetProperty("away").GetProperty("logo").GetString() ?? "",
				HomeScore = goals.TryGetProperty("home", out var gh) && gh.ValueKind == JsonValueKind.Number ? gh.GetInt32() : 0,
				AwayScore = goals.TryGetProperty("away", out var ga) && ga.ValueKind == JsonValueKind.Number ? ga.GetInt32() : 0,
				League = league.TryGetProperty("name", out var ln) ? ln.GetString() ?? "" : "",
				LeagueLogo = league.TryGetProperty("logo", out var ll) ? ll.GetString() ?? "" : "",
				DateUtc = fix.TryGetProperty("date", out var dt) ? dt.GetString() ?? "" : "",
			});
		}
		return result;
	}

	// Compara nomes de times entre APIs diferentes de forma flexível
	private static bool NamesMatch(string a, string b)
	{
		if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
		a = Normalize(a);
		b = Normalize(b);
		return a.Contains(b) || b.Contains(a);
	}

	private static string Normalize(string s) =>
		s.ToLowerInvariant()
		 .Replace("athletico", "atletico")
		 .Replace("atlético", "atletico")
		 .Replace("fluminense", "fluminense")
		 .Replace("-", " ")
		 .Trim();
}

// ═══════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════

public class MatchDto
{
	public int HomeTeamId { get; set; }
	public int AwayTeamId { get; set; }
	public TeamDto HomeTeam { get; set; } = new();
	public TeamDto AwayTeam { get; set; } = new();
	public string Competition { get; set; } = "";
	public string Round { get; set; } = "";
	public string Status { get; set; } = "";
	public string StatusLong { get; set; } = "";
	public int Minute { get; set; }
	public int HomeScore { get; set; }
	public int AwayScore { get; set; }
	public int HalfTimeHome { get; set; }
	public int HalfTimeAway { get; set; }
	public string DateUtc { get; set; } = "";
	public List<EventDto> Events { get; set; } = new();
	public LineupDto HomeLineup { get; set; } = new();
	public LineupDto AwayLineup { get; set; } = new();
	public List<StatDto> Stats { get; set; } = new();
}

public class TeamDto
{
	public int Id { get; set; }
	public string Name { get; set; } = "";
	public string Short { get; set; } = "";
	public string Logo { get; set; } = "";
}

public class EventDto
{
	public int Minute { get; set; }
	public int MinuteExtra { get; set; }
	public int TeamId { get; set; }
	public bool IsHome { get; set; }
	public string TeamName { get; set; } = "";
	public string Player { get; set; } = "";
	public int PlayerId { get; set; }
	public string Assist { get; set; } = "";
	public string Type { get; set; } = "";
	public string Detail { get; set; } = "";
	public string Comments { get; set; } = "";
}

public class LineupDto
{
	public string Formation { get; set; } = "";
	public string Coach { get; set; } = "";
	public List<LineupPlayerDto> StartXI { get; set; } = new();
	public List<LineupPlayerDto> Substitutes { get; set; } = new();
}

public class LineupPlayerDto
{
	public int Id { get; set; }
	public string Name { get; set; } = "";
	public int Number { get; set; }
	public string Position { get; set; } = "";
	public string Grid { get; set; } = "";
}

public class StatDto
{
	public string Name { get; set; } = "";
	public int HomeValue { get; set; }
	public int AwayValue { get; set; }
	public string Unit { get; set; } = "";
}

public class MatchSummaryDto
{
	public int Id { get; set; }
	public string Status { get; set; } = "";
	public int Minute { get; set; }
	public string HomeTeam { get; set; } = "";
	public string HomeLogo { get; set; } = "";
	public string AwayTeam { get; set; } = "";
	public string AwayLogo { get; set; } = "";
	public int HomeScore { get; set; }
	public int AwayScore { get; set; }
	public string League { get; set; } = "";
	public string LeagueLogo { get; set; } = "";
	public string DateUtc { get; set; } = "";
}
