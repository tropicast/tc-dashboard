namespace Tropicast.Dashboard.Domain.Tenants;

/// <summary>
/// An invitation to join a tenant with a role, sent by email. Only the SHA-256 hash of the link token is stored.
/// </summary>
public sealed class Invitation : ITenantOwned
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private Invitation()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    /// <summary>Lowercase; the accepting account must have this confirmed email.</summary>
    public string Email { get; private set; } = null!;
    public MembershipRole Role { get; private set; }
    /// <summary>Lowercase hex SHA-256 of the token in the link.</summary>
    public string TokenHash { get; private set; } = null!;
    public Guid InvitedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }

    public static Invitation Create(Guid tenantId, string email, MembershipRole role, string tokenHash, Guid invitedBy, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        TenantId = tenantId,
        Email = Text.Required(email, 256, nameof(email)).ToLowerInvariant(),
        Role = role,
        TokenHash = Text.Sha256Hex(tokenHash, nameof(tokenHash)),
        InvitedBy = invitedBy,
        CreatedAt = now,
        ExpiresAt = now + Lifetime,
    };

    public bool IsOpen(DateTimeOffset now) => AcceptedAt is null && now < ExpiresAt;

    public bool IsFor(string email) => string.Equals(Email, email, StringComparison.OrdinalIgnoreCase);

    /// <summary>Marks the invitation used and returns the membership it grants.</summary>
    public Membership Accept(Guid userId, DateTimeOffset now)
    {
        if (!IsOpen(now))
        {
            throw new InvalidOperationException("The invitation was already used or has expired.");
        }
        AcceptedAt = now;
        return Membership.Create(TenantId, userId, Role, now);
    }
}
