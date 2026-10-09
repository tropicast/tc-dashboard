namespace Tropicast.Dashboard.Domain.Stations;

/// <summary>
/// A station's source password for one device (e.g. "Studio PC"). Only the SHA-256 hash is stored;
/// revoking one device leaves the others working.
/// </summary>
public sealed class BroadcastCredential
{
    private BroadcastCredential()
    {
    }

    public Guid Id { get; private set; }
    public Guid StationId { get; private set; }
    public Station Station { get; private set; } = null!;
    public string DeviceLabel { get; private set; } = null!;
    /// <summary>Lowercase hex SHA-256 of the secret.</summary>
    public string SecretHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    public static BroadcastCredential Create(Guid stationId, string deviceLabel, string secretHash, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        StationId = stationId,
        DeviceLabel = Text.Required(deviceLabel, 64, nameof(deviceLabel)),
        SecretHash = Text.Sha256Hex(secretHash, nameof(secretHash)),
        CreatedAt = now,
    };

    public void RecordUse(DateTimeOffset now) => LastUsedAt = now;

    /// <summary>Idempotent; takes effect on the device's next connect.</summary>
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
