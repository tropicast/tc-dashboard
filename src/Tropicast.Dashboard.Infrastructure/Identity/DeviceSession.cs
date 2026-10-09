using Tropicast.Dashboard.Application.Security;

namespace Tropicast.Dashboard.Infrastructure.Identity;

/// <summary>
/// A signed-in desktop device. Holds the hash of its current refresh token; each refresh rotates it, and presenting
/// the previous token again (a stolen copy) revokes the session.
/// </summary>
public sealed class DeviceSession
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(60);

    private DeviceSession()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string DeviceName { get; private set; } = null!;
    public string RefreshTokenHash { get; private set; } = null!;
    public string? PreviousRefreshTokenHash { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastUsedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    /// <summary>Starts a session and returns it with the refresh token to hand to the device (shown once).</summary>
    public static (DeviceSession Session, string RefreshToken) Start(Guid userId, string deviceName, DateTimeOffset now)
    {
        var token = Secrets.New();
        var name = deviceName.Trim();
        return (new DeviceSession
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            DeviceName = name.Length is > 0 and <= 64 ? name : throw new ArgumentException("1-64 characters.", nameof(deviceName)),
            RefreshTokenHash = Secrets.Hash(token),
            CreatedAt = now,
            LastUsedAt = now,
            ExpiresAt = now + Lifetime,
        }, token);
    }

    /// <summary>Replaces the refresh token; the old one stops working. Sliding expiry.</summary>
    public string Rotate(DateTimeOffset now)
    {
        var token = Secrets.New();
        PreviousRefreshTokenHash = RefreshTokenHash;
        RefreshTokenHash = Secrets.Hash(token);
        LastUsedAt = now;
        ExpiresAt = now + Lifetime;
        return token;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
