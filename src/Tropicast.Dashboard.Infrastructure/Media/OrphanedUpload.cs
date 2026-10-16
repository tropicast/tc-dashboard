namespace Tropicast.Dashboard.Infrastructure.Media;

/// <summary>
/// An upload still pending when its station's rows were deleted (workspace deletion, #125). Its presigned URL may still
/// PUT a file after the station prefix was cleared: <see cref="PendingUploadCleanup"/> deletes the object once the URL
/// expired, then this row.
/// </summary>
public sealed class OrphanedUpload
{
    private OrphanedUpload()
    {
    }

    public string ObjectKey { get; private set; } = null!;
    /// <summary>When the upload URL can no longer write: the object is final from then on.</summary>
    public DateTimeOffset DeleteAfter { get; private set; }

    public static OrphanedUpload Of(string objectKey, DateTimeOffset deleteAfter) => new() { ObjectKey = objectKey, DeleteAfter = deleteAfter };
}
