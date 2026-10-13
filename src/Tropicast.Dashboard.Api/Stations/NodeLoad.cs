using Microsoft.EntityFrameworkCore;
using Tropicast.Dashboard.Application.Provisioning;
using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Stations;

/// <summary>The stations a streaming node carries, and whether more fit (#58).</summary>
internal static partial class NodeLoad
{
    /// <summary>
    /// The plan of every station on <paramref name="node"/>, with its tenant: the same stations as the node's
    /// stations.json (suspended tenants are not provisioned, deleted stations are gone).
    /// </summary>
    public static async Task<List<(Guid TenantId, Plan Plan)>> ReadAsync(AppDbContext db, string node, CancellationToken cancellationToken)
    {
        var rows = await db.StreamAssignments.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(a => a.Node == node && a.Station.DeletedAt == null)
            .Join(db.Tenants.IgnoreQueryFilters([AppDbContext.TenantFilter]).Where(t => t.Status == TenantStatus.Active),
                a => a.Station.TenantId, t => t.Id, (_, t) => new { TenantId = t.Id, t.PlanId })
            .Join(db.Plans, x => x.PlanId, p => p.Id, (x, p) => new { x.TenantId, Plan = p })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => (r.TenantId, r.Plan))];
    }

    /// <summary>Serialises capacity decisions on one node for the rest of the current transaction.</summary>
    public static Task LockAsync(AppDbContext db, string node, CancellationToken cancellationToken)
        => db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({"node:" + node}))", cancellationToken);

    /// <summary>Null when the tenant's stations fit on the node under <paramref name="plan"/>; otherwise why not.</summary>
    public static string? WhyNotOnPlan(IReadOnlyList<(Guid TenantId, Plan Plan)> load, Guid tenantId, Plan plan, NodeLimits limits)
    {
        var own = load.Count(s => s.TenantId == tenantId);
        return own == 0 ? null
            : NodeCapacity.WhyNot(load.Where(s => s.TenantId != tenantId).Select(s => s.Plan).Concat(Enumerable.Repeat(plan, own)), limits);
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Streaming node {Node} is full: {Reason}. Add a node or raise its limits.")]
    public static partial void LogNodeFull(ILogger logger, string node, string reason);
}
