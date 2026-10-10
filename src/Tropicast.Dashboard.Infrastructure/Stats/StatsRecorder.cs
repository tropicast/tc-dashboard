using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tropicast.Dashboard.Application.Stats;
using Tropicast.Dashboard.Domain.Stations;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Infrastructure.Stats;

/// <summary>Writes a node sample to the database: rollups, live sessions and retention.</summary>
internal sealed class StatsRecorder(AppDbContext db)
{
    /// <summary>One writer per sample when several API instances collect.</summary>
    private const long LockKey = 0x54_43_53_54_41_54_53; // "TCSTATS"

    /// <summary>A source can be allowed just before the sample that misses it: give new sessions this long.</summary>
    internal static readonly TimeSpan SessionGrace = TimeSpan.FromSeconds(30);

    /// <summary>Adds the sample's listener time and egress to the 5-minute and daily rollups.</summary>
    public async Task RecordRollupsAsync(NodeSample? previous, NodeSample current, StatsOptions options, CancellationToken cancellationToken)
    {
        var increments = ListenerAccounting.Increments(previous, current, options.MaxGap, options.OverheadFactor);
        if (increments.Count == 0)
        {
            return;
        }
        var ids = await StationIdsAsync([.. increments.Select(i => i.PublicId)], cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var locked = await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({LockKey}) AS \"Value\"").SingleAsync(cancellationToken);
        if (!locked)
        {
            return;
        }
        var fiveMinutes = ListenerAccounting.FiveMinutePeriod(current.At);
        var day = ListenerAccounting.DayPeriod(current.At);
        foreach (var increment in increments)
        {
            if (!ids.TryGetValue(increment.PublicId, out var stationId))
            {
                continue;
            }
            var hours = increment.ListenerSeconds / 3600;
            foreach (var (interval, start) in new[] { (RollupInterval.FiveMinutes, fiveMinutes), (RollupInterval.Day, day) })
            {
                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO station_stats_rollups (station_id, interval, period_start, peak_listeners, listener_hours, egress_bytes)
                    VALUES ({stationId}, {interval.ToString()}, {start}, {increment.PeakListeners}, {hours}, {increment.EgressBytes})
                    ON CONFLICT (station_id, interval, period_start) DO UPDATE SET
                        peak_listeners = GREATEST(station_stats_rollups.peak_listeners, EXCLUDED.peak_listeners),
                        listener_hours = station_stats_rollups.listener_hours + EXCLUDED.listener_hours,
                        egress_bytes = station_stats_rollups.egress_bytes + EXCLUDED.egress_bytes
                    """, cancellationToken);
            }
        }
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Ends the live sessions of the node's stations whose mount is gone, and opens one for a live mount without
    /// (e.g. a source that connected while source auth was elsewhere).
    /// </summary>
    public async Task RecordSessionsAsync(NodeSample current, CancellationToken cancellationToken)
    {
        if (!current.IcecastUp)
        {
            return; // Unknown, not offline.
        }
        var live = current.Mounts.Select(m => (m.PublicId, m.Format)).ToHashSet();
        var open = await db.LiveSessions.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(l => l.EndedAt == null)
            .Join(db.StreamAssignments.IgnoreQueryFilters([AppDbContext.TenantFilter]).Where(a => a.Node == current.Node),
                l => l.StationId, a => a.StationId, (l, _) => l)
            .Join(db.Stations.IgnoreQueryFilters(), l => l.StationId, s => s.Id, (l, s) => new { Session = l, s.PublicId })
            .ToListAsync(cancellationToken);
        foreach (var gone in open.Where(o => !live.Contains((o.PublicId, o.Session.Format)) && o.Session.StartedAt < current.At - SessionGrace))
        {
            gone.Session.End(current.At);
        }
        var opened = open.Select(o => (o.PublicId, o.Session.Format)).ToHashSet();
        var missing = current.Mounts.Where(m => !opened.Contains((m.PublicId, m.Format))).ToList();
        if (missing.Count > 0)
        {
            var ids = await StationIdsAsync([.. missing.Select(m => m.PublicId)], cancellationToken);
            foreach (var mount in missing.Where(m => ids.ContainsKey(m.PublicId)))
            {
                db.LiveSessions.Add(LiveSession.Start(ids[mount.PublicId], mount.Format, mount.StartedAt ?? current.At));
            }
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e is DbUpdateConcurrencyException
            || e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Source auth or another instance changed the same sessions: the next sample settles it.
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Deletes 5-minute rollups past their retention; daily rollups stay.</summary>
    public Task<int> DeleteExpiredAsync(DateTimeOffset now, StatsOptions options, CancellationToken cancellationToken)
    {
        var cutoff = now - options.FiveMinuteRetention;
        return db.StationStatsRollups.IgnoreQueryFilters()
            .Where(r => r.Interval == RollupInterval.FiveMinutes && r.PeriodStart < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Station IDs by public ID, across tenants; deleted stations count no more.</summary>
    private Task<Dictionary<string, Guid>> StationIdsAsync(List<string> publicIds, CancellationToken cancellationToken)
        => db.Stations.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(s => publicIds.Contains(s.PublicId))
            .ToDictionaryAsync(s => s.PublicId, s => s.Id, cancellationToken);
}
