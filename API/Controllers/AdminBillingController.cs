using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/admin/billing")]
public sealed class AdminBillingController(IBillingService billing) : ControllerBase
{
    [HttpGet("revenue")]
    public Task<PaymentRevenueResponse> Revenue(CancellationToken ct) => billing.GetRevenueAsync(ct);
}
