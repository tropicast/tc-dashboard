using Microsoft.Extensions.Options;
using Stripe;
using Tropicast.Dashboard.Application.Billing;
using Tropicast.Dashboard.Domain.Plans;

namespace Tropicast.Dashboard.Infrastructure.Billing;

/// <summary>
/// Stripe Checkout (subscriptions), the Billing Portal, and verified webhooks. No card data reaches us: customers
/// pay on Stripe's pages. The tenant is carried as <c>client_reference_id</c> and as subscription metadata.
/// </summary>
internal sealed class StripePaymentProvider(IOptions<BillingOptions> options) : IPaymentProvider
{
    private const string TenantKey = "tenant_id";

    /// <summary>Webhook signatures older than this are refused (replay protection on top of idempotency).</summary>
    private const long ToleranceSeconds = 300;

    private StripeClient Client => options.Value.SecretKey is { Length: > 0 } key
        ? new StripeClient(key)
        : throw new BillingException("Billing is not configured.");

    /// <summary>Only plans with a non-blank price (an unset deploy variable gives an empty one).</summary>
    public bool Sells(string planId) => !string.IsNullOrWhiteSpace(options.Value.Prices.GetValueOrDefault(planId));

    public async Task<Uri> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var price = options.Value.Prices.GetValueOrDefault(request.PlanId) is { } configured && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : throw new BillingException($"The {request.PlanId} plan cannot be bought.");
        var tenant = request.TenantId.ToString();
        var create = new Stripe.Checkout.SessionCreateOptions
        {
            Mode = "subscription",
            ClientReferenceId = tenant,
            Customer = request.CustomerId,
            CustomerEmail = request.CustomerId is null ? request.Email : null,
            LineItems = [new Stripe.Checkout.SessionLineItemOptions { Price = price, Quantity = 1 }],
            SubscriptionData = new Stripe.Checkout.SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string> { [TenantKey] = tenant },
                TrialPeriodDays = request.TrialDays > 0 ? request.TrialDays : null,
            },
            Metadata = new Dictionary<string, string> { [TenantKey] = tenant },
            AllowPromotionCodes = true,
            SuccessUrl = request.SuccessUrl.AbsoluteUri,
            CancelUrl = request.CancelUrl.AbsoluteUri,
        };
        try
        {
            var session = await new Stripe.Checkout.SessionService(Client).CreateAsync(create, cancellationToken: cancellationToken);
            return new Uri(session.Url);
        }
        catch (StripeException ex)
        {
            throw new BillingException("The payment provider refused to start the checkout. Try again later.", ex);
        }
    }

    public async Task<Uri> CreatePortalAsync(string customerId, Uri returnUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(returnUrl);
        try
        {
            var session = await new Stripe.BillingPortal.SessionService(Client).CreateAsync(
                new Stripe.BillingPortal.SessionCreateOptions { Customer = customerId, ReturnUrl = returnUrl.AbsoluteUri },
                cancellationToken: cancellationToken);
            return new Uri(session.Url);
        }
        catch (StripeException ex)
        {
            throw new BillingException("The payment provider refused to open the billing portal. Try again later.", ex);
        }
    }

    public BillingEvent ParseWebhook(string payload, string signature)
    {
        var secret = options.Value.WebhookSecret is { Length: > 0 } configured
            ? configured
            : throw new BillingException("Billing webhooks are not configured.");
        Event stripeEvent;
        try
        {
            // The library pins an API version; events are read loosely, so a newer endpoint version still parses.
            stripeEvent = EventUtility.ConstructEvent(payload, signature, secret, ToleranceSeconds, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            throw new BillingException("The webhook signature is invalid or too old.", ex);
        }
        var created = new DateTimeOffset(DateTime.SpecifyKind(stripeEvent.Created, DateTimeKind.Utc));
        BillingEvent Ignored() => new(stripeEvent.Id, created, stripeEvent.Type, BillingEventKind.Ignored, null, null, null, null);
        switch (stripeEvent.Data.Object)
        {
            case Stripe.Checkout.Session { Mode: "subscription" } session when stripeEvent.Type == EventTypes.CheckoutSessionCompleted:
                return new BillingEvent(stripeEvent.Id, created, stripeEvent.Type, BillingEventKind.CheckoutCompleted,
                    Tenant(session.ClientReferenceId), session.CustomerId, session.SubscriptionId, null);
            case Stripe.Subscription subscription when stripeEvent.Type.StartsWith("customer.subscription.", StringComparison.Ordinal):
                var item = subscription.Items?.Data?.FirstOrDefault();
                var plan = item?.Price?.Id is { } priceId
                    ? options.Value.Prices.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Value) && p.Value == priceId).Key
                    : null;
                if (plan is null)
                {
                    return Ignored(); // A price this app does not sell.
                }
                var state = new SubscriptionState(plan,
                    stripeEvent.Type == EventTypes.CustomerSubscriptionDeleted ? SubscriptionStatus.Canceled : Status(subscription.Status),
                    Utc(item!.CurrentPeriodEnd), subscription.CancelAtPeriodEnd, subscription.TrialEnd is { } trialEnd ? Utc(trialEnd) : null,
                    subscription.CustomerId, subscription.Id);
                return new BillingEvent(stripeEvent.Id, created, stripeEvent.Type, BillingEventKind.SubscriptionChanged,
                    Tenant(subscription.Metadata?.GetValueOrDefault(TenantKey)), subscription.CustomerId, subscription.Id, state);
            default:
                return Ignored();
        }
    }

    /// <summary>Stripe statuses: an unpaid subscription keeps the grace that started when it became past due.</summary>
    internal static SubscriptionStatus Status(string status) => status switch
    {
        "trialing" => SubscriptionStatus.Trialing,
        "active" => SubscriptionStatus.Active,
        "past_due" or "unpaid" => SubscriptionStatus.PastDue,
        "incomplete" => SubscriptionStatus.Incomplete,
        _ => SubscriptionStatus.Canceled, // canceled, incomplete_expired, paused (trial ended without payment)
    };

    private static Guid? Tenant(string? value) => Guid.TryParse(value, out var id) ? id : null;

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
