using System.Text.Json;

namespace MengolNews.Api.Services;

public class SquadService
{
	private readonly HttpClient _http;
	private readonly string _apiKey;

	// ID do Flamengo na football-data.org
	private const int FlamengoId = 1783;
	// Código do Brasileirão na football-data.org
	private const string BrasileiraoCode = "BSA";

	// Fotos dos jogadores — adicione aqui quando tiver as URLs
	// Chave: nome exato do jogador (como vem da API)
	// Valor: URL da foto
	private static readonly Dictionary<string, string> FotosJogadores = new()
{
    // Goleiros
    { "Agustín Rossi",               "images/jogadores/goleiro-rossi.jpg"    },
	{ "Francisco Dyogo",             "images/jogadores/dyogo-alves.jpg"      },
	{ "Léo Nannetti",                "images/jogadores/leo-nannetti.webp"    },
	{ "Andrew",                      "images/jogadores/andrew.jpg"           },
	{ "Pedro Henrique",              "images/jogadores/pedro-henrique.jpg"   },
    // Defensores
    { "Leo Ortiz",                   "images/jogadores/leo-ortiz.jpg"         },
	{ "Ayrton Lucas",                "images/jogadores/airton-lucas.jpg"      },
	{ "Guillermo Varela",            "images/jogadores/varela.webp"           },
	{ "Léo Pereira",                 "images/jogadores/leo-pereira.jpg"       },
	{ "Vitão",                       "images/jogadores/vitao.jpg"             },
	{ "Daniel Sales",                "images/jogadores/daniel-sales.webp"     },
	{ "João Pedro",                  "images/jogadores/joao-pedro.jpg"        },
	{ "Gusttavo",                    "images/jogadores/gusttavo-sousa.jpg"    },
	{ "Johnny Goes",                 "images/jogadores/johnny.webp"           },
	{ "Antonio Da Silva",            ""                                       },
	{ "Daniel Carvalho",             "images/jogadores/daniel-carvalho.webp"  },
	{ "Danilo",                      "images/jogadores/danilo.jpg"            },
	{ "Alex Sandro",                 "images/jogadores/alex-sandro.jpg"       },
	{ "Emerson Royal",               "images/jogadores/emerson-royal.jpg"     },
    // Meias
    { "Giorgian De Arrascaeta",      "images/jogadores/arrascaeta.webp"       },
	{ "Erick Pulgar",                "images/jogadores/pulgar.jpg"            },
	{ "Jorge Carrascal",             "images/jogadores/carrascal.jpg"         },
	{ "Nicolas de la Cruz",          "images/jogadores/de-la-cruz.webp"       },
	{ "Evertton",                    "images/jogadores/evertton-araujo.jpg"   },
	{ "Yan",                         "images/jogadores/yan.webp"              },
	{ "Caio",                        ""                                       },
	{ "João Victor",                 ""                                       },
	{ "Pablo Lucio",                 ""                                       },
	{ "Lucas",                       ""                                       },
	{ "Luiz Felipe",                 ""                                       },
	{ "Joshua",                      "images/jogadores/joshua.jpg"            },
	{ "Ramires",                     ""                                       },
	{ "Lucas Vitor",                 ""                                       },
	{ "Kaio",                        ""                                       },
	{ "Kaio Júnior",                 "images/jogadores/kaio-junior.jpg"       },
	{ "Juan Sayago",                 "images/jogadores/sayago.webp"           },
	{ "Jorginho",                    "images/jogadores/jorginho.jpg"          },
	{ "Lucas Paquetá",               "images/jogadores/lucas-paqueta.jpg"     },
	{ "Saúl",                        "images/jogadores/saul.jpg"              },
    // Atacantes
    { "Pedro",                       "images/jogadores/pedro.jpg"             },
	{ "Éverton",                     "images/jogadores/cebolinha.webp"        },
	{ "Bruno Henrique",              "images/jogadores/bruno-henrique.webp"   },
	{ "Luiz Araújo",                 "images/jogadores/luiz-araujo.jpg"       },
	{ "Gonzalo Plata",               "images/jogadores/plata.jpg"             },
	{ "Guilherme",                   "images/jogadores/guilherme.jpg"         },
	{ "Douglas Telles",              "images/jogadores/douglas-telles.png"    },
	{ "João Paulo",                  ""                                       },
	{ "David",                       ""                                       },
	{ "Alan Silva",                  "images/jogadores/alan-santos.jpg"       },
	{ "Ryan de Oliveira",            "images/jogadores/ryan-roberto.webp"     },
	{ "Samuel Lino",                 "images/jogadores/lino.jpg"              },
};

	// Números das camisas
	private static readonly Dictionary<string, int> NumerosCamisas = new()
{
    // Goleiros
    { "Agustín Rossi",               1  },
	{ "Francisco Dyogo",             23 },
	{ "Léo Nannetti",                37 },
	{ "Andrew",                      0  },
	{ "Pedro Henrique",              0  },
    // Defensores
    { "Leo Ortiz",                   3  },
	{ "Ayrton Lucas",                6  },
	{ "Guillermo Varela",            0  },
	{ "Léo Pereira",                 4  },
	{ "Vitão",                       24 },
	{ "Daniel Sales",                0  },
	{ "João Pedro",                  0  },
	{ "Gusttavo",                    0  },
	{ "Johnny Goes",                 0  },
	{ "Antonio Da Silva",            0  },
	{ "Daniel Carvalho",             0  },
	{ "Danilo",                      0  },
	{ "Alex Sandro",                 6  },
	{ "Emerson Royal",               2  },
    // Meias
    { "Giorgian De Arrascaeta",      14 },
	{ "Erick Pulgar",                5  },
	{ "Jorge Carrascal",             10 },
	{ "Nicolas de la Cruz",          18 },
	{ "Evertton",                    0  },
	{ "Yan",                         0  },
	{ "Caio",                        0  },
	{ "João Victor",                 0  },
	{ "Pablo Lucio",                 0  },
	{ "Lucas",                       0  },
	{ "Luiz Felipe",                 0  },
	{ "Joshua",                      0  },
	{ "Ramires",                     0  },
	{ "Lucas Vitor",                 0  },
	{ "Kaio",                        0  },
	{ "Kaio Júnior",                 26 },
	{ "Juan Sayago",                 0  },
	{ "Jorginho",                    8  },
	{ "Lucas Paquetá",               11 },
	{ "Saúl",                        0  },
    // Atacantes
    { "Pedro",                       9  },
	{ "Éverton",                     7  },
	{ "Bruno Henrique",              27 },
	{ "Luiz Araújo",                 17 },
	{ "Gonzalo Plata",               0  },
	{ "Guilherme",                   0  },
	{ "Douglas Telles",              0  },
	{ "João Paulo",                  0  },
	{ "David",                       0  },
	{ "Alan Silva",                  0  },
	{ "Ryan de Oliveira",            0  },
	{ "Samuel Lino",                 21 },
};

	public SquadService(HttpClient http, IConfiguration config)
	{
		_http = http;
		_apiKey = config["FootballData:ApiKey"]
			   ?? throw new Exception("FootballData:ApiKey not configured");

		_http.BaseAddress = new Uri("https://api.football-data.org/");
		_http.DefaultRequestHeaders.Add("X-Auth-Token", _apiKey);
	}

	/// <summary>
	/// Retorna o elenco atual do Flamengo buscando via Brasileirão.
	/// GET /v4/competitions/BSA/teams?season=2025
	/// </summary>
	public async Task<List<PlayerDto>?> GetFlamengoSquadAsync()
	{
		var season = DateTime.UtcNow.Year;
		var response = await _http.GetAsync($"v4/competitions/{BrasileiraoCode}/teams?season={season}");
		if (!response.IsSuccessStatusCode) return null;

		var json = await response.Content.ReadAsStringAsync();
		var doc = JsonSerializer.Deserialize<JsonElement>(json);

		if (!doc.TryGetProperty("teams", out var teams)) return null;

		// Encontra o Flamengo
		JsonElement flamengo = default;
		foreach (var team in teams.EnumerateArray())
		{
			if (team.TryGetProperty("id", out var idEl) && idEl.GetInt32() == FlamengoId)
			{
				flamengo = team;
				break;
			}
		}

		if (flamengo.ValueKind == JsonValueKind.Undefined) return null;
		if (!flamengo.TryGetProperty("squad", out var squad)) return null;

		var result = new List<PlayerDto>();

		foreach (var p in squad.EnumerateArray())
		{
			var name = p.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
			var dob = p.TryGetProperty("dateOfBirth", out var d) ? d.GetString() ?? "" : "";
			var age = CalcularIdade(dob);
			var pos = p.TryGetProperty("position", out var po) ? NormalizePosition(po.GetString() ?? "") : "";

			FotosJogadores.TryGetValue(name, out var foto);
			NumerosCamisas.TryGetValue(name, out var numero);

			result.Add(new PlayerDto
			{
				Id = p.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
				Name = name,
				Age = age,
				Number = numero,
				Position = pos,
				Photo = string.IsNullOrEmpty(foto) ? "" : foto,
				Nationality = p.TryGetProperty("nationality", out var nat) ? nat.GetString() ?? "" : "",
			});
		}

		return result
			.OrderBy(p => PositionOrder(p.Position))
			.ThenBy(p => p.Number == 0 ? int.MaxValue : p.Number)
			.ToList();
	}

	/// <summary>
	/// Retorna dados básicos de um jogador pelo ID.
	/// Como o plano free não tem endpoint de stats individuais,
	/// retorna os dados do elenco sem estatísticas de temporada.
	/// </summary>
	public async Task<PlayerStatsDto?> GetPlayerStatsAsync(int playerId)
	{
		var squad = await GetFlamengoSquadAsync();
		if (squad is null) return null;

		var player = squad.FirstOrDefault(p => p.Id == playerId);
		if (player is null) return null;

		var stats = new PlayerStatsDto
		{
			Id = player.Id,
			Name = player.Name,
			Age = player.Age,
			Number = player.Number,
			Position = player.Position,
			Photo = player.Photo,
			Nationality = player.Nationality,
		};

		// Busca scorers do Brasileirão para cruzar stats
		try
		{
			var season = DateTime.UtcNow.Year;
			var response = await _http.GetAsync($"v4/competitions/BSA/scorers?season={season}&limit=100");
			if (response.IsSuccessStatusCode)
			{
				var json = await response.Content.ReadAsStringAsync();
				var doc = JsonSerializer.Deserialize<JsonElement>(json);

				if (doc.TryGetProperty("scorers", out var scorers))
				{
					foreach (var scorer in scorers.EnumerateArray())
					{
						if (!scorer.TryGetProperty("player", out var p)) continue;
						if (!p.TryGetProperty("id", out var idEl)) continue;
						if (idEl.GetInt32() != playerId) continue;

						stats.Goals = scorer.TryGetProperty("goals", out var g) ? g.GetInt32() : 0;
						stats.Assists = scorer.TryGetProperty("assists", out var a) ? (a.ValueKind == JsonValueKind.Number ? a.GetInt32() : 0) : 0;
						stats.Appearances = scorer.TryGetProperty("playedMatches", out var pm) ? pm.GetInt32() : 0;
						break;
					}
				}
			}
		}
		catch { }

		return stats;
	}

	// ── Helpers ──────────────────────────────────────────────────────────────

	private static int CalcularIdade(string dateOfBirth)
	{
		if (!DateTime.TryParse(dateOfBirth, out var dob)) return 0;
		var today = DateTime.Today;
		var age = today.Year - dob.Year;
		if (dob.Date > today.AddYears(-age)) age--;
		return age;
	}

	// football-data.org usa "Defence" em vez de "Defender"
	private static string NormalizePosition(string pos) => pos switch
	{
		// Goleiro
		"Goalkeeper" => "Goalkeeper",
		// Defensores
		"Defence" => "Defender",
		"Centre-Back" => "Defender",
		"Left-Back" => "Defender",
		"Right-Back" => "Defender",
		// Meias
		"Midfield" => "Midfielder",
		"Defensive Midfield" => "Midfielder",
		"Central Midfield" => "Midfielder",
		"Attacking Midfield" => "Midfielder",
		"Left Midfield" => "Midfielder",
		"Right Midfield" => "Midfielder",
		// Atacantes
		"Offence" => "Attacker",
		"Centre-Forward" => "Attacker",
		"Left Winger" => "Attacker",
		"Right Winger" => "Attacker",
		"Secondary Striker" => "Attacker",
		_ => pos
	};

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
	public string Nationality { get; set; } = "";
}

public class PlayerStatsDto : PlayerDto
{
	public string Firstname { get; set; } = "";
	public string Lastname { get; set; } = "";
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
