using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Application.Billing;
using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Domain.Stations;
using Tropicast.Dashboard.Infrastructure.Billing;
using Tropicast.Dashboard.Infrastructure.Identity;
using Tropicast.Dashboard.Infrastructure.Persistence;
using Tropicast.Dashboard.Infrastructure.Telemetry;

namespace Tropicast.Dashboard.Api.Billing;

/// <summary>A plan as offered.</summary>
/// <param name="Id">Plan ID.</param>
/// <param name="Name">Display name.</param>
/// <param name="MaxStations">Stations.</param>
/// <param name="MaxListeners">Concurrent listeners per station.</param>
/// <param name="MaxBitrateKbps">Highest stream bitrate.</param>
/// <param name="DirectoryListing">RadioBrowser listing.</param>
/// <param name="Embed">Embeddable player.</param>
/// <param name="Price">Price to show, e.g. "€9 / month"; null for Free.</param>
/// <param name="Purchasable">Can be bought with checkout.</param>
internal sealed record PlanOfferResponse(string Id, string Name, int MaxStations, int MaxListeners, int MaxBitrateKbps, bool DirectoryListing,
    bool Embed, string? Price, bool Purchasable);

/// <summary>The tenant's plan and subscription.</summary>
/// <param name="PlanId">Current plan (what the tenant gets now).</param>
/// <param name="SubscriptionPlanId">Plan subscribed to, if any (differs from <see cref="PlanId"/> after a failed payment's grace period).</param>
/// <param name="Status">Subscription status; null without a subscription.</param>
/// <param name="CurrentPeriodEnd">End of the paid period.</param>
/// <param name="CancelAtPeriodEnd">The subscription ends at the period end.</param>
/// <param name="TrialEnd">End of the trial.</param>
/// <param name="GraceEndsAt">After a failed payment: when the tenant falls back to Free.</param>
/// <param name="TrialAvailable">The next checkout starts with a free trial.</param>
/// <param name="CanManage">A billing portal exists for this tenant (payment methods, invoices, plan changes, cancellation).</param>
/// <param name="Enabled">Billing is switched on for this deployment.</param>
/// <param name="Plans">Plans on offer, cheapest first.</param>
internal sealed record BillingResponse(string PlanId, string? SubscriptionPlanId, SubscriptionStatus? Status, DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd, DateTimeOffset? TrialEnd, DateTimeOffset? GraceEndsAt, bool TrialAvailable, bool CanManage, bool Enabled,
    IReadOnlyList<PlanOfferResponse> Plans);

internal sealed record CheckoutRequestBody([property: Required, MaxLength(32)] string PlanId);

/// <summary>Where to send the browser.</summary>
/// <param name="Url">The payment provider's page.</param>
internal sealed record RedirectResponse(Uri Url);

/// <summary>One station's use of a month.</summary>
internal sealed record StationUsageResponse(Guid StationId, string PublicId, string Name, int PeakListeners, double ListenerHours, long EgressBytes);

/// <summary>A tenant's use of a month, for fair-use review: from the daily statistics.</summary>
/// <param name="Month">The month (<c>YYYY-MM</c>, UTC).</param>
/// <param name="PlanId">The tenant's current plan.</param>
/// <param name="PeakListeners">Highest peak of any station on any day.</param>
/// <param name="ListenerHours">Listening time of all stations.</param>
/// <param name="EgressBytes">Estimated traffic of all stations.</param>
/// <param name="Stations">Per station, including deleted ones that streamed that month.</param>
/// <param name="Storage">Uploaded audio stored now (not monthly), out of the plan's quota.</param>
internal sealed record UsageResponse(string Month, string PlanId, int PeakListeners, double ListenerHours, long EgressBytes,
    IReadOnlyList<StationUsageResponse> Stations, Media.StorageUsageResponse Storage);

/// <summary>One tenant's use of a month (operator report).</summary>
internal sealed record TenantUsageResponse(Guid TenantId, string TenantName, string PlanId, int PeakListeners, double ListenerHours, long EgressBytes);

/// <summary>
/// Subscriptions with Stripe (#14; docs/adr/0002-payments-stripe.md): hosted checkout and billing portal, signed
/// idempotent webhooks, and monthly usage for fair use.
/// </summary>
internal static partial class BillingEndpoints
{
    internal static void MapBilling(this IEndpointRouteBuilder app)
    {
        var billing = app.MapGroup("/api/v1/billing").WithTags("Billing");
        billing.MapGet("", GetAsync).RequireAuthorization(TenantPolicies.Member)
            .WithSummary("The tenant's plan, subscription and the plans on offer.");
        billing.MapPost("/checkout", CheckoutAsync).RequireAuthorization(TenantPolicies.Owner).AddEndpointFilter<RequestValidation>()
            .WithSummary("Starts a hosted checkout for a paid plan; redirect the browser to the returned URL.")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        billing.MapPost("/portal", PortalAsync).RequireAuthorization(TenantPolicies.Owner)
            .WithSummary("Opens the billing portal: payment methods, invoices, plan changes (applied at period end), cancellation.");
        billing.MapGet("/usage", UsageAsync).RequireAuthorization(TenantPolicies.Admin)
            .WithSummary("The tenant's peak listeners, listener-hours and estimated egress in a month (UTC), per station.");
        billing.MapPost("/webhook", WebhookAsync).AllowAnonymous().DisableAntiforgery().ExcludeFromDescription();

        app.MapGet("/api/v1/operator/usage", OperatorUsageAsync).WithTags("Operator").RequireAuthorization(Operator.OperatorEndpoints.Policy)
            .WithSummary("Every tenant's use in a month (zero for idle ones), heaviest egress first, for fair-use true-up.");
    }

    private static async Task<Ok<BillingResponse>> GetAsync(AppDbContext db, IPaymentProvider payments, IOptions<BillingOptions> options,
        TimeProvider time, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleAsync(cancellationToken);
        var subscription = await db.Subscriptions.SingleOrDefaultAsync(cancellationToken);
        var settings = options.Value;
        var plans = await db.Plans.OrderBy(p => p.SortOrder).ToListAsync(cancellationToken);
        var grace = subscription is { Status: SubscriptionStatus.PastDue, PastDueSince: { } since } ? since + settings.GracePeriod : (DateTimeOffset?)null;
        return TypedResults.Ok(new BillingResponse(tenant.PlanId, subscription?.PlanId == Plan.Free.Id ? null : subscription?.PlanId,
            subscription?.Status, subscription?.CurrentPeriodEnd, subscription?.CancelAtPeriodEnd ?? false, subscription?.TrialEnd, grace,
            settings.TrialDays > 0 && subscription?.TrialUsed != true, subscription?.ProviderCustomerId is not null, settings.Enabled,
            [.. plans.Select(p => new PlanOfferResponse(p.Id, p.Name, p.MaxStations, p.MaxListeners, p.MaxBitrateKbps, p.DirectoryListing,
                p.Embed, settings.DisplayPrices.GetValueOrDefault(p.Id), settings.Enabled && payments.Sells(p.Id)))]));
    }

    private static async Task<Results<Ok<RedirectResponse>, ProblemHttpResult>> CheckoutAsync(CheckoutRequestBody request, HttpContext context,
        UserManager<AppUser> users, AppDbContext db, IPaymentProvider payments, IOptions<BillingOptions> options, IOptions<AppOptions> app,
        IOptions<StreamingOptions> streaming, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled || !payments.Sells(request.PlanId))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "This plan cannot be bought here.");
        }
        var tenant = await db.Tenants.SingleAsync(cancellationToken);
        var subscription = await db.Subscriptions.SingleOrDefaultAsync(cancellationToken);
        if (subscription?.Status is SubscriptionStatus.Active or SubscriptionStatus.Trialing or SubscriptionStatus.PastDue)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "This tenant already has a subscription: change or cancel it in the billing portal.");
        }
        // Before taking a payment: the upgraded stations must still fit on their node (#58).
        if (await db.Plans.SingleOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken) is { } plan
            && NodeLoad.WhyNotOnPlan(await NodeLoad.ReadAsync(db, streaming.Value.Node, cancellationToken), tenant.Id, plan,
                streaming.Value.Limits) is { } reason)
        {
            var logger = loggers.CreateLogger(typeof(BillingEndpoints));
            NodeLoad.LogNodeFull(logger, streaming.Value.Node, reason);
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "No streaming capacity is free for this plan right now. The operator has been alerted; try again later.");
        }
        var user = await users.GetUserAsync(context.User) ?? throw new InvalidOperationException("Signed-in user not found.");
        var account = new Uri(new Uri(app.Value.PublicBaseUrl), "/account");
        try
        {
            var url = await payments.CreateCheckoutAsync(new CheckoutRequest(tenant.Id, request.PlanId, user.Email!,
                subscription?.ProviderCustomerId, subscription?.TrialUsed == true ? 0 : options.Value.TrialDays,
                new Uri($"{account}?billing=success"), new Uri($"{account}?billing=canceled")), cancellationToken);
            return TypedResults.Ok(new RedirectResponse(url));
        }
        catch (BillingException ex)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: ex.Message);
        }
    }

    private static async Task<Results<Ok<RedirectResponse>, ProblemHttpResult>> PortalAsync(AppDbContext db, IPaymentProvider payments,
        IOptions<AppOptions> app, CancellationToken cancellationToken)
    {
        var customer = (await db.Subscriptions.SingleOrDefaultAsync(cancellationToken))?.ProviderCustomerId;
        if (customer is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "This tenant has no billing account yet: choose a plan first.");
        }
        try
        {
            return TypedResults.Ok(new RedirectResponse(await payments.CreatePortalAsync(customer, new Uri(new Uri(app.Value.PublicBaseUrl), "/account"),
                cancellationToken)));
        }
        catch (BillingException ex)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: ex.Message);
        }
    }

    /// <summary>
    /// Stripe webhook: verified signature, then applied once (event ID recorded). 400 for a bad signature; 500 on a
    /// failure, so Stripe retries; 200 otherwise, including replays.
    /// </summary>
    private static async Task<IResult> WebhookAsync(HttpContext context, IPaymentProvider payments, BillingService billing,
        ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        var logger = loggers.CreateLogger(typeof(BillingEndpoints));
        // Bounded whatever the Content-Length says (chunked bodies have none): Stripe events are a few kilobytes.
        if (await ReadBoundedAsync(context.Request.Body, MaxWebhookBytes, cancellationToken) is not { } payload)
        {
            return Count("invalid", Results.StatusCode(StatusCodes.Status413PayloadTooLarge));
        }
        BillingEvent billingEvent;
        try
        {
            billingEvent = payments.ParseWebhook(payload, context.Request.Headers["Stripe-Signature"].ToString());
        }
        catch (BillingException ex)
        {
            LogRefused(logger, ex.Message);
            return Count("invalid", Results.BadRequest());
        }
        try
        {
            var outcome = await billing.ApplyAsync(billingEvent, cancellationToken);
            LogApplied(logger, billingEvent.Type, billingEvent.Id, outcome);
            return Count(outcome.ToString().ToLowerInvariant(), Results.Ok());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, billingEvent.Type, billingEvent.Id, ex.GetType().Name);
            return Count("error", Results.StatusCode(StatusCodes.Status500InternalServerError));
        }
    }

    private const int MaxWebhookBytes = 512 * 1024;

    /// <summary>The body as UTF-8 text, or null as soon as it exceeds <paramref name="max"/> bytes.</summary>
    internal static async Task<string?> ReadBoundedAsync(Stream body, int max, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > max)
            {
                return null;
            }
            buffer.Write(chunk, 0, read);
        }
        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static IResult Count(string result, IResult response)
    {
        DashboardMetrics.BillingWebhooks.Add(1, DashboardMetrics.Result(result));
        return response;
    }

    private static async Task<Results<Ok<UsageResponse>, ValidationProblem>> UsageAsync(string? month, AppDbContext db, TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (Month(month, time) is not { } range)
        {
            return InvalidMonth();
        }
        var tenant = await db.Tenants.SingleAsync(cancellationToken);
        // Deleted stations count too: they streamed that month.
        var rows = await db.StationStatsRollups.IgnoreQueryFilters([AppDbContext.DeletedFilter])
            .Where(r => r.Interval == RollupInterval.Day && r.PeriodStart >= range.Start && r.PeriodStart < range.End)
            .GroupBy(r => r.StationId)
            .Select(g => new { StationId = g.Key, Peak = g.Max(r => r.PeakListeners), Hours = g.Sum(r => r.ListenerHours), Egress = g.Sum(r => r.EgressBytes) })
            .Join(db.Stations.IgnoreQueryFilters([AppDbContext.DeletedFilter]), u => u.StationId, s => s.Id,
                (u, s) => new StationUsageResponse(s.Id, s.PublicId, s.Name, u.Peak, u.Hours, u.Egress))
            .ToListAsync(cancellationToken);
        var plan = await db.Plans.SingleAsync(p => p.Id == tenant.PlanId, cancellationToken);
        var storage = new Media.StorageUsageResponse(await Media.TrackEndpoints.UsedBytesAsync(db, time.GetUtcNow(), null, cancellationToken),
            plan.StorageQuotaBytes);
        return TypedResults.Ok(new UsageResponse(range.Label, tenant.PlanId, rows.Count == 0 ? 0 : rows.Max(r => r.PeakListeners),
            rows.Sum(r => r.ListenerHours), rows.Sum(r => r.EgressBytes), [.. rows.OrderByDescending(r => r.EgressBytes)], storage));
    }

    private static async Task<Results<Ok<List<TenantUsageResponse>>, ValidationProblem>> OperatorUsageAsync(string? month, AppDbContext db,
        TimeProvider time, CancellationToken cancellationToken)
    {
        if (Month(month, time) is not { } range)
        {
            return InvalidMonth();
        }
        var rollups = db.StationStatsRollups.IgnoreQueryFilters()
            .Where(r => r.Interval == RollupInterval.Day && r.PeriodStart >= range.Start && r.PeriodStart < range.End);
        var usage = await rollups
            .Join(db.Stations.IgnoreQueryFilters(), r => r.StationId, s => s.Id, (r, s) => new { r.PeakListeners, r.ListenerHours, r.EgressBytes, s.TenantId })
            .GroupBy(x => x.TenantId)
            .Select(g => new { TenantId = g.Key, Peak = g.Max(x => x.PeakListeners), Hours = g.Sum(x => x.ListenerHours), Egress = g.Sum(x => x.EgressBytes) })
            .ToDictionaryAsync(u => u.TenantId, cancellationToken);
        // Every tenant, idle ones with zero usage.
        var tenants = await db.Tenants.IgnoreQueryFilters().Select(t => new { t.Id, t.Name, t.PlanId }).ToListAsync(cancellationToken);
        var rows = tenants.Select(t => usage.TryGetValue(t.Id, out var u)
            ? new TenantUsageResponse(t.Id, t.Name, t.PlanId, u.Peak, u.Hours, u.Egress)
            : new TenantUsageResponse(t.Id, t.Name, t.PlanId, 0, 0, 0));
        return TypedResults.Ok(rows.OrderByDescending(r => r.EgressBytes).ThenBy(r => r.TenantName, StringComparer.Ordinal).ToList());
    }

    private sealed record MonthRange(string Label, DateTimeOffset Start, DateTimeOffset End);

    /// <summary><c>YYYY-MM</c>, default the current month (UTC).</summary>
    private static MonthRange? Month(string? month, TimeProvider time)
    {
        var now = time.GetUtcNow();
        DateTime start;
        if (string.IsNullOrEmpty(month))
        {
            start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        }
        else if (!DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out start))
        {
            return null;
        }
        var from = new DateTimeOffset(start.Year, start.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return new MonthRange(from.ToString("yyyy-MM", CultureInfo.InvariantCulture), from, from.AddMonths(1));
    }

    private static ValidationProblem InvalidMonth()
        => TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["month"] = ["Use YYYY-MM, e.g. 2026-10."] });

    [LoggerMessage(Level = LogLevel.Warning, Message = "Billing webhook refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Billing webhook {Type} {EventId}: {Outcome}")]
    private static partial void LogApplied(ILogger logger, string type, string eventId, WebhookOutcome outcome);

    [LoggerMessage(Level = LogLevel.Error, Message = "Billing webhook {Type} {EventId} failed ({ErrorType}); the provider retries")]
    private static partial void LogFailed(ILogger logger, string type, string eventId, string errorType);
}
