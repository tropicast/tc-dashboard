namespace Tropicast.Dashboard.Domain.Stations;

/// <summary>Where a station streams: the Icecast node and its mount base. The MVP has one node.</summary>
public sealed class StreamAssignment
{
    private StreamAssignment()
    {
    }

    public Guid Id { get; private set; }
    public Guid StationId { get; private set; }
    public Station Station { get; private set; } = null!;
    /// <summary>Node name, e.g. <c>tc-stream-1</c>.</summary>
    public string Node { get; private set; } = null!;
    /// <summary><c>/stations/{public-id}</c>; mounts add <c>/live.mp3</c> or <c>/live.opus</c>.</summary>
    public string MountBase { get; private set; } = null!;
    public DateTimeOffset AssignedAt { get; private set; }

    public static StreamAssignment Create(Station station, string node, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(station);
        return new()
        {
            Id = Guid.CreateVersion7(now),
            StationId = station.Id,
            Node = Text.Required(node, 64, nameof(node)),
            MountBase = $"/stations/{station.PublicId}",
            AssignedAt = now,
        };
    }

    public string Mount(AudioFormat format) => $"{MountBase}/live.{(format == AudioFormat.Mp3 ? "mp3" : "opus")}";
}
