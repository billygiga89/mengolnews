namespace MengolNews.Api.Services
{
    public class VideosWarmupHostedService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;

        public VideosWarmupHostedService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Espera o cache de notícias terminar de aquecer primeiro,
            // evitando os dois warmups competindo por memória ao mesmo tempo
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);

            using var scope = _serviceProvider.CreateScope();
            var videosService = scope.ServiceProvider.GetRequiredService<VideosService>();
            await videosService.AquecerCacheAsync();
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
