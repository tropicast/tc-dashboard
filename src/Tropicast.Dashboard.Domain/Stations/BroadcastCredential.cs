namespace Tropicast.Dashboard.Domain.Stations;

/// <summary>What a credential may publish to.</summary>
public enum CredentialPurpose
{
    /// <summary>A DJ's device: the live mounts <c>live.(mp3|opus)</c> only.</summary>
    Broadcast,
    /// <summary>The system's AutoDJ playout: the station's <c>autodj.(mp3|opus)</c> mounts only (#106).</summary>
    AutoDj,
}

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
    public CredentialPurpose Purpose { get; private set; }
    /// <summary>
    /// AutoDJ only: the secret encrypted with the API's Data Protection keys, so the playout worker can be given it
    /// (#107). Device secrets are never kept, only their hash.
    /// </summary>
    public string? ProtectedSecret { get; private set; }

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

    /// <summary>The station's AutoDJ credential, issued by the system rather than a person.</summary>
    public static BroadcastCredential CreateForAutoDj(Guid stationId, string secretHash, string protectedSecret, DateTimeOffset now)
    {
        var credential = Create(stationId, AutoDjLabel, secretHash, now);
        credential.Purpose = CredentialPurpose.AutoDj;
        credential.ProtectedSecret = Text.Required(protectedSecret, 1024, nameof(protectedSecret));
        return credential;
    }

    public const string AutoDjLabel = "AutoDJ";

    public void RecordUse(DateTimeOffset now) => LastUsedAt = now;

    /// <summary>Idempotent; takes effect on the device's next connect.</summary>
    public void Revoke(DateTimeOffset now)
    {
        RevokedAt ??= now;
        // A revoked AutoDJ secret is of no use to anyone: do not keep it.
        ProtectedSecret = null;
    }
}
