# ADR 0002: Payments with Stripe Checkout and the Billing Portal

- Status: accepted
- Date: 2026-10-11
- Issue: #14

## Context

Paid plans (Starter, Growth, Pro) need monthly card payments, with no card data
on our servers. The options were Stripe (we are the seller) or Paddle (merchant
of record). Mobile money for Madagascar comes later.

## Decision

Use **Stripe**: hosted **Checkout** for subscribing and the hosted **Billing
Portal** for everything after (payment methods, invoices, plan changes,
cancellation). The rest of the code depends only on the `IPaymentProvider` port
(`Application/Billing`), so a mobile-money provider can be added next to it.

- **Plans ↔ prices**: one recurring monthly Stripe price per paid plan
  (`Billing:Prices:{plan}`). Stripe holds the amounts; the app only shows
  `Billing:DisplayPrices`.
- **Source of truth**: Stripe's subscription. Webhooks
  (`checkout.session.completed`, `customer.subscription.created|updated|deleted`)
  update our `Subscription`, and the tenant's plan follows from it:
  - trialing or active: the subscribed plan;
  - past due: the plan for a 7-day grace period, then Free;
  - canceled, unpaid at the end of grace, or incomplete: Free.
- **Webhooks**: the signature is verified with Stripe.net (5-minute tolerance).
  Each event ID is recorded in the same transaction as its effect, so a replay
  changes nothing. Events older than the last one applied are ignored, because
  Stripe does not guarantee order. Failures return 500 and Stripe retries.
- **Trial**: 14 days, once per tenant (`TrialUsed`).
- **Plan changes**: in the portal. Downgrades and cancellations take effect at
  the period end (portal configuration). A plan change raises
  `TenantPlanChanged`: provisioning updates the node's caps, and the audit log
  records it. After a downgrade, stations beyond the plan's count (newest
  first) are refused at source auth until one is deleted or the plan upgraded.
- **Taxes**: Stripe Tax can be switched on in Stripe without code changes. As
  the seller, we are responsible for VAT; Paddle would have taken that on at a
  higher fee. We revisit this if EU B2C revenue grows.

## Consequences

- A Stripe account, products and prices, the portal configuration and a
  webhook endpoint are set up by hand (docs/runbook.md, Billing).
- Usage (peak listeners, listener-hours, egress) is reported per tenant and
  month (`/api/v1/billing/usage`, operator `/api/v1/operator/usage`) for fair
  use; it is not billed automatically.
