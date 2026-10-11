using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Application.Outbox;
using Tropicast.Dashboard.Domain.Audit;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Infrastructure.Audit;

/// <summary>
/// Puts plan changes in the tenant's audit log, whoever made them (billing, an operator): from the
/// <c>TenantPlanChanged</c> event. Idempotent: an event already recorded is skipped.
/// </summary>
internal sealed class PlanChangeAudit(IServiceScopeFactory scopes) : IOutboxConsumer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public IReadOnlyCollection<string> EventTypes { get; } = [nameof(TenantPlanChanged)];

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var change = JsonSerializer.Deserialize<TenantPlanChanged>(message.Payload, Json)
            ?? throw new JsonException("Empty TenantPlanChanged payload.");
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entries = db.AuditEntries.IgnoreQueryFilters([AppDbContext.TenantFilter]);
        if (await entries.AnyAsync(e => e.TenantId == change.TenantId && e.Action == "plan.changed" && e.OccurredAt == change.OccurredAt,
                cancellationToken))
        {
            return;
        }
        db.AuditEntries.Add(AuditEntry.Record(change.TenantId, null, "plan.changed", nameof(Tenant), change.TenantId.ToString(),
            change.PlanId, change.OccurredAt));
        await db.SaveChangesAsync(cancellationToken);
    }
}
