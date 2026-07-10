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
            using var scope = _serviceProvider.CreateScope();
            var videosService = scope.ServiceProvider.GetRequiredService<VideosService>();
            await videosService.AquecerCacheAsync();
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
