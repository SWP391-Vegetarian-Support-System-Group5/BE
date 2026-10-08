using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace API.Services;

public interface IOpenStreetMapService
{
    Task<IReadOnlyCollection<AddressSuggestionResponse>> GetAddressSuggestionsAsync(
        string query, decimal? latitude, decimal? longitude, CancellationToken cancellationToken);
    Task<GeocodeResponse?> GeocodeAsync(string address, CancellationToken cancellationToken);
    Task<string?> ReverseGeocodeAsync(decimal latitude, decimal longitude, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<NearbyPlaceResponse>> GetNearbyVegetarianPlacesAsync(
        decimal latitude, decimal longitude, decimal radiusKm, string provinceCode, string areaCode,
        CancellationToken cancellationToken);
}

public sealed record AddressSuggestionResponse(string Id, string Label, decimal Latitude, decimal Longitude);
public sealed record GeocodeResponse(decimal Latitude, decimal Longitude, string FormattedAddress);
public sealed record NearbyPlaceResponse(
    string Id,
    string Name,
    string Category,
    string Address,
    string ProvinceCode,
    string AreaCode,
    decimal Latitude,
    decimal Longitude,
    decimal Rating,
    int ReviewCount,
    decimal DistanceKm);

/// <summary>
/// Keeps public OpenStreetMap-provider traffic on the server so the browser only talks to VeggieMate.
/// Short coordinate rounding and caching prevent repeated map interactions from flooding public services.
/// </summary>
public sealed class OpenStreetMapService(IHttpClientFactory httpClientFactory, IMemoryCache cache) : IOpenStreetMapService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyCollection<AddressSuggestionResponse>> GetAddressSuggestionsAsync(
        string query, decimal? latitude, decimal? longitude, CancellationToken cancellationToken)
    {
        var normalizedQuery = query.Trim();
        var cacheKey = $"map:suggestions:{normalizedQuery.ToLowerInvariant()}:{Round(latitude)}:{Round(longitude)}";
        if (cache.TryGetValue(cacheKey, out AddressSuggestionResponse[]? cached) && cached is not null) return cached;

        var parameters = new Dictionary<string, string?>
        {
            ["q"] = normalizedQuery,
            ["limit"] = "5",
            ["lat"] = latitude?.ToString(CultureInfo.InvariantCulture),
            ["lon"] = longitude?.ToString(CultureInfo.InvariantCulture)
        };
        var uri = $"api?{BuildQuery(parameters)}";
        var payload = await GetJsonAsync<PhotonResponse>("Photon", uri, cancellationToken);
        var results = payload.Features
            .Where(feature => feature.Geometry.Coordinates.Count >= 2 && IsVietnam(feature.Properties.Country))
            .Select((feature, index) =>
            {
                var properties = feature.Properties;
                return new AddressSuggestionResponse(
                    properties.OsmId?.ToString(CultureInfo.InvariantCulture) ?? $"{feature.Geometry.Coordinates[0]}-{feature.Geometry.Coordinates[1]}-{index}",
                    FormatPhotonAddress(properties),
                    feature.Geometry.Coordinates[1],
                    feature.Geometry.Coordinates[0]);
            })
            .ToArray();

        cache.Set(cacheKey, results, TimeSpan.FromMinutes(30));
        return results;
    }

    public async Task<GeocodeResponse?> GeocodeAsync(string address, CancellationToken cancellationToken)
    {
        var normalizedAddress = address.Trim();
        var cacheKey = $"map:geocode:{normalizedAddress.ToLowerInvariant()}";
        if (cache.TryGetValue(cacheKey, out GeocodeResponse? cached)) return cached;

        var uri = $"api?{BuildQuery(new Dictionary<string, string?>
        {
            ["q"] = normalizedAddress,
            ["limit"] = "5"
        })}";
        var payload = await GetJsonAsync<PhotonResponse>("Photon", uri, cancellationToken);
        var first = payload.Features.FirstOrDefault(feature =>
            feature.Geometry.Coordinates.Count >= 2 && IsVietnam(feature.Properties.Country));
        var response = first is null ? null : new GeocodeResponse(
            first.Geometry.Coordinates[1], first.Geometry.Coordinates[0], FormatPhotonAddress(first.Properties));
        cache.Set(cacheKey, response, response is null ? TimeSpan.FromMinutes(5) : TimeSpan.FromHours(24));
        return response;
    }

    public async Task<string?> ReverseGeocodeAsync(decimal latitude, decimal longitude, CancellationToken cancellationToken)
    {
        var cacheKey = $"map:reverse:{Round(latitude)}:{Round(longitude)}";
        if (cache.TryGetValue(cacheKey, out string? cached)) return cached;

        var uri = $"reverse?{BuildQuery(new Dictionary<string, string?>
        {
            ["lat"] = latitude.ToString(CultureInfo.InvariantCulture),
            ["lon"] = longitude.ToString(CultureInfo.InvariantCulture)
        })}";
        var payload = await GetJsonAsync<PhotonResponse>("Photon", uri, cancellationToken);
        var result = payload.Features.FirstOrDefault();
        var address = result is null ? null : FormatPhotonAddress(result.Properties);
        cache.Set(cacheKey, address, TimeSpan.FromHours(24));
        return address;
    }

    public async Task<IReadOnlyCollection<NearbyPlaceResponse>> GetNearbyVegetarianPlacesAsync(
        decimal latitude, decimal longitude, decimal radiusKm, string provinceCode, string areaCode,
        CancellationToken cancellationToken)
    {
        var safeRadiusKm = Math.Clamp(radiusKm, 1m, 25m);
        var cacheKey = $"map:nearby:{Round(latitude)}:{Round(longitude)}:{safeRadiusKm}:{provinceCode}:{areaCode}";
        if (cache.TryGetValue(cacheKey, out NearbyPlaceResponse[]? cached) && cached is not null) return cached;

        try
        {
            // Photon usually responds in under a second and keeps the first map interaction responsive.
            var photonResults = await GetNearbyFromPhotonAsync(
                latitude, longitude, safeRadiusKm, provinceCode, areaCode, cancellationToken);
            if (photonResults.Length > 0)
            {
                cache.Set(cacheKey, photonResults, TimeSpan.FromMinutes(10));
                return photonResults;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Continue with the more detailed Overpass query when Photon is temporarily unavailable.
        }

        var radiusMetres = decimal.ToInt32(safeRadiusKm * 1000m);
        var lat = latitude.ToString(CultureInfo.InvariantCulture);
        var lon = longitude.ToString(CultureInfo.InvariantCulture);
        var query = $"""
            [out:json][timeout:25];(
              nwr["amenity"~"restaurant|cafe|fast_food"]["diet:vegetarian"="yes"](around:{radiusMetres},{lat},{lon});
              nwr["amenity"~"restaurant|cafe|fast_food"]["diet:vegan"="yes"](around:{radiusMetres},{lat},{lon});
              nwr["amenity"~"restaurant|cafe|fast_food"]["cuisine"~"vegetarian|vegan",i](around:{radiusMetres},{lat},{lon});
            );out center tags;
            """;
        OverpassResponse payload;
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = query });
            var client = httpClientFactory.CreateClient("Overpass");
            using var response = await client.PostAsync("api/interpreter", content, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            payload = await JsonSerializer.DeserializeAsync<OverpassResponse>(stream, JsonOptions, cancellationToken)
                ?? new OverpassResponse([]);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Both providers can occasionally be busy; an empty result keeps the map itself usable.
            return [];
        }

        var unique = new Dictionary<string, NearbyPlaceResponse>(StringComparer.Ordinal);
        foreach (var element in payload.Elements)
        {
            var elementLatitude = element.Latitude ?? element.Center?.Latitude;
            var elementLongitude = element.Longitude ?? element.Center?.Longitude;
            if (elementLatitude is null || elementLongitude is null) continue;
            var tags = element.Tags ?? [];
            var id = $"osm-{element.Type}-{element.Id}";
            unique[id] = new NearbyPlaceResponse(
                id,
                Value(tags, "name") ?? Value(tags, "name:vi") ?? "Địa điểm chay",
                Value(tags, "diet:vegan") == "yes" ? "Vegan" : "Vegetarian",
                FormatAddress(tags),
                provinceCode,
                areaCode,
                elementLatitude.Value,
                elementLongitude.Value,
                0m,
                0,
                Math.Round(DistanceKm(latitude, longitude, elementLatitude.Value, elementLongitude.Value), 1));
        }

        var results = unique.Values.OrderBy(place => place.DistanceKm).ToArray();
        cache.Set(cacheKey, results, TimeSpan.FromMinutes(15));
        return results;
    }

    private async Task<NearbyPlaceResponse[]> GetNearbyFromPhotonAsync(
        decimal latitude, decimal longitude, decimal radiusKm, string provinceCode, string areaCode,
        CancellationToken cancellationToken)
    {
        var uri = $"api?{BuildQuery(new Dictionary<string, string?>
        {
            ["q"] = "nhà hàng chay",
            ["limit"] = "30",
            ["lat"] = latitude.ToString(CultureInfo.InvariantCulture),
            ["lon"] = longitude.ToString(CultureInfo.InvariantCulture)
        })}";
        var payload = await GetJsonAsync<PhotonResponse>("Photon", uri, cancellationToken);
        return payload.Features
            .Where(feature => feature.Geometry.Coordinates.Count >= 2 && IsVietnam(feature.Properties.Country))
            .Select(feature =>
            {
                var placeLatitude = feature.Geometry.Coordinates[1];
                var placeLongitude = feature.Geometry.Coordinates[0];
                var distance = Math.Round(DistanceKm(latitude, longitude, placeLatitude, placeLongitude), 1);
                return new NearbyPlaceResponse(
                    $"osm-{feature.Properties.OsmType ?? "place"}-{feature.Properties.OsmId?.ToString(CultureInfo.InvariantCulture) ?? $"{placeLatitude}-{placeLongitude}"}",
                    feature.Properties.Name ?? "Địa điểm chay",
                    "Vegetarian",
                    FormatPhotonAddress(feature.Properties),
                    provinceCode,
                    areaCode,
                    placeLatitude,
                    placeLongitude,
                    0m,
                    0,
                    distance);
            })
            .Where(place => place.DistanceKm <= radiusKm)
            .GroupBy(place => place.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(place => place.DistanceKm)
            .ToArray();
    }

    private async Task<T> GetJsonAsync<T>(string clientName, string uri, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(clientName);
        using var response = await client.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            ?? throw new HttpRequestException("The map provider returned an empty response.");
    }

    private static string BuildQuery(IEnumerable<KeyValuePair<string, string?>> values) => string.Join("&", values
        .Where(item => !string.IsNullOrWhiteSpace(item.Value))
        .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value!)}"));

    private static string Round(decimal? value) => value is null
        ? "none"
        : Math.Round(value.Value, 3).ToString(CultureInfo.InvariantCulture);

    private static bool IsVietnam(string? country) => country is not null &&
        (country.Equals("Vietnam", StringComparison.OrdinalIgnoreCase) || country.Equals("Việt Nam", StringComparison.OrdinalIgnoreCase));

    private static string Join(string separator, params string?[] values) =>
        string.Join(separator, values.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string? Value(IReadOnlyDictionary<string, string> tags, string key) =>
        tags.TryGetValue(key, out var value) ? value : null;

    private static string FormatAddress(IReadOnlyDictionary<string, string> tags)
    {
        var street = Join(" ", Value(tags, "addr:housenumber"), Value(tags, "addr:street"));
        var address = Join(", ", street, Value(tags, "addr:suburb") ?? Value(tags, "addr:ward"),
            Value(tags, "addr:city") ?? Value(tags, "addr:province"));
        return string.IsNullOrWhiteSpace(address) ? "Chưa có địa chỉ trên OpenStreetMap" : address;
    }

    private static string FormatPhotonAddress(PhotonProperties properties)
    {
        var street = Join(" ", properties.HouseNumber, properties.Street ?? properties.Name);
        var address = Join(", ", street, properties.District, properties.City, properties.State, properties.Country);
        return string.IsNullOrWhiteSpace(address) ? properties.Name ?? "Chưa có địa chỉ trên OpenStreetMap" : address;
    }

    private static decimal DistanceKm(decimal latitude1, decimal longitude1, decimal latitude2, decimal longitude2)
    {
        static double Radians(decimal degrees) => (double)degrees * Math.PI / 180d;
        var latitudeDelta = Radians(latitude2 - latitude1);
        var longitudeDelta = Radians(longitude2 - longitude1);
        var a = Math.Pow(Math.Sin(latitudeDelta / 2d), 2d)
            + Math.Cos(Radians(latitude1)) * Math.Cos(Radians(latitude2)) * Math.Pow(Math.Sin(longitudeDelta / 2d), 2d);
        return (decimal)(6371d * 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a)));
    }

    private sealed record PhotonResponse(IReadOnlyCollection<PhotonFeature> Features);
    private sealed record PhotonFeature(PhotonGeometry Geometry, PhotonProperties Properties);
    private sealed record PhotonGeometry(IReadOnlyList<decimal> Coordinates);
    private sealed record PhotonProperties(
        [property: JsonPropertyName("osm_id")] long? OsmId,
        [property: JsonPropertyName("osm_type")] string? OsmType,
        string? Name,
        string? Street,
        [property: JsonPropertyName("housenumber")] string? HouseNumber,
        string? District,
        string? City,
        string? State,
        string? Country);
    private sealed record OverpassResponse(IReadOnlyCollection<OverpassElement> Elements);
    private sealed record OverpassElement(
        string Type,
        long Id,
        [property: JsonPropertyName("lat")] decimal? Latitude,
        [property: JsonPropertyName("lon")] decimal? Longitude,
        OverpassCenter? Center,
        Dictionary<string, string>? Tags);
    private sealed record OverpassCenter(
        [property: JsonPropertyName("lat")] decimal Latitude,
        [property: JsonPropertyName("lon")] decimal Longitude);
}
