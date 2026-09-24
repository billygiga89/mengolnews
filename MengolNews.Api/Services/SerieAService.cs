// MengolNews.Api/Services/SerieAService.cs
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

public class SerieAService
{
    private readonly HttpClient _http;     // football-data.org
    private readonly HttpClient _httpAf;   // api-football (cartões)
    private readonly IConfiguration _config;
    private readonly IMemoryCache _cache;
    private readonly string _afKey;

    public SerieAService(HttpClient http, IHttpClientFactory factory, IConfiguration config, IMemoryCache cache)
    {
        _http = http;
        _config = config;
        _cache = cache;

        _afKey = config["ApiFootball:ApiKey"] ?? "";
        _httpAf = factory.CreateClient("apifootball");
        _httpAf.BaseAddress = new Uri("https://v3.football.api-sports.io/");
        if (!string.IsNullOrEmpty(_afKey))
            _httpAf.DefaultRequestHeaders.Add("x-apisports-key", _afKey);
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

        var temJogoAtivo = data.GetProperty("matches").EnumerateArray().Any(m =>
        {
            var status = m.GetProperty("status").GetString();
            var utc = m.GetProperty("utcDate").GetString();
            var isHoje = DateTime.TryParse(utc, out var dt) && dt.Date == DateTime.UtcNow.Date;
            return isHoje && (status == "IN_PLAY" || status == "PAUSED" || status == "FINISHED");
        });

        var duracao = temJogoAtivo ? TimeSpan.FromMinutes(30) : TimeSpan.FromMinutes(30);
        _cache.Set(cacheKey, data, duracao);

        return data;
    }

    // ─────────────────────────────────────────────────────────
    // NOVO: ranking de cartões amarelos (api-football)
    // ─────────────────────────────────────────────────────────
    public async Task<List<CartaoAmareloDto>> GetCartoesAmarelosAsync()
    {
        const string cacheKey = "seriea_cartoes_amarelos";
        if (_cache.TryGetValue(cacheKey, out List<CartaoAmareloDto>? cached) && cached != null)
            return cached;

        if (string.IsNullOrEmpty(_afKey))
            return new(); // sem chave configurada — não tenta gastar cota à toa

        var lista = await BuscarCartoesAsync(DateTime.UtcNow.Year);

        // início de temporada: se ainda não há dados no ano atual, tenta o anterior
        if (lista.Count == 0)
            lista = await BuscarCartoesAsync(DateTime.UtcNow.Year - 1);

        // cacheia por 12h mesmo se vier vazia, pra não bater na API de novo
        // a cada requisição em caso de erro/temporada sem dados ainda
        _cache.Set(cacheKey, lista, TimeSpan.FromHours(12));
        return lista;
    }

    private async Task<List<CartaoAmareloDto>> BuscarCartoesAsync(int temporada)
    {
        var lista = new List<CartaoAmareloDto>();
        try
        {
            var res = await _httpAf.GetAsync($"players/topyellowcards?league=71&season={temporada}");
            if (!res.IsSuccessStatusCode) return lista;

            var doc = JsonSerializer.Deserialize<JsonElement>(await res.Content.ReadAsStringAsync());
            if (!doc.TryGetProperty("response", out var arr)) return lista;

            foreach (var item in arr.EnumerateArray())
            {
                var player = item.GetProperty("player");
                var stats = item.GetProperty("statistics")[0];
                var team = stats.GetProperty("team");
                var cards = stats.GetProperty("cards");

                lista.Add(new CartaoAmareloDto
                {
                    Nome = player.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Foto = player.TryGetProperty("photo", out var f) ? f.GetString() ?? "" : "",
                    Time = team.TryGetProperty("name", out var tn) ? tn.GetString() ?? "" : "",
                    Amarelos = cards.TryGetProperty("yellow", out var y) ? GetIntFlexible(y) : 0,
                });
            }
        }
        catch { /* falha silenciosa — página mostra só a tabela */ }

        return lista;
    }

    // api-football às vezes retorna número puro, às vezes {"total": N} — cobre os dois casos
    private static int GetIntFlexible(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number) return el.GetInt32();
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("total", out var t) &&
            t.ValueKind == JsonValueKind.Number) return t.GetInt32();
        return 0;
    }
}

public class CartaoAmareloDto
{
    public string Nome { get; set; } = "";
    public string Foto { get; set; } = "";
    public string Time { get; set; } = "";
    public int Amarelos { get; set; }
}