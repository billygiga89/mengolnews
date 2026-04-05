using Microsoft.Extensions.Hosting;

namespace MengolNews.Api.Services
{
	public class CacheWarmupService : BackgroundService
	{
		private readonly IServiceProvider _services;
		private readonly ILogger<CacheWarmupService> _logger;

		public CacheWarmupService(IServiceProvider services, ILogger<CacheWarmupService> logger)
		{
			_services = services;
			_logger = logger;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			// Aquece o cache logo ao iniciar
			await AquecerCache();

			// Renova a cada 8 minutos (seu cache dura 10min)
			using var timer = new PeriodicTimer(TimeSpan.FromMinutes(8));
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				await AquecerCache();
			}
		}

		private async Task AquecerCache()
		{
			try
			{
				using var scope = _services.CreateScope();
				var service = scope.ServiceProvider.GetRequiredService<NoticiasService>();
				await service.GetTodasNoticias();
				_logger.LogInformation("✅ Cache de notícias aquecido com sucesso");
			}
			catch (Exception ex)
			{
				_logger.LogWarning("⚠️ Falha ao aquecer cache: {msg}", ex.Message);
			}
		}
	}
}
