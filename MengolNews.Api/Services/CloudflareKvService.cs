using System.Net.Http.Headers;
using System.Text;

namespace MengolNews.Api.Services
{
    // Fala com a API REST da Cloudflare Workers KV. Usada pra arquivar
    // notícias permanentemente, já que o disco do Render (free tier) é
    // apagado a cada deploy/restart e não pode ser usado pra isso.
    public class CloudflareKvService
    {
        private readonly HttpClient _http;
        private readonly bool _configurado;

        public CloudflareKvService(IHttpClientFactory factory, IConfiguration config)
        {
            var accountId = config["CloudflareKv:AccountId"] ?? "";
            var namespaceId = config["CloudflareKv:NamespaceId"] ?? "";
            var token = config["CloudflareKv:ApiToken"] ?? "";

            _configurado = !string.IsNullOrWhiteSpace(accountId)
                && !string.IsNullOrWhiteSpace(namespaceId)
                && !string.IsNullOrWhiteSpace(token);

            _http = factory.CreateClient("cloudflarekv");

            if (_configurado)
            {
                _http.BaseAddress = new Uri(
                    $"https://api.cloudflare.com/client/v4/accounts/{accountId}/storage/kv/namespaces/{namespaceId}/");
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }
        }

        public async Task<string?> GetAsync(string key)
        {
            if (!_configurado) return null;
            try
            {
                var res = await _http.GetAsync($"values/{Uri.EscapeDataString(key)}");
                if (!res.IsSuccessStatusCode) return null;
                return await res.Content.ReadAsStringAsync();
            }
            catch
            {
                return null;
            }
        }

        // Verifica existência sem gastar cota de escrita — só de leitura
        // (100 mil/dia no free tier, bem mais folgada que a de escrita, 1 mil/dia).
        public async Task<bool> ExistsAsync(string key)
        {
            if (!_configurado) return false;
            try
            {
                var res = await _http.GetAsync($"values/{Uri.EscapeDataString(key)}");
                return res.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> SetAsync(string key, string valorJson)
        {
            if (!_configurado) return false;
            try
            {
                var content = new StringContent(valorJson, Encoding.UTF8, "application/json");
                var res = await _http.PutAsync($"values/{Uri.EscapeDataString(key)}", content);
                return res.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
