using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Domain;
using Tropicast.Dashboard.Domain.Stations;
using Tropicast.Dashboard.Infrastructure.Persistence;
using Tropicast.Dashboard.Infrastructure.Stats;

namespace Tropicast.Dashboard.Api.Stations;

/// <summary>One live stream of a station.</summary>
/// <param name="Format">Stream format.</param>
/// <param name="Listeners">Current listeners.</param>
/// <param name="BitrateKbps">Source bitrate, declared or measured; null until known.</param>
/// <param name="LiveSince">When the source connected.</param>
internal sealed record LiveOutputResponse(AudioFormat Format, int Listeners, double? BitrateKbps, DateTimeOffset? LiveSince);

/// <summary>A station's live status, at most one collector interval (15 s) old.</summary>
/// <param name="Live">Some source is connected; null when the node's state is not known (see <see cref="Stale"/>).</param>
/// <param name="Listeners">Listeners on all formats.</param>
/// <param name="Outputs">Live streams.</param>
/// <param name="UpdatedAt">When the streaming node was sampled.</param>
/// <param name="Stale">No recent sample: the node or the collector is down.</param>
internal sealed record StationStatusResponse(bool? Live, int Listeners, IReadOnlyList<LiveOutputResponse> Outputs, DateTimeOffset? UpdatedAt,
    bool Stale);

/// <summary>One period of statistics.</summary>
/// <param name="Start">Period start (UTC).</param>
/// <param name="PeakListeners">Highest listener count seen in the period.</param>
/// <param name="ListenerHours">Listening time of all listeners.</param>
/// <param name="EgressBytes">Estimated traffic: bitrate × listening time × network overhead.</param>
internal sealed record StatsPointResponse(DateTimeOffset Start, int PeakListeners, double ListenerHours, long EgressBytes);

/// <summary>Statistics of a station over a time range. Periods without listening time are absent.</summary>
/// <param name="Interval">Period length.</param>
/// <param name="From">Range start, inclusive.</param>
/// <param name="To">Range end, exclusive.</param>
/// <param name="PeakListeners">Highest peak of the range.</param>
/// <param name="ListenerHours">Total listening time.</param>
/// <param name="EgressBytes">Total estimated traffic.</param>
/// <param name="Points">The periods, oldest first.</param>
internal sealed record StationStatsResponse(RollupInterval Interval, DateTimeOffset From, DateTimeOffset To, int PeakListeners,
    double ListenerHours, long EgressBytes, IReadOnlyList<StatsPointResponse> Points);

/// <summary>A streaming node's traffic in the current billing period, against what the server price includes.</summary>
/// <param name="Node">Node name.</param>
/// <param name="EgressBytes">Outgoing traffic month to date.</param>
/// <param name="IncludedBytes">Included traffic (20 TB on the CX33).</param>
/// <param name="UsedPercent">Share of the included traffic used.</param>
/// <param name="UpdatedAt">When the node was sampled (Hetzner updates its counters every few minutes).</param>
internal sealed record NodeTrafficResponse(string Node, long EgressBytes, long IncludedBytes, double UsedPercent, DateTimeOffset UpdatedAt);

/// <summary>
/// Live status and listener statistics, from the stats collector (Infrastructure/Stats): no time-series database,
/// 5-minute rollups for 30 days and daily rollups forever.
/// </summary>
internal static class StationStatsEndpoints
{
    /// <summary>Longest range per request, by interval.</summary>
    private static readonly Dictionary<RollupInterval, TimeSpan> MaxRange = new()
    {
        [RollupInterval.FiveMinutes] = TimeSpan.FromDays(7),
        [RollupInterval.Day] = TimeSpan.FromDays(400),
    };

    internal static void MapStationStats(this IEndpointRouteBuilder app)
    {
        var stations = app.MapGroup("/api/v1/stations/{id:guid}").WithTags("Stations").RequireAuthorization(TenantPolicies.Member);
        stations.MapGet("/status", GetStatusAsync).WithSummary("Live or offline, listeners per format and source bitrate.");
        stations.MapGet("/stats", GetStatsAsync)
            .WithSummary("Peak listeners, listener-hours and estimated egress per 5 minutes (last 30 days) or per day.")
            .ProducesValidationProblem();

        app.MapGet("/api/v1/operator/streaming-nodes/{node}/traffic", GetNodeTraffic).WithTags("Operator")
            .RequireAuthorization(Operator.OperatorEndpoints.Policy)
            .WithSummary("Node egress month to date against the traffic included in the server price.");
    }

    private static async Task<Results<Ok<StationStatusResponse>, NotFound>> GetStatusAsync(Guid id, AppDbContext db, LiveStatusStore store,
        IOptions<StatsOptions> options, IOptions<StreamingOptions> streaming, TimeProvider time, CancellationToken cancellationToken)
    {
        var station = await db.Stations.Where(s => s.Id == id)
            .Select(s => new
            {
                s.PublicId,
                Node = db.StreamAssignments.Where(a => a.StationId == s.Id).Select(a => a.Node).FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (station is null)
        {
            return TypedResults.NotFound();
        }
        var sample = store.Latest(station.Node ?? streaming.Value.Node);
        // Three missed samples: the node or the collector is down, so live/offline is not known.
        var stale = sample is null || !sample.IcecastUp || time.GetUtcNow() - sample.At > 3 * options.Value.Interval;
        var outputs = sample is null || stale ? [] : sample.Mounts
            .Where(m => m.PublicId == station.PublicId)
            .OrderBy(m => m.Format)
            .Select(m => new LiveOutputResponse(m.Format, m.Listeners, m.BitrateKbps > 0 ? m.BitrateKbps : null, m.StartedAt))
            .ToList();
        return TypedResults.Ok(new StationStatusResponse(stale ? null : outputs.Count > 0, outputs.Sum(o => o.Listeners), outputs,
            sample?.At, stale));
    }

    private static async Task<Results<Ok<StationStatsResponse>, NotFound, ValidationProblem>> GetStatsAsync(Guid id, DateTimeOffset? from,
        DateTimeOffset? to, RollupInterval? interval, AppDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var period = interval ?? RollupInterval.Day;
        var end = (to ?? time.GetUtcNow()).ToUniversalTime();
        var start = (from ?? end - (period == RollupInterval.Day ? TimeSpan.FromDays(30) : TimeSpan.FromHours(24))).ToUniversalTime();
        if (start >= end || end - start > MaxRange[period])
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["from"] = [$"from must be before to, at most {MaxRange[period].TotalDays:0} days apart for {period}."],
            });
        }
        if (!await db.Stations.AnyAsync(s => s.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }
        var points = await db.StationStatsRollups
            .Where(r => r.StationId == id && r.Interval == period && r.PeriodStart >= start && r.PeriodStart < end)
            .OrderBy(r => r.PeriodStart)
            .Select(r => new StatsPointResponse(r.PeriodStart, r.PeakListeners, r.ListenerHours, r.EgressBytes))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new StationStatsResponse(period, start, end, points.Count == 0 ? 0 : points.Max(p => p.PeakListeners),
            points.Sum(p => p.ListenerHours), points.Sum(p => p.EgressBytes), points));
    }

    private static Results<Ok<NodeTrafficResponse>, NotFound> GetNodeTraffic(string node, LiveStatusStore store)
        => store.Latest(node) is { EgressBytes: { } egress, IncludedBytes: { } included } sample && included > 0
            ? TypedResults.Ok(new NodeTrafficResponse(node, egress, included, Math.Round(100.0 * egress / included, 2), sample.At))
            : TypedResults.NotFound();
}
