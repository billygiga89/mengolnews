using System.Text.Json;

namespace MengolNews.Api.Services;

public class MatchService
{
	private readonly HttpClient _httpFd;   // football-data.org  (dados básicos)
	private readonly HttpClient _httpAf;   // api-football        (extras)
	private readonly string _fdKey;
	private readonly string _afKey;

	public MatchService(IHttpClientFactory factory, IConfiguration config)
	{
		_fdKey = config["FootballData:ApiKey"]
			  ?? throw new Exception("FootballData:ApiKey not configured");
		_afKey = config["ApiFootball:ApiKey"] ?? ""; // opcional no plano free

		_httpFd = factory.CreateClient("footballdata");
		_httpFd.DefaultRequestHeaders.Add("X-Auth-Token", _fdKey);

		_httpAf = factory.CreateClient("apifootball");
		_httpAf.BaseAddress = new Uri("https://v3.football.api-sports.io/");
		if (!string.IsNullOrEmpty(_afKey))
			_httpAf.DefaultRequestHeaders.Add("x-apisports-key", _afKey);
	}

	// ─────────────────────────────────────────────────────────
	// PRINCIPAL: busca jogo pelo ID da football-data.org
	// ─────────────────────────────────────────────────────────
	public async Task<MatchDto?> GetMatchByFdIdAsync(int fdMatchId)
	{
		// 1. Dados básicos da football-data.org
		JsonElement? fdDoc = null;
		try
		{
			var res = await _httpFd.GetAsync(
				$"https://api.football-data.org/v4/matches/{fdMatchId}");
			if (!res.IsSuccessStatusCode) return null;
			fdDoc = JsonSerializer.Deserialize<JsonElement>(
				await res.Content.ReadAsStringAsync());
		}
		catch { return null; }

		if (fdDoc == null) return null;
		var fd = fdDoc.Value;

		// Monta o DTO já com o que a football-data tem
		var dto = new MatchDto();

		// Times
		var htEl = fd.GetProperty("homeTeam");
		var atEl = fd.GetProperty("awayTeam");

		dto.HomeTeam = new TeamDto
		{
			Id = htEl.TryGetProperty("id", out var hid) ? hid.GetInt32() : 0,
			Name = htEl.TryGetProperty("name", out var hn) ? hn.GetString() ?? "" : "",
			Short = htEl.TryGetProperty("shortName", out var hsn) ? hsn.GetString() ?? "" : "",
			Logo = htEl.TryGetProperty("crest", out var hcr) ? hcr.GetString() ?? "" : "",
		};
		dto.AwayTeam = new TeamDto
		{
			Id = atEl.TryGetProperty("id", out var aid) ? aid.GetInt32() : 0,
			Name = atEl.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "",
			Short = atEl.TryGetProperty("shortName", out var asn) ? asn.GetString() ?? "" : "",
			Logo = atEl.TryGetProperty("crest", out var acr) ? acr.GetString() ?? "" : "",
		};
		dto.HomeTeamId = dto.HomeTeam.Id;
		dto.AwayTeamId = dto.AwayTeam.Id;

		// Competição
		if (fd.TryGetProperty("competition", out var comp))
		{
			dto.Competition = comp.TryGetProperty("name", out var cn) ? cn.GetString() ?? "" : "";
		}
		if (fd.TryGetProperty("season", out var season) &&
			season.TryGetProperty("currentMatchday", out var cmd))
		{
			dto.Round = $"Rodada {cmd.GetInt32()}";
		}

		// Status e placar
		var statusFd = fd.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
		dto.Status = MapStatus(statusFd);
		dto.StatusLong = statusFd;
		dto.DateUtc = fd.TryGetProperty("utcDate", out var dt) ? dt.GetString() ?? "" : "";

		if (fd.TryGetProperty("score", out var score))
		{
			var ft = score.TryGetProperty("fullTime", out var ftEl) ? ftEl : default;
			var ht = score.TryGetProperty("halfTime", out var htEl2) ? htEl2 : default;

			// Usa fullTime se disponível, senão halfTime
			dto.HomeScore = GetScoreVal(ft, "home") ?? GetScoreVal(ht, "home") ?? 0;
			dto.AwayScore = GetScoreVal(ft, "away") ?? GetScoreVal(ht, "away") ?? 0;
		}

		// Minuto (football-data não fornece, só API-Football ao vivo)
		dto.Minute = 0;

		// 2. Tenta complementar com API-Football (lineup, stats, eventos)
		//    Só faz se tiver chave configurada
		if (!string.IsNullOrEmpty(_afKey))
		{
			try
			{
				await EnrichWithApiFootball(dto);
			}
			catch { /* falha silenciosa — dados básicos já estão ok */ }
		}

		return dto;
	}

	// ─────────────────────────────────────────────────────────
	// Enriquece o DTO com dados da API-Football
	// ─────────────────────────────────────────────────────────
	private async Task EnrichWithApiFootball(MatchDto dto)
	{
		if (string.IsNullOrEmpty(dto.DateUtc)) return;
		if (!DateTime.TryParse(dto.DateUtc, out var matchDate)) return;

		var dateStr = matchDate.ToString("yyyy-MM-dd");
		var year = matchDate.Year;

		// Tenta achar o fixture na API-Football
		var leaguesToTry = new List<(int leagueId, int season)>
		{
			(71, year),      // Brasileirão ano exato
            (71, year - 1),  // Brasileirão ano anterior (temporada anterior)
            (13, year),      // Libertadores
            (11, year),      // Sul-Americana
            (2,  year - 1),  // Champions League
            (39, year - 1),  // Premier League
        };

		int fixtureId = -1;
		foreach (var (leagueId, season) in leaguesToTry)
		{
			var doc = await FetchAfAsync($"fixtures?date={dateStr}&league={leagueId}&season={season}");
			if (doc == null) continue;

			foreach (var f in GetResponse(doc.Value))
			{
				var teams = f.GetProperty("teams");
				var homeName = teams.GetProperty("home").GetProperty("name").GetString() ?? "";
				var awayName = teams.GetProperty("away").GetProperty("name").GetString() ?? "";

				if (NamesMatch(homeName, dto.HomeTeam.Name) &&
					NamesMatch(awayName, dto.AwayTeam.Name))
				{
					fixtureId = f.GetProperty("fixture").GetProperty("id").GetInt32();

					// Aproveita o minuto ao vivo se disponível
					var fixEl = f.GetProperty("fixture");
					var status = fixEl.GetProperty("status");
					if (status.TryGetProperty("elapsed", out var el) &&
						el.ValueKind == JsonValueKind.Number)
						dto.Minute = el.GetInt32();

					break;
				}
			}
			if (fixtureId != -1) break;
		}

		if (fixtureId == -1) return; // não achou na API-Football, retorna com dados básicos

		// Busca extras em paralelo
		var evTask = FetchAfAsync($"fixtures/events?fixture={fixtureId}");
		var luTask = FetchAfAsync($"fixtures/lineups?fixture={fixtureId}");
		var statsTask = FetchAfAsync($"fixtures/statistics?fixture={fixtureId}");
		await Task.WhenAll(evTask, luTask, statsTask);

		// Eventos
		dto.Events = new();
		if (await evTask is { } evDoc)
			foreach (var ev in GetResponse(evDoc))
				dto.Events.Add(ParseEvent(ev, dto.HomeTeamId));

		// Escalações
		dto.HomeLineup = new();
		dto.AwayLineup = new();
		if (await luTask is { } luDoc)
			foreach (var lu in GetResponse(luDoc))
			{
				var luTeamId = lu.TryGetProperty("team", out var lut) &&
							   lut.TryGetProperty("id", out var lid) ? lid.GetInt32() : -1;
				var target = luTeamId == dto.HomeTeamId ? dto.HomeLineup : dto.AwayLineup;
				ParseLineup(lu, target);
			}

		// Estatísticas
		dto.Stats = new();
		if (await statsTask is { } stDoc)
			dto.Stats = ParseStats(stDoc, dto.HomeTeamId);
	}

	// ─────────────────────────────────────────────────────────
	// Jogos ao vivo (football-data.org)
	// ─────────────────────────────────────────────────────────
	public async Task<List<MatchSummaryDto>?> GetLiveMatchesAsync()
	{
		try
		{
			var res = await _httpFd.GetAsync(
				"https://api.football-data.org/v4/matches?status=IN_PLAY");
			if (!res.IsSuccessStatusCode) return null;

			var doc = JsonSerializer.Deserialize<JsonElement>(
				await res.Content.ReadAsStringAsync());

			if (!doc.TryGetProperty("matches", out var arr)) return new();

			return arr.EnumerateArray().Select(m =>
			{
				var fix = m.GetProperty("homeTeam");
				var score = m.GetProperty("score");
				var ft = score.TryGetProperty("fullTime", out var f) ? f : default;
				return new MatchSummaryDto
				{
					Id = m.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
					Status = MapStatus(m.TryGetProperty("status", out var st) ? st.GetString() ?? "" : ""),
					HomeTeam = m.GetProperty("homeTeam").TryGetProperty("shortName", out var hn) ? hn.GetString() ?? "" : "",
					HomeLogo = m.GetProperty("homeTeam").TryGetProperty("crest", out var hc) ? hc.GetString() ?? "" : "",
					AwayTeam = m.GetProperty("awayTeam").TryGetProperty("shortName", out var an) ? an.GetString() ?? "" : "",
					AwayLogo = m.GetProperty("awayTeam").TryGetProperty("crest", out var ac) ? ac.GetString() ?? "" : "",
					HomeScore = GetScoreVal(ft, "home") ?? 0,
					AwayScore = GetScoreVal(ft, "away") ?? 0,
					League = m.TryGetProperty("competition", out var comp) &&
							   comp.TryGetProperty("name", out var cn) ? cn.GetString() ?? "" : "",
					DateUtc = m.TryGetProperty("utcDate", out var dt) ? dt.GetString() ?? "" : "",
				};
			}).ToList();
		}
		catch { return null; }
	}

	// ─────────────────────────────────────────────────────────
	// Helpers
	// ─────────────────────────────────────────────────────────
	private async Task<JsonElement?> FetchAfAsync(string url)
	{
		try
		{
			var res = await _httpAf.GetAsync(url);
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

	private static int? GetScoreVal(JsonElement el, string side)
	{
		if (el.ValueKind == JsonValueKind.Undefined) return null;
		if (!el.TryGetProperty(side, out var v)) return null;
		return v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
	}

	private static string MapStatus(string fd) => fd switch
	{
		"IN_PLAY" => "1H",
		"PAUSED" => "HT",
		"FINISHED" => "FT",
		"TIMED" => "NS",
		"SCHEDULED" => "NS",
		_ => fd,
	};

	private static EventDto ParseEvent(JsonElement ev, int homeTeamId)
	{
		var timeEl = ev.GetProperty("time");
		var teamEl = ev.GetProperty("team");
		var playerEl = ev.GetProperty("player");
		var teamId = teamEl.TryGetProperty("id", out var tid) ? tid.GetInt32() : -1;
		var assistEl = ev.TryGetProperty("assist", out var ass) ? ass : default;

		return new EventDto
		{
			Minute = timeEl.TryGetProperty("elapsed", out var me) && me.ValueKind == JsonValueKind.Number ? me.GetInt32() : 0,
			MinuteExtra = timeEl.TryGetProperty("extra", out var mx) && mx.ValueKind == JsonValueKind.Number ? mx.GetInt32() : 0,
			TeamId = teamId,
			IsHome = teamId == homeTeamId,
			TeamName = teamEl.TryGetProperty("name", out var tn) ? tn.GetString() ?? "" : "",
			Player = playerEl.TryGetProperty("name", out var pn) ? pn.GetString() ?? "" : "",
			PlayerId = playerEl.TryGetProperty("id", out var pid) && pid.ValueKind == JsonValueKind.Number ? pid.GetInt32() : 0,
			Assist = assistEl.ValueKind != JsonValueKind.Undefined &&
						  assistEl.TryGetProperty("name", out var an) &&
						  an.ValueKind != JsonValueKind.Null ? an.GetString() ?? "" : "",
			Type = ev.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
			Detail = ev.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "",
		};
	}

	private static void ParseLineup(JsonElement lu, LineupDto target)
	{
		target.Formation = lu.TryGetProperty("formation", out var form) ? form.GetString() ?? "" : "";
		target.Coach = lu.TryGetProperty("coach", out var coach) &&
						   coach.TryGetProperty("name", out var cn) ? cn.GetString() ?? "" : "";

		target.StartXI = new();
		if (lu.TryGetProperty("startXI", out var xi))
			foreach (var entry in xi.EnumerateArray())
				if (entry.TryGetProperty("player", out var p))
					target.StartXI.Add(ParseLineupPlayer(p));

		target.Substitutes = new();
		if (lu.TryGetProperty("substitutes", out var subs))
			foreach (var entry in subs.EnumerateArray())
				if (entry.TryGetProperty("player", out var p))
					target.Substitutes.Add(ParseLineupPlayer(p));
	}

	private static LineupPlayerDto ParseLineupPlayer(JsonElement p) => new()
	{
		Id = p.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt32() : 0,
		Name = p.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
		Number = p.TryGetProperty("number", out var num) && num.ValueKind == JsonValueKind.Number ? num.GetInt32() : 0,
		Position = p.TryGetProperty("pos", out var pos) ? pos.GetString() ?? "" : "",
		Grid = p.TryGetProperty("grid", out var g) && g.ValueKind != JsonValueKind.Null ? g.GetString() ?? "" : "",
	};

	private static List<StatDto> ParseStats(JsonElement doc, int homeTeamId)
	{
		var homeStats = new Dictionary<string, string>();
		var awayStats = new Dictionary<string, string>();

		foreach (var teamStats in GetResponse(doc))
		{
			var tsId = teamStats.TryGetProperty("team", out var tst) &&
					   tst.TryGetProperty("id", out var tsid) ? tsid.GetInt32() : -1;
			var dict = tsId == homeTeamId ? homeStats : awayStats;

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
			"Ball Possession","Total Shots","Shots on Goal","Shots off Goal",
			"Corner Kicks","Fouls","Yellow Cards","Red Cards",
			"Offsides","Passes accurate","Total passes","Goalkeeper Saves",
		};

		var result = new List<StatDto>();
		foreach (var name in statNames)
		{
			homeStats.TryGetValue(name, out var hv);
			awayStats.TryGetValue(name, out var av);
			var hNum = ParseStatNum(hv);
			var aNum = ParseStatNum(av);
			if (hNum > 0 || aNum > 0)
				result.Add(new StatDto
				{
					Name = name,
					HomeValue = hNum,
					AwayValue = aNum,
					Unit = name == "Ball Possession" ? "%" : "",
				});
		}
		return result;
	}

	private static int ParseStatNum(string? val)
	{
		if (string.IsNullOrEmpty(val)) return 0;
		return int.TryParse(val.Replace("%", "").Trim(), out var n) ? n : 0;
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

// ══════════════════════════════════════════════════════════════
// DTOs (mantém os mesmos que já existem)
// ══════════════════════════════════════════════════════════════

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
