// MengolNews.Api/Services/SerieAService.cs
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

public class SerieAService
{
	private readonly HttpClient _http;
	private readonly IConfiguration _config;
	private readonly IMemoryCache _cache;

	public SerieAService(HttpClient http, IConfiguration config, IMemoryCache cache)
	{
		_http = http;
		_config = config;
		_cache = cache;
	}

	private HttpRequestMessage CreateRequest(string url)
	{
		var req = new HttpRequestMessage(HttpMethod.Get, url);
		req.Headers.Add("X-Auth-Token", _config["FootballData:ApiKey"]);
		return req;
	}

	public async Task<JsonElement> GetStandingsAsync()
	{
		if (_cache.TryGetValue("seriea_standings", out JsonElement cached))
			return cached;

		var res = await _http.SendAsync(
			CreateRequest("https://api.football-data.org/v4/competitions/BSA/standings"));
		res.EnsureSuccessStatusCode();

		var data = JsonSerializer.Deserialize<JsonElement>(
			await res.Content.ReadAsStringAsync());

		_cache.Set("seriea_standings", data, TimeSpan.FromMinutes(30));
		return data;
	}

	public async Task<JsonElement> GetMatchesAsync(int? matchday = null)
	{
		var cacheKey = $"seriea_matches_{matchday ?? 0}";
		if (_cache.TryGetValue(cacheKey, out JsonElement cached))
			return cached;

		var url = "https://api.football-data.org/v4/competitions/BSA/matches";
		if (matchday.HasValue)
			url += $"?matchday={matchday}";

		var res = await _http.SendAsync(CreateRequest(url));
		res.EnsureSuccessStatusCode();

		var data = JsonSerializer.Deserialize<JsonElement>(
			await res.Content.ReadAsStringAsync());

		_cache.Set(cacheKey, data, TimeSpan.FromMinutes(10));
		return data;
	}
}
