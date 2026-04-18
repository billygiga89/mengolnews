using System.Text.Json;

namespace MengolNews.Api.Services;

public class SquadService
{
	private readonly HttpClient _http;
	private readonly string _apiKey;

	// ID do Flamengo na API-Football
	private const int FlamengoId = 127;

	public SquadService(HttpClient http, IConfiguration config)
	{
		_http = http;
		_apiKey = config["ApiFootball:ApiKey"]
			   ?? throw new Exception("ApiFootball:ApiKey not configured");

		_http.BaseAddress = new Uri("https://v3.football.api-sports.io/");
		_http.DefaultRequestHeaders.Add("x-apisports-key", _apiKey);
	}

	/// <summary>
	/// Retorna o elenco atual do Flamengo com foto e dados dos jogadores.
	/// GET /players/squads?team=127
	/// </summary>
	public async Task<List<PlayerDto>?> GetFlamengoSquadAsync()
	{
		var response = await _http.GetAsync($"players/squads?team={FlamengoId}");
		if (!response.IsSuccessStatusCode) return null;

		var json = await response.Content.ReadAsStringAsync();
		var doc = JsonSerializer.Deserialize<JsonElement>(json);

		if (!doc.TryGetProperty("response", out var responseArr)) return null;
		var arr = responseArr.EnumerateArray().FirstOrDefault();
		if (arr.ValueKind == JsonValueKind.Undefined) return null;
		if (!arr.TryGetProperty("players", out var players)) return null;

		var result = new List<PlayerDto>();

		foreach (var p in players.EnumerateArray())
		{
			result.Add(new PlayerDto
			{
				Id = p.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
				Name = p.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "",
				Age = p.TryGetProperty("age", out var ag) ? ag.GetInt32() : 0,
				Number = p.TryGetProperty("number", out var nu) ? nu.ValueKind == JsonValueKind.Number ? nu.GetInt32() : 0 : 0,
				Position = p.TryGetProperty("position", out var po) ? po.GetString() ?? "" : "",
				Photo = p.TryGetProperty("photo", out var ph) ? ph.GetString() ?? "" : "",
			});
		}

		return result.OrderBy(p => PositionOrder(p.Position)).ThenBy(p => p.Number).ToList();
	}

	/// <summary>
	/// Retorna estatísticas da temporada atual de um jogador.
	/// GET /players?id={playerId}&season={year}&team=127
	/// </summary>
	public async Task<PlayerStatsDto?> GetPlayerStatsAsync(int playerId)
	{
		//var season = DateTime.UtcNow.Month >= 2 ? DateTime.UtcNow.Year : DateTime.UtcNow.Year - 1;
		var season = DateTime.UtcNow.Year;
		var response = await _http.GetAsync($"players?id={playerId}&season={season}&team={FlamengoId}");
		if (!response.IsSuccessStatusCode) return null;

		var json = await response.Content.ReadAsStringAsync();
		var doc = JsonSerializer.Deserialize<JsonElement>(json);

		if (!doc.TryGetProperty("response", out var arr)) return null;
		var first = arr.EnumerateArray().FirstOrDefault();
		if (first.ValueKind == JsonValueKind.Undefined) return null;

		var player = first.GetProperty("player");
		var stats = first.GetProperty("statistics").EnumerateArray().FirstOrDefault();

		return new PlayerStatsDto
		{
			Id = player.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
			Name = player.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "",
			Firstname = player.TryGetProperty("firstname", out var fn) ? fn.GetString() ?? "" : "",
			Lastname = player.TryGetProperty("lastname", out var ln) ? ln.GetString() ?? "" : "",
			Age = player.TryGetProperty("age", out var ag) ? ag.GetInt32() : 0,
			Nationality = player.TryGetProperty("nationality", out var na) ? na.GetString() ?? "" : "",
			Photo = player.TryGetProperty("photo", out var ph) ? ph.GetString() ?? "" : "",
			Height = player.TryGetProperty("height", out var ht) ? ht.GetString() ?? "" : "",
			Weight = player.TryGetProperty("weight", out var wt) ? wt.GetString() ?? "" : "",
			Injured = player.TryGetProperty("injured", out var inj) ? inj.GetBoolean() : false,

			// Estatísticas
			Appearances = GetStatInt(stats, "games", "appearences"),
			Starts = GetStatInt(stats, "games", "lineups"),
			MinutesPlayed = GetStatInt(stats, "games", "minutes"),
			Goals = GetStatInt(stats, "goals", "total"),
			Assists = GetStatInt(stats, "goals", "assists"),
			YellowCards = GetStatInt(stats, "cards", "yellow"),
			RedCards = GetStatInt(stats, "cards", "red"),
			Shots = GetStatInt(stats, "shots", "total"),
			ShotsOn = GetStatInt(stats, "shots", "on"),
			Passes = GetStatInt(stats, "passes", "total"),
			PassAccuracy = GetStatStr(stats, "passes", "accuracy"),
			Tackles = GetStatInt(stats, "tackles", "total"),
			Rating = GetStatStr(stats, "games", "rating"),
		};
	}

	private static int GetStatInt(JsonElement stats, string section, string field)
	{
		if (stats.ValueKind == JsonValueKind.Undefined) return 0;
		if (!stats.TryGetProperty(section, out var sec)) return 0;
		if (!sec.TryGetProperty(field, out var val)) return 0;
		return val.ValueKind == JsonValueKind.Number ? val.GetInt32() : 0;
	}

	private static string GetStatStr(JsonElement stats, string section, string field)
	{
		if (stats.ValueKind == JsonValueKind.Undefined) return "";
		if (!stats.TryGetProperty(section, out var sec)) return "";
		if (!sec.TryGetProperty(field, out var val)) return "";
		return val.ValueKind == JsonValueKind.Null ? "" : val.ToString();
	}

	private static int PositionOrder(string pos) => pos switch
	{
		"Goalkeeper" => 0,
		"Defender" => 1,
		"Midfielder" => 2,
		"Attacker" => 3,
		_ => 4
	};
}

public class PlayerDto
{
	public int Id { get; set; }
	public string Name { get; set; } = "";
	public int Age { get; set; }
	public int Number { get; set; }
	public string Position { get; set; } = "";
	public string Photo { get; set; } = "";
}

public class PlayerStatsDto : PlayerDto
{
	public string Firstname { get; set; } = "";
	public string Lastname { get; set; } = "";
	public string Nationality { get; set; } = "";
	public string Height { get; set; } = "";
	public string Weight { get; set; } = "";
	public bool Injured { get; set; }

	public int Appearances { get; set; }
	public int Starts { get; set; }
	public int MinutesPlayed { get; set; }
	public int Goals { get; set; }
	public int Assists { get; set; }
	public int YellowCards { get; set; }
	public int RedCards { get; set; }
	public int Shots { get; set; }
	public int ShotsOn { get; set; }
	public int Passes { get; set; }
	public string PassAccuracy { get; set; } = "";
	public int Tackles { get; set; }
	public string Rating { get; set; } = "";
}

