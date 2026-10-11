using Tropicast.Dashboard.Domain.Plans;

namespace Tropicast.Dashboard.Application.Billing;

/// <summary>A hosted checkout for a paid plan. Card data never reaches our servers.</summary>
/// <param name="TenantId">Tenant that subscribes.</param>
/// <param name="PlanId">Paid plan.</param>
/// <param name="Email">Billing email (pre-filled; the customer can change it).</param>
/// <param name="CustomerId">Existing provider customer, so a returning tenant keeps its payment methods.</param>
/// <param name="TrialDays">Free days before the first charge; 0 for none.</param>
/// <param name="SuccessUrl">Where the provider sends the customer back after paying.</param>
/// <param name="CancelUrl">Where it sends them back when they give up.</param>
public sealed record CheckoutRequest(Guid TenantId, string PlanId, string Email, string? CustomerId, int TrialDays, Uri SuccessUrl, Uri CancelUrl);

public enum BillingEventKind
{
    /// <summary>A checkout finished: links the tenant to the provider's customer and subscription.</summary>
    CheckoutCompleted,
    /// <summary>A subscription was created, changed or ended.</summary>
    SubscriptionChanged,
    /// <summary>Any other event: acknowledged, nothing to do.</summary>
    Ignored,
}

/// <summary>A verified webhook event, reduced to what billing needs.</summary>
/// <param name="Id">Provider event ID: processed once.</param>
/// <param name="CreatedAt">When the provider created it (orders events).</param>
/// <param name="Type">Provider event type, for logs.</param>
/// <param name="Kind">What it means here.</param>
/// <param name="TenantId">Tenant from the checkout or subscription metadata, when present.</param>
/// <param name="CustomerId">Provider customer.</param>
/// <param name="SubscriptionId">Provider subscription.</param>
/// <param name="State">The subscription's state, for <see cref="BillingEventKind.SubscriptionChanged"/>.</param>
public sealed record BillingEvent(string Id, DateTimeOffset CreatedAt, string Type, BillingEventKind Kind, Guid? TenantId,
    string? CustomerId, string? SubscriptionId, SubscriptionState? State);

/// <summary>The payment provider (Stripe; docs/adr/0002-payments-stripe.md).</summary>
public interface IPaymentProvider
{
    /// <summary>Whether this plan can be bought (it has a price).</summary>
    bool Sells(string planId);

    Task<Uri> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default);

    /// <summary>The provider's customer portal: payment methods, invoices, plan changes, cancellation.</summary>
    Task<Uri> CreatePortalAsync(string customerId, Uri returnUrl, CancellationToken cancellationToken = default);

    /// <summary>Verifies the signature and reads the event. Throws <see cref="BillingException"/> when it is not authentic.</summary>
    BillingEvent ParseWebhook(string payload, string signature);
}

/// <summary>A billing failure; the message is safe to show.</summary>
public sealed class BillingException : Exception
{
    public BillingException() : this("The payment provider call failed.") { }

    public BillingException(string message) : base(message) { }

    public BillingException(string message, Exception innerException) : base(message, innerException) { }
}
