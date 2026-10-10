using System.Security.Cryptography;
using Tropicast.Dashboard.Application.Security;

namespace Tropicast.Dashboard.Infrastructure.Identity;

/// <summary>What a device learns when it polls a <see cref="DeviceAuthorization"/>.</summary>
public enum DeviceAuthorizationState
{
    /// <summary>Nobody has approved or denied it yet: poll again after the interval.</summary>
    Pending,
    /// <summary>Polled sooner than the interval: wait longer.</summary>
    SlowDown,
    /// <summary>A signed-in user approved it: sign the device in. Happens once; later polls see <see cref="Expired"/>.</summary>
    Approved,
    /// <summary>The user denied it.</summary>
    Denied,
    /// <summary>Expired, or already used: start again.</summary>
    Expired,
}

/// <summary>
/// A desktop sign-in request (OAuth 2.0 device authorization style, RFC 8628). The device shows the short
/// <see cref="UserCode"/>; a user signed in on the web enters it and approves; the device, polling with its secret
/// device code (<c>{id}.{secret}</c>, only the hash stored), then gets tokens once.
/// </summary>
public sealed class DeviceAuthorization
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    public const int UserCodeLength = 8;

    /// <summary>Consonants only: no vowels to spell words, no look-alike digits.</summary>
    private const string UserCodeAlphabet = "BCDFGHJKLMNPQRSTVWXZ";

    private DeviceAuthorization()
    {
    }

    public Guid Id { get; private set; }
    public string DeviceName { get; private set; } = null!;
    public string DeviceCodeHash { get; private set; } = null!;
    /// <summary>Eight letters, stored without the dash shown to people (<see cref="DisplayUserCode"/>).</summary>
    public string UserCode { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? LastPolledAt { get; private set; }
    /// <summary>The user who approved it.</summary>
    public Guid? UserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset? DeniedAt { get; private set; }
    /// <summary>When the device got its tokens; the request cannot be used again.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary><c>BCDF-GHJK</c>.</summary>
    public string DisplayUserCode => $"{UserCode[..4]}-{UserCode[4..]}";

    /// <summary>Starts a request and returns it with the device code to hand to the device (shown once).</summary>
    public static (DeviceAuthorization Request, string DeviceCode) Start(string deviceName, DateTimeOffset now)
    {
        var name = deviceName.Trim();
        var secret = Secrets.New();
        var request = new DeviceAuthorization
        {
            Id = Guid.CreateVersion7(now),
            DeviceName = name.Length is > 0 and <= 64 ? name : throw new ArgumentException("1-64 characters.", nameof(deviceName)),
            DeviceCodeHash = Secrets.Hash(secret),
            UserCode = RandomNumberGenerator.GetString(UserCodeAlphabet, UserCodeLength),
            CreatedAt = now,
            ExpiresAt = now + Lifetime,
        };
        return (request, $"{request.Id:N}.{secret}");
    }

    /// <summary>The user code as stored, from what a person typed (any case, dashes and spaces ignored); null if malformed.</summary>
    public static string? NormalizeUserCode(string? input)
    {
        var code = new string((input ?? "").Where(c => c is not ('-' or ' ')).Select(char.ToUpperInvariant).ToArray());
        return code.Length == UserCodeLength && code.All(UserCodeAlphabet.Contains) ? code : null;
    }

    /// <summary>The request a device code claims to belong to; it still has to match <see cref="Matches"/>.</summary>
    public static bool TryGetId(string? deviceCode, out Guid id)
    {
        id = Guid.Empty;
        var dot = deviceCode?.IndexOf('.', StringComparison.Ordinal) ?? -1;
        return dot > 0 && Guid.TryParseExact(deviceCode![..dot], "N", out id);
    }

    /// <summary>Whether this is the request's device code (constant-time).</summary>
    public bool Matches(string deviceCode)
    {
        ArgumentNullException.ThrowIfNull(deviceCode);
        return TryGetId(deviceCode, out var id) && id == Id
            && Secrets.Matches(deviceCode[(deviceCode.IndexOf('.', StringComparison.Ordinal) + 1)..], DeviceCodeHash);
    }

    /// <summary>Waiting for a user, and not expired.</summary>
    public bool IsPending(DateTimeOffset now) => UserId is null && DeniedAt is null && CompletedAt is null && now < ExpiresAt;

    public void Approve(Guid userId, DateTimeOffset now)
    {
        if (!IsPending(now))
        {
            throw new InvalidOperationException("Only a pending request can be approved.");
        }
        UserId = userId;
        ApprovedAt = now;
    }

    public void Deny(DateTimeOffset now)
    {
        if (!IsPending(now))
        {
            throw new InvalidOperationException("Only a pending request can be denied.");
        }
        DeniedAt = now;
    }

    /// <summary>One poll from the device. <see cref="DeviceAuthorizationState.Approved"/> completes the request.</summary>
    public DeviceAuthorizationState Poll(DateTimeOffset now)
    {
        if (CompletedAt is not null || now >= ExpiresAt)
        {
            return DeviceAuthorizationState.Expired;
        }
        if (DeniedAt is not null)
        {
            return DeviceAuthorizationState.Denied;
        }
        // One second of slack for timer and network jitter.
        if (LastPolledAt is { } last && now - last < PollInterval - TimeSpan.FromSeconds(1))
        {
            return DeviceAuthorizationState.SlowDown;
        }
        LastPolledAt = now;
        if (UserId is null)
        {
            return DeviceAuthorizationState.Pending;
        }
        CompletedAt = now;
        return DeviceAuthorizationState.Approved;
    }
}
