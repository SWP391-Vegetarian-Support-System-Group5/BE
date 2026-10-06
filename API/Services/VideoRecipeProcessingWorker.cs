using BLL.Services;

namespace API.Services;

public sealed class VideoRecipeProcessingWorker(IServiceScopeFactory scopeFactory, ILogger<VideoRecipeProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IVideoRecipeDraftService>().ProcessPendingAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Video recipe processing worker failed.");
            }
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }
}
