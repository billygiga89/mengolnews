using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Net.Sockets;

namespace MengolNews.Api.Controllers
{
    [ApiController]
    [Route("api/imagem")]
    public class ImageProxyController : ControllerBase
    {
        private const int LimiteBytes = 10 * 1024 * 1024; // 10 MB

        // Cliente único: reaproveita conexões e barra destinos internos
        private static readonly HttpClient _http = CriarClienteSeguro();

        private static HttpClient CriarClienteSeguro()
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3,
                ConnectTimeout = TimeSpan.FromSeconds(8),
                ConnectCallback = async (contexto, ct) =>
                {
                    // Resolve o endereço e só conecta se for público (vale também nos redirecionamentos)
                    var enderecos = await Dns.GetHostAddressesAsync(contexto.DnsEndPoint.Host, ct);
                    var ip = enderecos.FirstOrDefault(a => !EhEnderecoInterno(a))
                        ?? throw new HttpRequestException("Destino não permitido");

                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(ip, contexto.DnsEndPoint.Port), ct);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            };

            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        }

        private static bool EhEnderecoInterno(IPAddress ip)
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (IPAddress.IsLoopback(ip)) return true;

            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return ip.IsIPv6LinkLocal
                    || ip.IsIPv6SiteLocal
                    || ip.IsIPv6Multicast
                    || (ip.GetAddressBytes()[0] & 0xFE) == 0xFC; // fc00::/7
            }

            var b = ip.GetAddressBytes();
            return b[0] == 0
                || b[0] == 10
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168);
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string url, CancellationToken ct)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return BadRequest("URL inválida");

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);

                request.Headers.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                    "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                request.Headers.Accept.ParseAdd("image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
                request.Headers.AcceptLanguage.ParseAdd("pt-BR,pt;q=0.9,en;q=0.8");

                // Referer do próprio site da imagem (resolve a maioria dos bloqueios)
                request.Headers.Referrer = new Uri(uri.GetLeftPart(UriPartial.Authority));
                request.Headers.Add("Origin", "https://www.google.com");
                request.Headers.Add("Sec-Fetch-Dest", "image");
                request.Headers.Add("Sec-Fetch-Mode", "no-cors");
                request.Headers.Add("Sec-Fetch-Site", "cross-site");

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                if (!response.IsSuccessStatusCode)
                    return NotFound();

                var contentType = response.Content.Headers.ContentType?.MediaType;

                if (string.IsNullOrWhiteSpace(contentType)
                    || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                    || contentType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase))
                    return NotFound();

                if (response.Content.Headers.ContentLength > LimiteBytes)
                    return NotFound();

                // Lê tudo pra memória (com limite) ANTES do using descartar o response
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var memoria = new MemoryStream();
                var buffer = new byte[81920];
                int lidos;

                while ((lidos = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    memoria.Write(buffer, 0, lidos);
                    if (memoria.Length > LimiteBytes)
                        return NotFound();
                }

                Response.Headers.Append("Access-Control-Allow-Origin", "*");
                Response.Headers.Append("Cache-Control", "public, max-age=604800"); // 7 dias
                Response.Headers.Append("X-Content-Type-Options", "nosniff");

                return File(memoria.ToArray(), contentType);
            }
            catch
            {
                return NotFound();
            }
        }
    }
}