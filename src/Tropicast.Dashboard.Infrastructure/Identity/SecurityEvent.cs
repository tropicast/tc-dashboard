namespace Tropicast.Dashboard.Infrastructure.Identity;

/// <summary>
/// An account security event (sign-in, failed sign-in, password change, device sign-in or sign-out), shown to the
/// account owner and kept <see cref="Retention"/>. Never holds secrets.
/// </summary>
public sealed class SecurityEvent
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    private SecurityEvent()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    /// <summary>Dotted action name, e.g. <c>signin.succeeded</c>.</summary>
    public string Action { get; private set; } = null!;
    /// <summary>Client address as the API saw it (behind the gateway: the forwarded one).</summary>
    public string? IpAddress { get; private set; }
    /// <summary>Short context, e.g. the device name.</summary>
    public string? Detail { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    public static SecurityEvent Record(Guid userId, string action, string? ipAddress, string? detail, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        UserId = userId,
        Action = action.Length <= 64 ? action : throw new ArgumentException("At most 64 characters.", nameof(action)),
        IpAddress = ipAddress is { Length: > 0 and <= 45 } ? ipAddress : null,
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Length <= 128 ? detail : detail[..128],
        OccurredAt = now,
    };
}
