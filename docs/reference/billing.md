# Billing

Stripe Checkout and the Billing Portal ([ADR 0002](../adr/0002-payments-stripe.md)),
behind the `IPaymentProvider` port; no card data reaches the API.

- `GET /api/v1/billing` gives the plan, subscription and plans on offer.
  `POST /checkout` and `/portal` (Owner) return Stripe URLs.
  `GET /usage?month=` (Admin) gives the month's peak listeners,
  listener-hours and egress per station. Operators read
  `GET /api/v1/operator/usage`.
- `POST /api/v1/billing/webhook`: the Stripe signature is verified, and each
  event is applied once (event IDs are recorded) and in order (late events are
  ignored). The plan follows the subscription: trial and active give the paid
  plan, past due keeps it for 7 days, and anything else is Free.
- A plan change raises `TenantPlanChanged`: provisioning applies the new
  caps, and the audit log records it. After a downgrade, stations beyond the
  plan's count (newest first) are refused at source auth.
- Configuration: the `Billing` section (`SecretKey`, `WebhookSecret`,
  `Prices:{plan}`, `DisplayPrices:{plan}`, `TrialDays` 14, `GracePeriod`
  7 days). Without a secret key billing is off. Stripe setup:
  [runbook, section 10](../runbook.md#10-billing-stripe).
