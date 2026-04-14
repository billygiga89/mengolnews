using MengolNews;
using MengolNews.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// URL da API vinda do appsettings.json
var apiUrl = builder.Configuration["ApiBaseUrl"]
			 ?? "https://localhost:7221/";


builder.Services.AddScoped(sp => new HttpClient
{
	BaseAddress = new Uri(apiUrl),
	Timeout = TimeSpan.FromSeconds(90) //era 30
});

// Serviço que consome a API
builder.Services.AddScoped<NewsApiService>();

await builder.Build().RunAsync();

