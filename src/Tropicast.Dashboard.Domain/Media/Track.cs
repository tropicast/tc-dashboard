using Tropicast.Dashboard.Domain.Stations;

namespace Tropicast.Dashboard.Domain.Media;

/// <summary>The container and codec of an uploaded track, as read from the file (never from the client).</summary>
public enum TrackFormat
{
    Mp3,
    Flac,
    OggOpus,
    OggVorbis,
}

public enum TrackStatus
{
    /// <summary>An upload URL was handed out; the file is not confirmed yet. Counts toward the quota until it expires.</summary>
    Pending,
    /// <summary>Checked and playable.</summary>
    Ready,
}

/// <summary>
/// An audio file a station uploaded for AutoDJ (#103), stored in the private bucket under <see cref="ObjectKey"/>.
/// Created <see cref="TrackStatus.Pending"/> when the upload starts, then made <see cref="TrackStatus.Ready"/> with
/// what the server read from the stored object.
/// </summary>
public sealed class Track
{
    /// <summary>Longest title or artist kept.</summary>
    public const int MaxTextLength = 200;

    private Track()
    {
    }

    public Guid Id { get; private set; }
    public Guid StationId { get; private set; }
    public Station Station { get; private set; } = null!;
    /// <summary>Bucket key: <c>stations/{station id}/tracks/{track id}</c>.</summary>
    public string ObjectKey { get; private set; } = null!;
    /// <summary>The uploaded file's name, for display.</summary>
    public string FileName { get; private set; } = null!;
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public TrackStatus Status { get; private set; }
    public TrackFormat? Format { get; private set; }
    /// <summary>Playing time; null until confirmed.</summary>
    public long? DurationMs { get; private set; }
    /// <summary>Declared size while pending, then the stored object's size.</summary>
    public long SizeBytes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    /// <summary>A pending upload not confirmed by then is deleted with its object.</summary>
    public DateTimeOffset? UploadExpiresAt { get; private set; }

    public static Track StartUpload(Guid stationId, string fileName, long declaredSize, DateTimeOffset now, TimeSpan uploadLifetime)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(declaredSize);
        var id = Guid.CreateVersion7(now);
        return new Track
        {
            Id = id,
            StationId = stationId,
            ObjectKey = KeyFor(stationId, id),
            FileName = Text.Required(fileName, 255, nameof(fileName)),
            Status = TrackStatus.Pending,
            SizeBytes = declaredSize,
            CreatedAt = now,
            UploadExpiresAt = now + uploadLifetime,
        };
    }

    public static string KeyFor(Guid stationId, Guid trackId) => $"stations/{stationId:N}/tracks/{trackId:N}";

    /// <summary>The prefix of every object of a station, for cleanup.</summary>
    public static string StationPrefix(Guid stationId) => $"stations/{stationId:N}/";

    /// <summary>The stored file checked out: keep what the server read from it.</summary>
    public void Confirm(TrackFormat format, TimeSpan duration, long sizeBytes, string title, string artist)
    {
        if (Status != TrackStatus.Pending)
        {
            throw new InvalidOperationException("Only a pending upload can be confirmed.");
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        Format = format;
        DurationMs = (long)duration.TotalMilliseconds;
        SizeBytes = sizeBytes;
        Title = Clip(title);
        Artist = Clip(artist);
        Status = TrackStatus.Ready;
        UploadExpiresAt = null;
    }

    public void Describe(string? title, string? artist)
    {
        Title = title is null ? Title : Clip(title);
        Artist = artist is null ? Artist : Clip(artist);
    }

    private static string Clip(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= MaxTextLength ? trimmed : trimmed[..MaxTextLength];
    }
}
