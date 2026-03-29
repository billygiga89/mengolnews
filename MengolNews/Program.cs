using MengolNews;
using MengolNews.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// HttpClient apontando SOMENTE para a API
builder.Services.AddScoped(sp =>
{
	var client = new HttpClient
	{
		BaseAddress = new Uri("https://localhost:7221/"),
		Timeout = TimeSpan.FromSeconds(30)
	};

	return client;
});

// Serviço que consome a API
builder.Services.AddScoped<NewsApiService>();

await builder.Build().RunAsync();

