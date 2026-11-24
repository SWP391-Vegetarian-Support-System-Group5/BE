using BLL.Services;
using API.Infrastructure;

namespace API.Services;

public sealed class VideoRecipeProcessingWorker(
    IServiceScopeFactory scopeFactory,
    VideoRecipeSettings settings,
    ILogger<VideoRecipeProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var databaseFailures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(Math.Max(1, settings.PollIntervalSeconds));
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IVideoRecipeDraftService>().ProcessPendingAsync(stoppingToken);
                databaseFailures = 0;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested && DatabaseConnectivity.IsTransientFailure(exception))
            {
                databaseFailures = Math.Min(databaseFailures + 1, 3);
                delay = TimeSpan.FromSeconds(Math.Max(delay.TotalSeconds, 15 * (1 << databaseFailures)));
                logger.LogWarning("Database unavailable; video processing will retry in {DelaySeconds} seconds.", delay.TotalSeconds);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Video recipe processing worker failed.");
            }
            await Task.Delay(delay, stoppingToken);
        }
    }
}
