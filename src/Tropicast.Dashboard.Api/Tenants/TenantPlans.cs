using Microsoft.EntityFrameworkCore;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Tenants;

/// <summary>The current tenant's row lock and plan, for checks that must not race (limits, quotas, the last Owner).</summary>
internal static class TenantPlans
{
    /// <summary>
    /// Locks the tenant row until the transaction ends: concurrent requests of the tenant that take this lock run
    /// their check and write one after another. Call it inside a transaction.
    /// </summary>
    internal static async Task<Tenant> LockAsync(AppDbContext db, TenantAccess access, CancellationToken cancellationToken)
        => await db.Tenants.FromSql($"SELECT *, xmin FROM tenants WHERE id = {access.TenantId} FOR UPDATE")
            .AsNoTracking().SingleAsync(cancellationToken);

    /// <summary><see cref="LockAsync"/>, then the tenant's plan as of the lock.</summary>
    internal static async Task<Plan> LockPlanAsync(AppDbContext db, TenantAccess access, CancellationToken cancellationToken)
    {
        var tenant = await LockAsync(db, access, cancellationToken);
        return await db.Plans.SingleAsync(p => p.Id == tenant.PlanId, cancellationToken);
    }

    /// <summary>The current tenant's plan (tenant query filter), without a lock.</summary>
    internal static async Task<Plan> CurrentAsync(AppDbContext db, CancellationToken cancellationToken)
        => await db.Tenants.Join(db.Plans, t => t.PlanId, p => p.Id, (_, p) => p).SingleAsync(cancellationToken);
}
