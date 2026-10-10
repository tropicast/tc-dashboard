using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Application.Provisioning;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Infrastructure.Provisioning;

/// <summary>
/// Makes the node's <c>stations.json</c> match the database: renders the desired state, and applies it when its
/// version differs from the last applied one. One instance at a time (PostgreSQL advisory lock). Never throws:
/// failures are recorded on <see cref="StreamingNodeState"/> and retried with backoff.
/// </summary>
internal sealed partial class Reconciler(AppDbContext db, IStreamingNodeClient client, IOptions<ProvisioningOptions> options,
    TimeProvider time, ILogger<Reconciler> logger)
{
    private const long LockKey = 0x7472_6F70_6963_6173; // "tropicas"

    public async Task<StreamingNodeState?> ReconcileAsync(CancellationToken cancellationToken)
    {
        var node = options.Value.Node;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var locked = await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({LockKey}) AS \"Value\"").SingleAsync(cancellationToken);
        if (!locked)
        {
            return null; // Another instance is reconciling.
        }

        var onNode = db.StreamAssignments.IgnoreQueryFilters([AppDbContext.TenantFilter]).Where(a => a.Node == node);
        var stations = await db.Stations.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(s => onNode.Any(a => a.StationId == s.Id))
            .Join(db.Tenants.IgnoreQueryFilters([AppDbContext.TenantFilter]).Where(t => t.Status == TenantStatus.Active),
                s => s.TenantId, t => t.Id, (s, t) => new { s.PublicId, t.PlanId })
            .Join(db.Plans, x => x.PlanId, p => p.Id, (x, p) => new StationLimits(x.PublicId, p))
            .ToListAsync(cancellationToken);
        var defaultCap = (await db.Plans.SingleAsync(p => p.Id == Domain.Plans.Plan.Free.Id, cancellationToken)).MaxListeners;
        var desired = StationLimitsDocument.Render(stations, defaultCap);

        var state = await db.StreamingNodes.SingleOrDefaultAsync(n => n.Node == node, cancellationToken);
        if (state is null)
        {
            state = StreamingNodeState.For(node);
            db.StreamingNodes.Add(state);
        }
        state.Want(desired.Version);
        var now = time.GetUtcNow();
        if (!state.InSync && state.Due(now))
        {
            try
            {
                await client.ApplyStationsAsync(node, desired.Json, cancellationToken);
                state.Applied(desired.Version, time.GetUtcNow());
                LogApplied(logger, node, desired.Version, stations.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                state.Failed($"{ex.GetType().Name}: {ex.Message}", time.GetUtcNow(), options.Value.RetryDelay);
                LogFailed(logger, node, ex.GetType().Name, state.FailedAttempts);
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return state;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied stations {Version} to {Node} ({Count} stations)")]
    private static partial void LogApplied(ILogger logger, string node, string version, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Applying stations to {Node} failed ({ErrorType}), attempt {Attempts}; will retry")]
    private static partial void LogFailed(ILogger logger, string node, string errorType, int attempts);
}
