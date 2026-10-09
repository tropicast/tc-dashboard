namespace Tropicast.Dashboard.Domain.Stations;

/// <summary>One broadcast of a station in one format, from source connect to disconnect.</summary>
public sealed class LiveSession
{
    private LiveSession()
    {
    }

    public Guid Id { get; private set; }
    public Guid StationId { get; private set; }
    public Station Station { get; private set; } = null!;
    public AudioFormat Format { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }

    public static LiveSession Start(Guid stationId, AudioFormat format, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        StationId = stationId,
        Format = format,
        StartedAt = now,
    };

    public void End(DateTimeOffset now) => EndedAt ??= now;
}
