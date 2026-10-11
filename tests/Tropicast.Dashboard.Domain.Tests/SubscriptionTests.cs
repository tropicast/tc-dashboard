using Tropicast.Dashboard.Domain.Plans;

namespace Tropicast.Dashboard.Domain.Tests;

public sealed class SubscriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Grace = TimeSpan.FromDays(7);

    private static SubscriptionState State(string plan, SubscriptionStatus status, DateTimeOffset? trialEnd = null)
        => new(plan, status, Now.AddMonths(1), false, trialEnd, "cus_1", "sub_1");

    [Fact]
    public void Trialing_and_active_subscriptions_give_their_plan()
    {
        var subscription = Subscription.Create(Guid.NewGuid(), Plan.Free.Id, SubscriptionStatus.Incomplete, Now);
        Assert.Equal(Plan.Free.Id, subscription.EffectivePlanId(Now, Grace));

        Assert.True(subscription.Apply(State("starter", SubscriptionStatus.Trialing, Now.AddDays(14)), Now));
        Assert.Equal("starter", subscription.EffectivePlanId(Now, Grace));
        Assert.True(subscription.TrialUsed);
        Assert.Equal(("cus_1", "sub_1"), (subscription.ProviderCustomerId, subscription.ProviderSubscriptionId));

        Assert.True(subscription.Apply(State("growth", SubscriptionStatus.Active), Now.AddDays(15)));
        Assert.Equal("growth", subscription.EffectivePlanId(Now.AddDays(15), Grace));
    }

    [Fact]
    public void A_failed_payment_keeps_the_plan_during_the_grace_period_then_free()
    {
        var subscription = Subscription.Create(Guid.NewGuid(), Plan.Free.Id, SubscriptionStatus.Incomplete, Now);
        subscription.Apply(State("starter", SubscriptionStatus.Active), Now);
        subscription.Apply(State("starter", SubscriptionStatus.PastDue), Now.AddDays(30));
        // Another past-due event later does not restart the grace period.
        subscription.Apply(State("starter", SubscriptionStatus.PastDue), Now.AddDays(33));

        Assert.Equal(Now.AddDays(30), subscription.PastDueSince);
        Assert.Equal("starter", subscription.EffectivePlanId(Now.AddDays(36), Grace));
        Assert.Equal(Plan.Free.Id, subscription.EffectivePlanId(Now.AddDays(37), Grace));

        subscription.Apply(State("starter", SubscriptionStatus.Active), Now.AddDays(38));
        Assert.Null(subscription.PastDueSince);
        Assert.Equal("starter", subscription.EffectivePlanId(Now.AddDays(38), Grace));
    }

    [Fact]
    public void Canceled_is_free_and_late_events_are_ignored()
    {
        var subscription = Subscription.Create(Guid.NewGuid(), Plan.Free.Id, SubscriptionStatus.Incomplete, Now);
        subscription.Apply(State("pro", SubscriptionStatus.Canceled), Now.AddDays(2));
        Assert.Equal(Plan.Free.Id, subscription.EffectivePlanId(Now.AddDays(2), Grace));

        // An "active" event created before the cancellation, delivered after it.
        Assert.False(subscription.Apply(State("pro", SubscriptionStatus.Active), Now.AddDays(1)));
        Assert.Equal(SubscriptionStatus.Canceled, subscription.Status);
    }
}
