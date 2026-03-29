using MengolNews.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// ?? HttpClient GLOBAL (PROXY + SERVICES)
builder.Services.AddHttpClient("default", client =>
{
	client.Timeout = TimeSpan.FromSeconds(15);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
	AllowAutoRedirect = true // ?? ESSENCIAL para imagens
});

// ?? Service usando o HttpClient configurado
builder.Services.AddHttpClient<NoticiasService>();

// ?? CORS (mais flexível pra DEV)
builder.Services.AddCors(options =>
{
	options.AddPolicy("AllowBlazor", policy =>
	{
		policy
			.AllowAnyOrigin() // ?? libera tudo pra evitar bloqueio
			.AllowAnyHeader()
			.AllowAnyMethod();
	});
});

var app = builder.Build();

// Pipeline
app.UseHttpsRedirection();

app.UseCors("AllowBlazor");

app.UseAuthorization();

app.MapControllers();

app.Run();
