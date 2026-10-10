namespace Tropicast.Dashboard.Domain.Tenants;

public enum TenantStatus
{
    Active,
    Suspended,
}

/// <summary>A tenant's plan changed: provisioning applies the new limits to its stations (#8).</summary>
public sealed record TenantPlanChanged(Guid TenantId, string PlanId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>A customer account: owns stations, members and a subscription.</summary>
public sealed class Tenant : IHasDomainEvents
{
    private readonly List<IDomainEvent> _events = [];

    private Tenant()
    {
    }

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;
    public void ClearDomainEvents() => _events.Clear();

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

    public void ChangePlan(string planId, DateTimeOffset now)
    {
        var plan = Text.Required(planId, 32, nameof(planId));
        if (plan != PlanId)
        {
            PlanId = plan;
            _events.Add(new TenantPlanChanged(Id, plan, now));
        }
    }
}
