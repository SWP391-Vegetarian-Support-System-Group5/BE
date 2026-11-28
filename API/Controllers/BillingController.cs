using System.Security.Cryptography;
using System.Text;
using API.Infrastructure;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api")]
public class BillingController(IBillingService billing, SePaySettings sePaySettings) : ControllerBase
{
    [Authorize]
    [HttpGet("billing/plans")]
    public Task<IReadOnlyCollection<SubscriptionPlanResponse>> Plans(CancellationToken ct) => billing.GetPlansAsync(false, ct);

    [Authorize]
    [HttpGet("billing/usage")]
    public Task<AiUsageResponse> Usage(CancellationToken ct) => billing.GetUsageAsync(User.GetRequiredUserId(), ct);

    [Authorize]
    [HttpPost("billing/payments")]
    public async Task<ActionResult<PaymentTransactionResponse>> CreatePayment(CreatePaymentRequest request, CancellationToken ct)
    {
        var payment = await billing.CreatePaymentAsync(User.GetRequiredUserId(), request.PlanId, ct);
        return Created($"/api/billing/payments/{payment.PaymentTransactionId}", payment);
    }

    [Authorize]
    [HttpGet("billing/payments/{id:long}")]
    public async Task<ActionResult<PaymentTransactionResponse>> Payment(long id, CancellationToken ct) =>
        await billing.GetPaymentAsync(id, User.GetRequiredUserId(), ct) is { } payment ? Ok(payment) : NotFound();

    [AllowAnonymous]
    [HttpPost("payments/sepay/webhook")]
    public async Task<IActionResult> SePayWebhook(
        SePayWebhookRequest request,
        [FromHeader(Name = "X-API-Key")] string? webhookApiKey,
        CancellationToken ct)
    {
        if (!IsAuthorizedWebhook(webhookApiKey)) return Unauthorized();
        var result = await billing.HandleSePayWebhookAsync(request, ct);
        // SePay expects both HTTP 201 and a JSON success field. An unmatched
        // transfer is not a successful activation and needs reconciliation.
        return StatusCode(StatusCodes.Status201Created, new
        {
            success = result.Matched,
            result.Matched,
            result.Activated,
            result.PaymentTransactionId,
            result.Message
        });
    }

    private bool IsAuthorizedWebhook(string? webhookApiKey)
    {
        if (!string.IsNullOrWhiteSpace(sePaySettings.WebhookApiKey))
        {
            var actual = webhookApiKey
                ?? Request.Headers["Authorization"].FirstOrDefault()?.Replace("Apikey ", "", StringComparison.OrdinalIgnoreCase)
                ?? Request.Headers["X-API-Key"].FirstOrDefault();
            return actual is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(sePaySettings.WebhookApiKey));
        }
        // No secret means live webhook activation is intentionally blocked.
        return false;
    }
}

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/admin/subscription-plans")]
public class SubscriptionPlansAdminController(IBillingService billing) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<SubscriptionPlanResponse>> List(CancellationToken ct) => billing.GetPlansAsync(true, ct);

    [HttpPost]
    public async Task<ActionResult<SubscriptionPlanResponse>> Create(SubscriptionPlanRequest request, CancellationToken ct)
    {
        var plan = await billing.SavePlanAsync(null, request, ct);
        return Created($"/api/admin/subscription-plans/{plan.SubscriptionPlanId}", plan);
    }

    [HttpPut("{id:int}")]
    public Task<SubscriptionPlanResponse> Update(int id, SubscriptionPlanRequest request, CancellationToken ct) => billing.SavePlanAsync(id, request, ct);
}
