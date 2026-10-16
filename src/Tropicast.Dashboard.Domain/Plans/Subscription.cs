namespace Tropicast.Dashboard.Domain.Plans;

public enum SubscriptionStatus
{
    /// <summary>Checkout not finished, or the first payment needs action: no paid plan yet.</summary>
    Incomplete,
    Trialing,
    Active,
    /// <summary>A renewal payment failed: the plan stays during the grace period, then the tenant falls back to Free.</summary>
    PastDue,
    /// <summary>Ended (canceled, or never paid): the tenant is on Free.</summary>
    Canceled,
}

/// <summary>
/// A tenant's paid subscription with the payment provider (one per tenant). The provider is the source of truth;
/// this is the latest state its webhooks reported, applied in order (<see cref="LastEventAt"/>).
/// </summary>
public sealed class Subscription : ITenantOwned
{
    private Subscription()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    /// <summary>The paid plan subscribed to (not necessarily the tenant's current plan, see <see cref="EffectivePlanId"/>).</summary>
    public string PlanId { get; private set; } = null!;
    public SubscriptionStatus Status { get; private set; }
    public DateTimeOffset? CurrentPeriodEnd { get; private set; }
    /// <summary>The subscription ends at <see cref="CurrentPeriodEnd"/> (canceled or downgraded to Free by the customer).</summary>
    public bool CancelAtPeriodEnd { get; private set; }
    public DateTimeOffset? TrialEnd { get; private set; }
    /// <summary>When the subscription became past due; the grace period runs from here.</summary>
    public DateTimeOffset? PastDueSince { get; private set; }
    /// <summary>A trial was already given to this tenant: none at the next checkout.</summary>
    public bool TrialUsed { get; private set; }
    public string? ProviderCustomerId { get; private set; }
    public string? ProviderSubscriptionId { get; private set; }
    /// <summary>Creation time of the last provider event applied; older events arriving late are ignored.</summary>
    public DateTimeOffset? LastEventAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Stripe may still charge for a subscription in this status (<c>Incomplete</c> too: a checkout or 3-D Secure step
    /// may still complete). Deleting an account or a workspace waits until it is <c>Canceled</c>.
    /// </summary>
    public static bool MayCharge(SubscriptionStatus status) => status != SubscriptionStatus.Canceled;

    public static Subscription Create(Guid tenantId, string planId, SubscriptionStatus status, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        TenantId = tenantId,
        PlanId = Text.Required(planId, 32, nameof(planId)),
        Status = status,
        CreatedAt = now,
    };

    /// <summary>Links the provider's customer (and subscription, when known) after checkout.</summary>
    public void Link(string customerId, string? subscriptionId)
    {
        ProviderCustomerId = Text.Required(customerId, 255, nameof(customerId));
        if (subscriptionId is not null)
        {
            ProviderSubscriptionId = Text.Required(subscriptionId, 255, nameof(subscriptionId));
        }
    }

    /// <summary>
    /// Applies the provider's view of the subscription. Returns false (and changes nothing) for an event older
    /// than the last one applied: webhooks can arrive out of order.
    /// </summary>
    public bool Apply(SubscriptionState state, DateTimeOffset eventAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (LastEventAt is { } last && eventAt < last)
        {
            return false;
        }
        PlanId = Text.Required(state.PlanId, 32, nameof(state.PlanId));
        if (state.Status == SubscriptionStatus.PastDue && Status != SubscriptionStatus.PastDue)
        {
            PastDueSince = eventAt;
        }
        else if (state.Status != SubscriptionStatus.PastDue)
        {
            PastDueSince = null;
        }
        Status = state.Status;
        CurrentPeriodEnd = state.CurrentPeriodEnd;
        CancelAtPeriodEnd = state.CancelAtPeriodEnd;
        TrialEnd = state.TrialEnd;
        TrialUsed |= state.Status == SubscriptionStatus.Trialing || state.TrialEnd is not null;
        Link(state.CustomerId, state.SubscriptionId);
        LastEventAt = eventAt;
        return true;
    }

    /// <summary>
    /// The plan the tenant should be on: the subscribed plan while trialing or active, and while past due within
    /// <paramref name="grace"/>; Free otherwise.
    /// </summary>
    public string EffectivePlanId(DateTimeOffset now, TimeSpan grace) => Status switch
    {
        SubscriptionStatus.Trialing or SubscriptionStatus.Active => PlanId,
        SubscriptionStatus.PastDue when PastDueSince is { } since && now < since + grace => PlanId,
        _ => Plan.Free.Id,
    };
}

/// <summary>What the payment provider says about a subscription.</summary>
/// <param name="PlanId">Plan of the subscribed price.</param>
/// <param name="Status">Its status.</param>
/// <param name="CurrentPeriodEnd">End of the paid period.</param>
/// <param name="CancelAtPeriodEnd">Ends at the period end.</param>
/// <param name="TrialEnd">End of the trial, if any.</param>
/// <param name="CustomerId">Provider customer.</param>
/// <param name="SubscriptionId">Provider subscription.</param>
public sealed record SubscriptionState(string PlanId, SubscriptionStatus Status, DateTimeOffset? CurrentPeriodEnd, bool CancelAtPeriodEnd,
    DateTimeOffset? TrialEnd, string CustomerId, string SubscriptionId);
