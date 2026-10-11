using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Application.Billing;
using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Infrastructure.Billing;

namespace Tropicast.Dashboard.Infrastructure.Tests;

/// <summary>Webhooks as Stripe signs them (<c>Stripe-Signature: t=…,v1=HMAC-SHA256("{t}.{payload}")</c>).</summary>
public sealed class StripePaymentProviderTests
{
    private const string Secret = "whsec_test_secret_for_unit_tests";
    private static readonly Guid Tenant = Guid.NewGuid();

    private static StripePaymentProvider Provider() => new(Options.Create(new BillingOptions
    {
        SecretKey = "sk_test_unused",
        WebhookSecret = Secret,
        Prices = new() { ["starter"] = "price_starter", ["growth"] = "price_growth" },
    }));

    private static string Sign(string payload, long? at = null)
    {
        var timestamp = at ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hmac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return $"t={timestamp},v1={Convert.ToHexStringLower(hmac)}";
    }

    private static string SubscriptionEvent(string type, string status, string price, long periodEnd, string? trialEnd = "null") => """
        {"id":"evt_1","object":"event","api_version":"2025-09-30.clover","created":1791680000,"type":"__type__",
         "livemode":false,"pending_webhooks":1,"request":{"id":null,"idempotency_key":null},
         "data":{"object":{"id":"sub_123","object":"subscription","customer":"cus_123","status":"__status__",
           "cancel_at_period_end":false,"trial_end":__trialEnd__,"metadata":{"tenant_id":"__Tenant__"},
           "items":{"object":"list","data":[{"id":"si_1","object":"subscription_item","current_period_end":__periodEnd__,
             "price":{"id":"__price__","object":"price"}}]}}}}
        """.Replace("__type__", type, StringComparison.Ordinal).Replace("__status__", status, StringComparison.Ordinal).Replace("__trialEnd__", trialEnd ?? "null", StringComparison.Ordinal).Replace("__Tenant__", Tenant.ToString(), StringComparison.Ordinal).Replace("__periodEnd__", periodEnd.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("__price__", price, StringComparison.Ordinal);

    [Fact]
    public void A_signed_subscription_event_gives_the_plan_and_state()
    {
        var payload = SubscriptionEvent("customer.subscription.updated", "active", "price_starter", 1794358400);

        var billingEvent = Provider().ParseWebhook(payload, Sign(payload));

        Assert.Equal(("evt_1", BillingEventKind.SubscriptionChanged, Tenant), (billingEvent.Id, billingEvent.Kind, billingEvent.TenantId));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1791680000), billingEvent.CreatedAt);
        var state = billingEvent.State!;
        Assert.Equal(("starter", SubscriptionStatus.Active, "cus_123", "sub_123"), (state.PlanId, state.Status, state.CustomerId, state.SubscriptionId));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1794358400), state.CurrentPeriodEnd);
    }

    [Fact]
    public void A_deleted_subscription_is_canceled_and_a_trial_is_read()
    {
        var deleted = SubscriptionEvent("customer.subscription.deleted", "active", "price_growth", 1794358400);
        Assert.Equal(SubscriptionStatus.Canceled, Provider().ParseWebhook(deleted, Sign(deleted)).State!.Status);

        var trial = SubscriptionEvent("customer.subscription.created", "trialing", "price_growth", 1794358400, "1792889600");
        var state = Provider().ParseWebhook(trial, Sign(trial)).State!;
        Assert.Equal((SubscriptionStatus.Trialing, DateTimeOffset.FromUnixTimeSeconds(1792889600)), (state.Status, state.TrialEnd));
    }

    [Fact]
    public void Checkout_completion_links_the_tenant()
    {
        var payload = """
            {"id":"evt_2","object":"event","api_version":"2025-09-30.clover","created":1791680000,"type":"checkout.session.completed",
             "data":{"object":{"id":"cs_1","object":"checkout.session","mode":"subscription","client_reference_id":"__Tenant__",
               "customer":"cus_9","subscription":"sub_9"}}}
            """.Replace("__Tenant__", Tenant.ToString(), StringComparison.Ordinal);
        var billingEvent = Provider().ParseWebhook(payload, Sign(payload));
        Assert.Equal((BillingEventKind.CheckoutCompleted, Tenant, "cus_9", "sub_9"),
            (billingEvent.Kind, billingEvent.TenantId, billingEvent.CustomerId, billingEvent.SubscriptionId));
    }

    [Fact]
    public void Forged_stale_or_unknown_events_are_refused_or_ignored()
    {
        var payload = SubscriptionEvent("customer.subscription.updated", "active", "price_starter", 1794358400);
        Assert.Throws<BillingException>(() => Provider().ParseWebhook(payload, Sign(payload + " ")));
        Assert.Throws<BillingException>(() => Provider().ParseWebhook(payload, Sign(payload, DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds())));
        Assert.Throws<BillingException>(() => Provider().ParseWebhook(payload, "t=1,v1=00"));

        var otherPrice = SubscriptionEvent("customer.subscription.updated", "active", "price_someone_else", 1794358400);
        Assert.Equal(BillingEventKind.Ignored, Provider().ParseWebhook(otherPrice, Sign(otherPrice)).Kind);
    }

    [Theory]
    [InlineData("trialing", SubscriptionStatus.Trialing)]
    [InlineData("active", SubscriptionStatus.Active)]
    [InlineData("past_due", SubscriptionStatus.PastDue)]
    [InlineData("unpaid", SubscriptionStatus.PastDue)]
    [InlineData("incomplete", SubscriptionStatus.Incomplete)]
    [InlineData("incomplete_expired", SubscriptionStatus.Canceled)]
    [InlineData("paused", SubscriptionStatus.Canceled)]
    [InlineData("canceled", SubscriptionStatus.Canceled)]
    public void Stripe_statuses_map(string stripe, SubscriptionStatus status) => Assert.Equal(status, StripePaymentProvider.Status(stripe));
}
