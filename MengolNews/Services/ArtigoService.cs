using System.Net.Http.Json;
using MengolNews.Models;

namespace MengolNews.Services;

/// <summary>
/// Guarda os artigos na memória: cada endereço da API é pedido uma única vez.
/// Se der erro, a próxima chamada tenta de novo.
/// </summary>
public class ArtigoService
{
    private readonly HttpClient _http;
    private Task<List<ArtigoDto>?>? _listaTask;
    private readonly Dictionary<string, Task<ArtigoDto?>> _artigos = new(StringComparer.OrdinalIgnoreCase);

    public ArtigoService(HttpClient http) => _http = http;

    public Task<List<ArtigoDto>?> GetListaAsync()
    {
        // refaz o pedido só se ainda não houve ou se o anterior falhou
        if (_listaTask == null || (_listaTask.IsCompletedSuccessfully && _listaTask.Result is null))
            _listaTask = CarregarListaAsync();

        return _listaTask;
    }

    public Task<ArtigoDto?> GetAsync(string slug)
    {
        if (!_artigos.TryGetValue(slug, out var task) ||
            (task.IsCompletedSuccessfully && task.Result is null))
        {
            task = CarregarArtigoAsync(slug);
            _artigos[slug] = task;
        }

        return task;
    }

    private async Task<List<ArtigoDto>?> CarregarListaAsync()
    {
        try
        {
            var lista = await _http.GetFromJsonAsync<List<ArtigoDto>>("api/artigos");

            if (lista != null)
            {
                // se a lista já trouxer o texto, o artigo abre sem nova requisição
                foreach (var a in lista)
                {
                    if (!string.IsNullOrEmpty(a.ConteudoHtml) && !_artigos.ContainsKey(a.Slug))
                        _artigos[a.Slug] = Task.FromResult<ArtigoDto?>(a);
                }
            }

            return lista;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<ArtigoDto?> CarregarArtigoAsync(string slug)
    {
        try
        {
            return await _http.GetFromJsonAsync<ArtigoDto>($"api/artigos/{Uri.EscapeDataString(slug)}");
        }
        catch (Exception)
        {
            return null;
        }
    }
}
