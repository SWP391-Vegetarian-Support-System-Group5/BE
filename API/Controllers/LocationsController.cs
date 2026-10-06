using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/locations")]
public sealed class LocationsController(ILocationService locationService) : ControllerBase
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
}
