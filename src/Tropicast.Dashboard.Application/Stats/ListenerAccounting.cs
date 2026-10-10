namespace Tropicast.Dashboard.Application.Stats;

/// <summary>What one sample adds to a station's statistics.</summary>
/// <param name="PublicId">Station public ID.</param>
/// <param name="PeakListeners">Listeners on all the station's formats at this sample.</param>
/// <param name="ListenerSeconds">Listener-seconds since the previous sample.</param>
/// <param name="EgressBytes">Estimated node egress since the previous sample, overhead included.</param>
public sealed record StationIncrement(string PublicId, int PeakListeners, double ListenerSeconds, long EgressBytes);

/// <summary>
/// Turns consecutive node samples into listener-seconds and estimated egress per station. Egress is
/// <c>bitrate × listener-seconds × overhead</c> (tc-streaming docs/load-test.md: 1.10 at normal load); when the
/// bitrate is not known yet, the mount's sent-bytes delta × overhead.
/// </summary>
public static class ListenerAccounting
{
    /// <summary>Overhead of TCP/IP, TLS and HTTP framing over audio payload (tc-streaming#37).</summary>
    public const double DefaultOverheadFactor = 1.10;

    /// <param name="previous">The previous sample of the same node, or null.</param>
    /// <param name="current">This sample.</param>
    /// <param name="maxGap">Longer gaps (collector or node down) count no listener time: it is unknown.</param>
    /// <param name="overheadFactor">Egress per payload byte.</param>
    public static IReadOnlyList<StationIncrement> Increments(NodeSample? previous, NodeSample current, TimeSpan maxGap,
        double overheadFactor = DefaultOverheadFactor)
    {
        ArgumentNullException.ThrowIfNull(current);
        var seconds = previous is { IcecastUp: true } && current.At > previous.At && current.At - previous.At <= maxGap
            ? (current.At - previous.At).TotalSeconds
            : 0;
        var before = previous?.Mounts.ToDictionary(m => (m.PublicId, m.Format)) ?? [];
        return [.. current.Mounts.GroupBy(m => m.PublicId).Select(station =>
        {
            double listenerSeconds = 0, egress = 0;
            foreach (var mount in station)
            {
                var mountSeconds = mount.Listeners * seconds;
                listenerSeconds += mountSeconds;
                if (mount.BitrateKbps > 0)
                {
                    egress += mount.BitrateKbps * 1000 / 8 * mountSeconds;
                }
                else if (seconds > 0 && before.TryGetValue((mount.PublicId, mount.Format), out var last)
                    && last.StartedAt == mount.StartedAt && mount.SentBytes >= last.SentBytes)
                {
                    egress += mount.SentBytes - last.SentBytes;
                }
            }
            return new StationIncrement(station.Key, station.Sum(m => m.Listeners), listenerSeconds,
                (long)Math.Round(egress * overheadFactor));
        })];
    }

    /// <summary>Start of the 5-minute period containing <paramref name="at"/> (UTC).</summary>
    public static DateTimeOffset FiveMinutePeriod(DateTimeOffset at)
    {
        var utc = at.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute - (utc.Minute % 5), 0, TimeSpan.Zero);
    }

    /// <summary>Start of the UTC day containing <paramref name="at"/>.</summary>
    public static DateTimeOffset DayPeriod(DateTimeOffset at)
    {
        var utc = at.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
    }
}
