using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Tropicast.Dashboard.Application.Billing;
using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Infrastructure.Billing;

/// <summary>What happened to a webhook event.</summary>
public enum WebhookOutcome
{
    Applied,
    /// <summary>Already processed: nothing changed.</summary>
    Duplicate,
    /// <summary>Nothing to do (other event type, unknown tenant, or older than the state already applied).</summary>
    Ignored,
}

/// <summary>
/// Applies payment-provider events to the tenant's <see cref="Subscription"/> and plan, once each (the event ID is
/// recorded in the same transaction), and syncs plans when a grace period ends. A plan change raises
/// TenantPlanChanged: provisioning updates the node's listener caps, and the audit log records it.
/// </summary>
public sealed partial class BillingService(AppDbContext db, IOptions<BillingOptions> options, TimeProvider time, ILogger<BillingService> logger)
{
    public async Task<WebhookOutcome> ApplyAsync(BillingEvent billingEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(billingEvent);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await db.ProcessedWebhookEvents.AnyAsync(e => e.Id == billingEvent.Id, cancellationToken))
        {
            return WebhookOutcome.Duplicate;
        }
        db.ProcessedWebhookEvents.Add(ProcessedWebhookEvent.From(billingEvent.Id, billingEvent.Type, time.GetUtcNow()));
        var outcome = billingEvent.Kind == BillingEventKind.Ignored ? WebhookOutcome.Ignored : await ApplyChangeAsync(billingEvent, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The same event delivered twice at once: the other delivery applied it.
            return WebhookOutcome.Duplicate;
        }
        await transaction.CommitAsync(cancellationToken);
        return outcome;
    }

    private async Task<WebhookOutcome> ApplyChangeAsync(BillingEvent billingEvent, CancellationToken cancellationToken)
    {
        var subscriptions = db.Subscriptions.IgnoreQueryFilters([AppDbContext.TenantFilter]);
        var subscription = billingEvent.SubscriptionId is { } subscriptionId
            ? await subscriptions.SingleOrDefaultAsync(s => s.ProviderSubscriptionId == subscriptionId, cancellationToken)
            : null;
        var tenantId = billingEvent.TenantId ?? subscription?.TenantId;
        if (tenantId is null && billingEvent.CustomerId is { } customerId)
        {
            tenantId = (await subscriptions.FirstOrDefaultAsync(s => s.ProviderCustomerId == customerId, cancellationToken))?.TenantId;
        }
        var tenant = tenantId is null ? null
            : await db.Tenants.IgnoreQueryFilters([AppDbContext.TenantFilter]).SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
        {
            LogUnknownTenant(logger, billingEvent.Type, billingEvent.Id);
            return WebhookOutcome.Ignored;
        }
        subscription ??= await subscriptions.SingleOrDefaultAsync(s => s.TenantId == tenant.Id, cancellationToken);
        var now = time.GetUtcNow();
        if (subscription is null)
        {
            subscription = Subscription.Create(tenant.Id, Plan.Free.Id, SubscriptionStatus.Incomplete, now);
            db.Subscriptions.Add(subscription);
        }
        if (billingEvent.Kind == BillingEventKind.CheckoutCompleted)
        {
            subscription.Link(billingEvent.CustomerId!, billingEvent.SubscriptionId);
        }
        else if (!subscription.Apply(billingEvent.State!, billingEvent.CreatedAt))
        {
            return WebhookOutcome.Ignored;
        }
        Sync(tenant, subscription, now);
        return WebhookOutcome.Applied;
    }

    /// <summary>Moves tenants whose grace period ended (or whose state otherwise requires it) to their effective plan.</summary>
    public async Task<int> SyncPlansAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var rows = await db.Subscriptions.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Join(db.Tenants.IgnoreQueryFilters([AppDbContext.TenantFilter]), s => s.TenantId, t => t.Id, (s, t) => new { Subscription = s, Tenant = t })
            .ToListAsync(cancellationToken);
        var changed = rows.Count(r => Sync(r.Tenant, r.Subscription, now));
        var cutoff = now - ProcessedWebhookEvent.Retention;
        await db.ProcessedWebhookEvents.Where(e => e.ProcessedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return changed;
    }

    private bool Sync(Tenant tenant, Subscription subscription, DateTimeOffset now)
    {
        var plan = subscription.EffectivePlanId(now, options.Value.GracePeriod);
        if (tenant.PlanId == plan)
        {
            return false;
        }
        LogPlanChanged(logger, tenant.Id, tenant.PlanId, plan, subscription.Status);
        tenant.ChangePlan(plan, now);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Billing event {Type} {EventId} names no known tenant; ignored")]
    private static partial void LogUnknownTenant(ILogger logger, string type, string eventId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tenant {TenantId} plan {From} -> {To} (subscription {Status})")]
    private static partial void LogPlanChanged(ILogger logger, Guid tenantId, string from, string to, SubscriptionStatus status);
}

/// <summary>Applies grace-period ends every <see cref="BillingOptions.Interval"/> when billing is on.</summary>
internal sealed partial class BillingWorker(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes, IOptions<BillingOptions> options,
    TimeProvider time, ILogger<BillingWorker> logger) : Microsoft.Extensions.Hosting.BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }
        using var timer = new PeriodicTimer(options.Value.Interval, time);
        do
        {
            try
            {
                await using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateAsyncScope(scopes);
                await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                    .GetRequiredService<BillingService>(scope.ServiceProvider).SyncPlansAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Billing plan sync failed ({ErrorType})")]
    private static partial void LogFailed(ILogger logger, string errorType);
}
