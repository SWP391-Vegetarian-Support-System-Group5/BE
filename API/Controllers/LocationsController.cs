using BLL.DTOs;
using BLL.Services;
using API.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/locations")]
public sealed class LocationsController(ILocationService locationService, IOpenStreetMapService mapService) : ControllerBase
{
    [HttpGet("provinces")]
    public ActionResult<IReadOnlyCollection<ProvinceResponse>> GetProvinces() => Ok(locationService.GetProvinces());

    [HttpGet("provinces/{provinceCode}/areas")]
    public ActionResult<IReadOnlyCollection<AreaResponse>> GetAreas(string provinceCode)
    {
        var areas = locationService.GetAreas(provinceCode);
        return areas is null
            ? NotFound(new { message = "Province was not found." })
            : Ok(areas);
    }

    [HttpGet("address-suggestions")]
    public async Task<ActionResult<IReadOnlyCollection<AddressSuggestionResponse>>> GetAddressSuggestions(
        [FromQuery] string query,
        [FromQuery] decimal? latitude,
        [FromQuery] decimal? longitude,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 3)
            return BadRequest(new { message = "Query must contain at least 3 characters." });
        if (!ValidOptionalCoordinates(latitude, longitude))
            return BadRequest(new { message = "Latitude and longitude must both be supplied and valid." });

        return Ok(await mapService.GetAddressSuggestionsAsync(query, latitude, longitude, cancellationToken));
    }

    [HttpGet("geocode")]
    public async Task<ActionResult<GeocodeResponse>> Geocode([FromQuery] string address, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(address)) return BadRequest(new { message = "Address is required." });
        var result = await mapService.GeocodeAsync(address, cancellationToken);
        return result is null ? NotFound(new { message = "Address was not found." }) : Ok(result);
    }

    [HttpGet("reverse-geocode")]
    public async Task<ActionResult<object>> ReverseGeocode(
        [FromQuery] decimal latitude,
        [FromQuery] decimal longitude,
        CancellationToken cancellationToken)
    {
        if (!ValidCoordinates(latitude, longitude)) return BadRequest(new { message = "Coordinates are invalid." });
        var address = await mapService.ReverseGeocodeAsync(latitude, longitude, cancellationToken);
        return address is null ? NotFound(new { message = "Address was not found." }) : Ok(new { formattedAddress = address });
    }

    [HttpGet("nearby-places")]
    public async Task<ActionResult<IReadOnlyCollection<NearbyPlaceResponse>>> GetNearbyPlaces(
        [FromQuery] decimal latitude,
        [FromQuery] decimal longitude,
        [FromQuery] decimal radiusKm = 15,
        [FromQuery] string provinceCode = "",
        [FromQuery] string areaCode = "all",
        CancellationToken cancellationToken = default)
    {
        if (!ValidCoordinates(latitude, longitude)) return BadRequest(new { message = "Coordinates are invalid." });
        if (radiusKm is < 1 or > 25) return BadRequest(new { message = "Radius must be between 1 and 25 kilometres." });
        return Ok(await mapService.GetNearbyVegetarianPlacesAsync(
            latitude, longitude, radiusKm, provinceCode, areaCode, cancellationToken));
    }

    private static bool ValidOptionalCoordinates(decimal? latitude, decimal? longitude) =>
        latitude is null && longitude is null ||
        latitude is not null && longitude is not null && ValidCoordinates(latitude.Value, longitude.Value);

    private static bool ValidCoordinates(decimal latitude, decimal longitude) =>
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
}
