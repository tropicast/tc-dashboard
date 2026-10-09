namespace Tropicast.Dashboard.Domain.Stations;

public enum RollupInterval
{
    FiveMinutes,
    Day,
}

/// <summary>Listener statistics of one station for one period (5-minute or daily).</summary>
public sealed class StationStatsRollup
{
    private StationStatsRollup()
    {
    }

    public Guid StationId { get; private set; }
    public Station Station { get; private set; } = null!;
    public RollupInterval Interval { get; private set; }
    public DateTimeOffset PeriodStart { get; private set; }
    public int PeakListeners { get; private set; }
    public double ListenerHours { get; private set; }
    public long EgressBytes { get; private set; }

    public static StationStatsRollup Create(Guid stationId, RollupInterval interval, DateTimeOffset periodStart,
        int peakListeners, double listenerHours, long egressBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(peakListeners);
        ArgumentOutOfRangeException.ThrowIfNegative(listenerHours);
        ArgumentOutOfRangeException.ThrowIfNegative(egressBytes);
        return new()
        {
            StationId = stationId,
            Interval = interval,
            PeriodStart = periodStart,
            PeakListeners = peakListeners,
            ListenerHours = listenerHours,
            EgressBytes = egressBytes,
        };
    }
}
