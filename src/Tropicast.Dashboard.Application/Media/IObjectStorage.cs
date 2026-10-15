namespace Tropicast.Dashboard.Application.Media;

/// <summary>A stored object's size and content type.</summary>
public sealed record StoredObject(long Size, string? ContentType);

/// <summary>
/// The private bucket for uploaded audio (#104). Browsers and the playout worker never get bucket credentials,
/// only presigned URLs that expire.
/// </summary>
public interface IObjectStorage
{
    /// <summary>Whether a bucket is configured. Without one, uploads are refused (503).</summary>
    bool IsConfigured { get; }

    /// <summary>A URL the browser PUTs the file to, with <paramref name="contentType"/> as its Content-Type.</summary>
    Uri PresignUpload(string key, string contentType, TimeSpan lifetime);

    /// <summary>A URL that GETs the object (the playout worker reads tracks this way).</summary>
    Uri PresignDownload(string key, TimeSpan lifetime);

    /// <summary>The object's size and type; null when there is none.</summary>
    Task<StoredObject?> HeadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Up to <paramref name="length"/> bytes from <paramref name="offset"/>.</summary>
    Task<byte[]> ReadRangeAsync(string key, long offset, int length, CancellationToken cancellationToken = default);

    /// <summary>Idempotent: a missing object is not an error.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Deletes every object whose key starts with <paramref name="prefix"/>; returns how many.</summary>
    Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default);
}
