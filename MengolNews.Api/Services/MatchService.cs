using System.Text.Json;

namespace MengolNews.Api.Services;

public class MatchService
{
	private readonly HttpClient _http;
	private readonly string _apiKey;

	public MatchService(HttpClient http, IConfiguration config)
	{
		_http = http;
		_apiKey = config["FootballData:ApiKey"] ?? throw new Exception("FootballData:ApiKey not configured");
		_http.BaseAddress = new Uri("https://api.football-data.org/v4/");
		_http.DefaultRequestHeaders.Add("X-Auth-Token", _apiKey);
	}

	public async Task<JsonElement?> GetMatchAsync(int matchId)
	{
		var response = await _http.GetAsync($"matches/{matchId}");
		if (!response.IsSuccessStatusCode) return null;
		var json = await response.Content.ReadAsStringAsync();
		return JsonSerializer.Deserialize<JsonElement>(json);
	}

	public async Task<JsonElement?> GetMatchLineupsAsync(int matchId)
	{
		// football-data.org v4 includes lineups inside the match endpoint
		return await GetMatchAsync(matchId);
	}

	public async Task<JsonElement?> GetLiveMatchesAsync()
	{
		var response = await _http.GetAsync("matches?status=LIVE");
		if (!response.IsSuccessStatusCode) return null;
		var json = await response.Content.ReadAsStringAsync();
		return JsonSerializer.Deserialize<JsonElement>(json);
	}

	public async Task<JsonElement?> GetTodayMatchesAsync()
	{
		var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
		var response = await _http.GetAsync($"matches?dateFrom={today}&dateTo={today}");
		if (!response.IsSuccessStatusCode) return null;
		var json = await response.Content.ReadAsStringAsync();
		return JsonSerializer.Deserialize<JsonElement>(json);
	}
}

