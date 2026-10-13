using System.Text.Json;
using API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;

namespace API.Controllers;

[ApiController]
[Route("api/community")]
public class CommunityEventsController(CommunityEventStream stream) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("events")]
    public async Task Events(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-cache";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        Response.Headers["X-Accel-Buffering"] = "no";
        Response.ContentType = "text/event-stream";
        using var streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var subscription = stream.SubscribeAsync(streamCancellation.Token).GetAsyncEnumerator();
        // Register the subscriber before flushing the handshake to avoid losing the first event.
        var nextEvent = subscription.MoveNextAsync().AsTask();
        using var heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        var heartbeat = heartbeatTimer.WaitForNextTickAsync(streamCancellation.Token).AsTask();
        try
        {
            await Response.WriteAsync(": connected\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                if (await Task.WhenAny(nextEvent, heartbeat) == heartbeat)
                {
                    if (!await heartbeat) break;
                    await Response.WriteAsync(": heartbeat\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                    heartbeat = heartbeatTimer.WaitForNextTickAsync(streamCancellation.Token).AsTask();
                    continue;
                }
                if (!await nextEvent) break;
                var item = subscription.Current;
                var json = JsonSerializer.Serialize(item, JsonOptions);
                await Response.WriteAsync($"event: {item.Type}\n", cancellationToken);
                await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                nextEvent = subscription.MoveNextAsync().AsTask();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (IOException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            streamCancellation.Cancel();
            try { await nextEvent; } catch (OperationCanceledException) when (streamCancellation.IsCancellationRequested) { }
            try { await heartbeat; } catch (OperationCanceledException) when (streamCancellation.IsCancellationRequested) { }
        }
    }
}
