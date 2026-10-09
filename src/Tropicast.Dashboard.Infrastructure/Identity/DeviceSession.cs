using Tropicast.Dashboard.Application.Security;

namespace Tropicast.Dashboard.Infrastructure.Identity;

/// <summary>
/// A signed-in desktop device. Refresh tokens are <c>{session id}.{secret}</c>; only the hash of the current secret is
/// stored. Each refresh rotates the secret, and any other token of the session (one rotated out, however long ago,
/// i.e. a copy) revokes the session.
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
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastUsedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    /// <summary>Starts a session and returns it with the refresh token to hand to the device (shown once).</summary>
    public static (DeviceSession Session, string RefreshToken) Start(Guid userId, string deviceName, DateTimeOffset now)
    {
        var name = deviceName.Trim();
        var session = new DeviceSession
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            DeviceName = name.Length is > 0 and <= 64 ? name : throw new ArgumentException("1-64 characters.", nameof(deviceName)),
            CreatedAt = now,
        };
        return (session, session.Rotate(now));
    }

    /// <summary>The session a refresh token claims to belong to; it still has to match <see cref="IsCurrent"/>.</summary>
    public static bool TryGetSessionId(string? refreshToken, out Guid sessionId)
    {
        sessionId = Guid.Empty;
        var dot = refreshToken?.IndexOf('.', StringComparison.Ordinal) ?? -1;
        return dot > 0 && Guid.TryParseExact(refreshToken![..dot], "N", out sessionId);
    }

    /// <summary>Whether this is the session's current refresh token (constant-time).</summary>
    public bool IsCurrent(string refreshToken)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);
        return TryGetSessionId(refreshToken, out var id) && id == Id
            && Secrets.Matches(refreshToken[(refreshToken.IndexOf('.', StringComparison.Ordinal) + 1)..], RefreshTokenHash);
    }

    /// <summary>Replaces the refresh token; every earlier one stops working. Sliding expiry.</summary>
    public string Rotate(DateTimeOffset now)
    {
        var secret = Secrets.New();
        RefreshTokenHash = Secrets.Hash(secret);
        LastUsedAt = now;
        ExpiresAt = now + Lifetime;
        return $"{Id:N}.{secret}";
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
