using BLL.Common;
using BLL.DTOs;
using DAL.Data;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public sealed class SePaySettings
{
    public string AccountNumber { get; init; } = string.Empty;
    public string BankCode { get; init; } = string.Empty;
    public string? WebhookApiKey { get; init; }
    public string? WebhookHmacSecret { get; init; }
    public int PaymentExpiryMinutes { get; init; } = 15;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(AccountNumber) && !string.IsNullOrWhiteSpace(BankCode);
}

public interface IBillingService
{
    Task<IReadOnlyCollection<SubscriptionPlanResponse>> GetPlansAsync(bool includeInactive, CancellationToken ct = default);
    Task<SubscriptionPlanResponse> SavePlanAsync(int? id, SubscriptionPlanRequest request, CancellationToken ct = default);
    Task<AiUsageResponse> GetUsageAsync(int userId, CancellationToken ct = default);
    Task ConsumeAsync(int userId, string feature, CancellationToken ct = default);
    Task<PaymentTransactionResponse> CreatePaymentAsync(int userId, int planId, CancellationToken ct = default);
    Task<PaymentTransactionResponse?> GetPaymentAsync(long paymentId, int userId, CancellationToken ct = default);
    Task HandleSePayWebhookAsync(SePayWebhookRequest request, CancellationToken ct = default);
}

public sealed class BillingService(VegetarianDbContext db, SePaySettings sePaySettings) : IBillingService
{
    private const string FreePlanCode = "FREE";
    public const string ChatFeature = "CHATBOT";
    public const string AiPostFeature = "AI_POST";

    public async Task<IReadOnlyCollection<SubscriptionPlanResponse>> GetPlansAsync(bool includeInactive, CancellationToken ct = default)
    {
        await EnsureFreePlanAsync(ct);
        var query = db.SubscriptionPlans.AsNoTracking();
        if (!includeInactive) query = query.Where(x => x.IsActive);
        var plans = await query.OrderBy(x => x.PriceVnd).ThenBy(x => x.Name).ToListAsync(ct);
        return plans.Select(ToResponse).ToList();
    }

    public async Task<SubscriptionPlanResponse> SavePlanAsync(int? id, SubscriptionPlanRequest request, CancellationToken ct = default)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z][A-Z0-9_-]{1,39}$"))
            throw new ServiceException("Plan code must use 2–40 uppercase letters, numbers, underscores, or hyphens.", 400);
        if (code == FreePlanCode && (request.PriceVnd != 0 || request.RefillIntervalHours != 24 * 30))
            throw new ServiceException("The FREE plan has a fixed monthly refill and zero price.", 400);

        var duplicate = await db.SubscriptionPlans.AnyAsync(x => x.Code == code && x.SubscriptionPlanId != id, ct);
        if (duplicate) throw new ServiceException("A plan with this code already exists.", 409);
        var now = VietnamTime.Now;
        var plan = id.HasValue
            ? await db.SubscriptionPlans.SingleOrDefaultAsync(x => x.SubscriptionPlanId == id.Value, ct) ?? throw new ServiceException("Plan was not found.", 404)
            : new SubscriptionPlan { CreatedAt = now };
        plan.Code = code;
        plan.Name = request.Name.Trim();
        plan.PriceVnd = request.PriceVnd;
        plan.ChatbotLimit = request.ChatbotLimit;
        plan.AiPostLimit = request.AiPostLimit;
        plan.RefillIntervalHours = request.RefillIntervalHours;
        plan.DurationDays = request.DurationDays;
        plan.IsActive = request.IsActive;
        plan.UpdatedAt = now;
        if (!id.HasValue) db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync(ct);
        return ToResponse(plan);
    }

    public async Task<AiUsageResponse> GetUsageAsync(int userId, CancellationToken ct = default)
    {
        var plan = await GetCurrentPlanAsync(userId, ct);
        var chat = await GetCounterAsync(userId, ChatFeature, plan, ct);
        var post = await GetCounterAsync(userId, AiPostFeature, plan, ct);
        await db.SaveChangesAsync(ct);
        return ToUsage(plan, chat, post, await GetActiveSubscriptionAsync(userId, ct));
    }

    public async Task ConsumeAsync(int userId, string feature, CancellationToken ct = default)
    {
        if (feature is not (ChatFeature or AiPostFeature)) throw new ArgumentOutOfRangeException(nameof(feature));
        var plan = await GetCurrentPlanAsync(userId, ct);
        var counter = await GetCounterAsync(userId, feature, plan, ct);
        var limit = feature == ChatFeature ? plan.ChatbotLimit : plan.AiPostLimit;
        if (counter.UsedCount >= limit)
            throw new ServiceException($"{(feature == ChatFeature ? "Chatbot" : "AI post") } quota has been used. It refills at {counter.NextRefillAt:O}.", 429);
        counter.UsedCount++;
        await db.SaveChangesAsync(ct);
    }

    public async Task<PaymentTransactionResponse> CreatePaymentAsync(int userId, int planId, CancellationToken ct = default)
    {
        if (!sePaySettings.IsConfigured)
            throw new ServiceException("SePay is not configured. Set SePay:AccountNumber and SePay:BankCode on the backend.", 503);
        var plan = await db.SubscriptionPlans.SingleOrDefaultAsync(x => x.SubscriptionPlanId == planId && x.IsActive, ct)
            ?? throw new ServiceException("Premium plan was not found or is inactive.", 404);
        if (plan.Code == FreePlanCode || plan.PriceVnd <= 0) throw new ServiceException("This plan does not require payment.", 400);
        var now = VietnamTime.Now;
        var payment = new PaymentTransaction
        {
            UserId = userId, SubscriptionPlanId = plan.SubscriptionPlanId,
            TransactionCode = $"VM-{Guid.NewGuid():N}".ToUpperInvariant()[..22], AmountVnd = plan.PriceVnd,
            Status = "PENDING", CreatedAt = now, ExpiresAt = now.AddMinutes(Math.Clamp(sePaySettings.PaymentExpiryMinutes, 5, 60))
        };
        db.PaymentTransactions.Add(payment);
        await db.SaveChangesAsync(ct);
        return ToPaymentResponse(payment, plan);
    }

    public async Task<PaymentTransactionResponse?> GetPaymentAsync(long paymentId, int userId, CancellationToken ct = default)
    {
        var payment = await db.PaymentTransactions.Include(x => x.Plan).SingleOrDefaultAsync(x => x.PaymentTransactionId == paymentId && x.UserId == userId, ct);
        if (payment is null) return null;
        if (payment.Status == "PENDING" && payment.ExpiresAt <= VietnamTime.Now)
        {
            payment.Status = "EXPIRED";
            await db.SaveChangesAsync(ct);
        }
        return ToPaymentResponse(payment, payment.Plan);
    }

    public async Task HandleSePayWebhookAsync(SePayWebhookRequest request, CancellationToken ct = default)
    {
        if (request.TransferAmount is not > 0) throw new ServiceException("Payment webhook does not contain a valid amount.", 400);
        var lookup = string.Join(" ", request.Code, request.Content, request.Description).Trim();
        if (string.IsNullOrWhiteSpace(lookup)) throw new ServiceException("Payment webhook does not contain an order code.", 400);
        // Match the SePay transfer text in memory. Provider messages contain a
        // user-entered description, so this is both more reliable and easier to
        // keep database-provider independent than translating String.Contains.
        var candidates = await db.PaymentTransactions.Include(x => x.Plan)
            .Where(x => x.Status == "PENDING" && x.ExpiresAt > VietnamTime.Now)
            .OrderByDescending(x => x.PaymentTransactionId).ToListAsync(ct);
        var payment = candidates.FirstOrDefault(x => lookup.Contains(x.TransactionCode, StringComparison.OrdinalIgnoreCase));
        if (payment is null) return; // Do not expose which codes exist to callers.
        if (request.TransferAmount.Value < payment.AmountVnd)
            throw new ServiceException("Payment amount is lower than the order amount.", 400);
        if (payment.Status == "PAID") return;
        var now = VietnamTime.Now;
        payment.Status = "PAID";
        payment.PaidAt = now;
        payment.ProviderTransactionId = request.ReferenceCode ?? request.Id?.ToString();
        payment.ProviderPayload = System.Text.Json.JsonSerializer.Serialize(request);
        var active = await GetActiveSubscriptionAsync(payment.UserId, ct);
        if (active is not null) active.Status = "EXPIRED";
        db.UserSubscriptions.Add(new UserSubscription
        {
            UserId = payment.UserId, SubscriptionPlanId = payment.SubscriptionPlanId, Status = "ACTIVE",
            StartedAt = now, ExpiresAt = now.AddDays(payment.Plan.DurationDays)
        });
        var counters = await db.AiUsageCounters.Where(x => x.UserId == payment.UserId).ToListAsync(ct);
        foreach (var counter in counters) ResetCounter(counter, payment.Plan, now);
        await db.SaveChangesAsync(ct);
    }

    private async Task<SubscriptionPlan> GetCurrentPlanAsync(int userId, CancellationToken ct)
        => (await GetActiveSubscriptionAsync(userId, ct))?.Plan ?? await EnsureFreePlanAsync(ct);

    private Task<UserSubscription?> GetActiveSubscriptionAsync(int userId, CancellationToken ct) =>
        db.UserSubscriptions.Include(x => x.Plan).Where(x => x.UserId == userId && x.Status == "ACTIVE" && x.ExpiresAt > VietnamTime.Now)
            .OrderByDescending(x => x.ExpiresAt).FirstOrDefaultAsync(ct);

    private async Task<SubscriptionPlan> EnsureFreePlanAsync(CancellationToken ct)
    {
        var plan = await db.SubscriptionPlans.SingleOrDefaultAsync(x => x.Code == FreePlanCode, ct);
        if (plan is not null) return plan;
        var now = VietnamTime.Now;
        plan = new SubscriptionPlan { Code = FreePlanCode, Name = "Free", PriceVnd = 0, ChatbotLimit = 10, AiPostLimit = 3, RefillIntervalHours = 24 * 30, DurationDays = 3650, IsActive = true, CreatedAt = now, UpdatedAt = now };
        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync(ct);
        return plan;
    }

    private async Task<AiUsageCounter> GetCounterAsync(int userId, string feature, SubscriptionPlan plan, CancellationToken ct)
    {
        var counter = await db.AiUsageCounters.SingleOrDefaultAsync(x => x.UserId == userId && x.Feature == feature, ct);
        var now = VietnamTime.Now;
        if (counter is null)
        {
            counter = new AiUsageCounter { UserId = userId, Feature = feature };
            ResetCounter(counter, plan, now);
            db.AiUsageCounters.Add(counter);
        }
        else if (counter.NextRefillAt <= now || NeedsNewPeriod(counter, plan)) ResetCounter(counter, plan, now);
        return counter;
    }

    private static bool NeedsNewPeriod(AiUsageCounter counter, SubscriptionPlan plan) =>
        plan.Code != FreePlanCode && counter.NextRefillAt - counter.PeriodStartedAt != TimeSpan.FromHours(plan.RefillIntervalHours);

    private static void ResetCounter(AiUsageCounter counter, SubscriptionPlan plan, DateTime now)
    {
        counter.UsedCount = 0;
        counter.PeriodStartedAt = now;
        counter.NextRefillAt = plan.Code == FreePlanCode
            ? new DateTime(now.Year, now.Month, 1).AddMonths(1)
            : now.AddHours(plan.RefillIntervalHours);
    }

    private static SubscriptionPlanResponse ToResponse(SubscriptionPlan x) => new(x.SubscriptionPlanId, x.Code, x.Name, x.PriceVnd, x.ChatbotLimit, x.AiPostLimit, x.RefillIntervalHours, x.DurationDays, x.IsActive);
    private static AiUsageResponse ToUsage(SubscriptionPlan plan, AiUsageCounter chat, AiUsageCounter post, UserSubscription? active) =>
        new(plan.Code, plan.Name, plan.Code != FreePlanCode, chat.UsedCount, plan.ChatbotLimit, post.UsedCount, plan.AiPostLimit, chat.NextRefillAt, active?.ExpiresAt);
    private PaymentTransactionResponse ToPaymentResponse(PaymentTransaction x, SubscriptionPlan plan)
    {
        var content = Uri.EscapeDataString(x.TransactionCode);
        var qr = $"https://vietqr.app/img?acc={Uri.EscapeDataString(sePaySettings.AccountNumber)}&bank={Uri.EscapeDataString(sePaySettings.BankCode)}&amount={x.AmountVnd:0}&des={content}";
        return new(x.PaymentTransactionId, x.TransactionCode, x.AmountVnd, x.Status, qr, x.ExpiresAt, plan.SubscriptionPlanId, plan.Name);
    }
}
