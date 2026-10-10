namespace Tropicast.Dashboard.Domain.Stations;

/// <summary>
/// A station's source password for one device (e.g. "Studio PC"). Only the SHA-256 hash is stored;
/// revoking one device leaves the others working. Credentials the desktop app gets for itself are bound to its
/// signed-in device session (<see cref="DeviceSessionId"/>) and end with it.
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
    /// <summary>The desktop device session that requested it; null for credentials issued by an admin.</summary>
    public Guid? DeviceSessionId { get; private set; }
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

    /// <summary>A credential the desktop app requested for itself, labelled with its device name.</summary>
    public static BroadcastCredential CreateForDevice(Guid stationId, Guid deviceSessionId, string deviceName, string secretHash,
        DateTimeOffset now)
    {
        var credential = Create(stationId, deviceName, secretHash, now);
        credential.DeviceSessionId = deviceSessionId;
        return credential;
    }

    public void RecordUse(DateTimeOffset now) => LastUsedAt = now;

    /// <summary>Idempotent; takes effect on the device's next connect.</summary>
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
