namespace Tropicast.Dashboard.Domain.Tenants;

public enum MembershipRole
{
    Owner,
    Admin,
    Broadcaster,
}

/// <summary>A user's role in a tenant. Users are managed by the identity system and referenced by ID.</summary>
public sealed class Membership : ITenantOwned
{
    private Membership()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public MembershipRole Role { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Membership Create(Guid tenantId, Guid userId, MembershipRole role, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        TenantId = tenantId,
        UserId = userId,
        Role = role,
        CreatedAt = now,
    };

    public void ChangeRole(MembershipRole role) => Role = role;
}
