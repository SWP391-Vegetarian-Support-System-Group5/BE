using System.Globalization;
using System.Text.Json;
using BLL.Common;
using BLL.DTOs;
using Microsoft.Extensions.Caching.Memory;

namespace API.Services;

public interface IRestaurantRouteService
{
    Task<RestaurantRouteResponse> GetRouteAsync(decimal originLatitude, decimal originLongitude, decimal destinationLatitude, decimal destinationLongitude, CancellationToken cancellationToken = default);
}

/// <summary>Small OSRM adapter used only for the classroom demo's in-app route display.</summary>
public sealed class RestaurantRouteService(IHttpClientFactory httpClientFactory, IMemoryCache cache, ILogger<RestaurantRouteService> logger) : IRestaurantRouteService
{
    public async Task<RestaurantRouteResponse> GetRouteAsync(decimal originLatitude, decimal originLongitude, decimal destinationLatitude, decimal destinationLongitude, CancellationToken cancellationToken = default)
    {
        var key = $"restaurant-route:{originLatitude:F5}:{originLongitude:F5}:{destinationLatitude:F5}:{destinationLongitude:F5}";
        if (cache.TryGetValue<RestaurantRouteResponse>(key, out var cached)) return cached!;

        var point = static (decimal value) => value.ToString(CultureInfo.InvariantCulture);
        var path = $"route/v1/driving/{point(originLongitude)},{point(originLatitude)};{point(destinationLongitude)},{point(destinationLatitude)}?overview=full&geometries=geojson";
        try
        {
            using var response = await httpClientFactory.CreateClient("OSRM").GetAsync(path, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"OSRM returned {(int)response.StatusCode}.");
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            var route = json.RootElement.GetProperty("routes")[0];
            var points = route.GetProperty("geometry").GetProperty("coordinates").EnumerateArray()
                .Select(pair => new RestaurantRoutePointResponse(pair[1].GetDecimal(), pair[0].GetDecimal())).ToList();
            var result = new RestaurantRouteResponse(
                Math.Round(route.GetProperty("distance").GetDecimal() / 1000m, 2),
                Math.Max(1, (int)Math.Ceiling(route.GetProperty("duration").GetDecimal() / 60m)),
                points);
            cache.Set(key, result, TimeSpan.FromMinutes(5));
            return result;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Could not retrieve a route from OSRM.");
            throw new ServiceException("Chưa thể tải chỉ đường. Vui lòng thử lại sau.", 503);
        }
    }
}
