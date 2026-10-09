namespace Tropicast.Dashboard.Domain.Tenants;

public enum TenantStatus
{
    Active,
    Suspended,
}

/// <summary>A customer account: owns stations, members and a subscription.</summary>
public sealed class Tenant
{
    private Tenant()
    {
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string PlanId { get; private set; } = null!;
    public TenantStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Tenant Create(string name, string slug, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Name = Text.Required(name, 100, nameof(name)),
        Slug = Text.Slug(slug, nameof(slug)),
        PlanId = Plans.Plan.Free.Id,
        Status = TenantStatus.Active,
        CreatedAt = now,
    };

    public void Rename(string name, string slug)
    {
        Name = Text.Required(name, 100, nameof(name));
        Slug = Text.Slug(slug, nameof(slug));
    }

    public void ChangePlan(string planId) => PlanId = Text.Required(planId, 32, nameof(planId));
}
