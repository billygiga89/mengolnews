using MengolNews.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// HttpClient GLOBAL
builder.Services.AddHttpClient("default", client =>
{
	client.Timeout = TimeSpan.FromSeconds(15);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
	AllowAutoRedirect = true
});

// Services
builder.Services.AddHttpClient<NoticiasService>();
builder.Services.AddHttpClient<VideosService>();

// CORS
builder.Services.AddCors(options =>
{
	options.AddPolicy("AllowBlazor", policy =>
	{
		policy
			.AllowAnyOrigin()
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
