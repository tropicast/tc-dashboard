namespace Tropicast.Dashboard.Domain.Plans;

public enum SubscriptionStatus
{
    Trialing,
    Active,
    PastDue,
    Canceled,
}

/// <summary>A tenant's billing subscription with the payment provider.</summary>
public sealed class Subscription : ITenantOwned
{
    private Subscription()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string PlanId { get; private set; } = null!;
    public SubscriptionStatus Status { get; private set; }
    public DateTimeOffset? CurrentPeriodEnd { get; private set; }
    public string? ProviderCustomerId { get; private set; }
    public string? ProviderSubscriptionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Subscription Create(Guid tenantId, string planId, SubscriptionStatus status, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        TenantId = tenantId,
        PlanId = Text.Required(planId, 32, nameof(planId)),
        Status = status,
        CreatedAt = now,
    };
}
