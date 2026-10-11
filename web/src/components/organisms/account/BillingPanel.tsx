import { useMutation, useQuery } from '@tanstack/react-query';
import { api } from '../../../api/client';
import type { components } from '../../../api/schema';
import { data } from '../../../lib/request';
import { Notice } from '../../atoms/Notice';

type Billing = components['schemas']['BillingResponse'];

const date = (value?: string | null) => (value ? new Date(value).toLocaleDateString() : '');

/** Plan, subscription and the way to pay or manage it (Stripe's hosted pages; no card data here). */
export function BillingPanel() {
  const tenantId = localStorage.getItem('tenant-id');
  const billing = useQuery({
    queryKey: ['billing', tenantId],
    queryFn: () => data<Billing>(api.GET('/api/v1/billing')),
  });
  const redirect = (url: string) => window.location.assign(url);
  const checkout = useMutation({
    mutationFn: (planId: string) =>
      data<components['schemas']['RedirectResponse']>(
        api.POST('/api/v1/billing/checkout', { body: { planId } }),
      ),
    onSuccess: (result) => redirect(result.url),
  });
  const portal = useMutation({
    mutationFn: () =>
      data<components['schemas']['RedirectResponse']>(api.POST('/api/v1/billing/portal')),
    onSuccess: (result) => redirect(result.url),
  });
  const returned = new URLSearchParams(window.location.search).get('billing');
  if (!billing.data) return <Notice error={billing.error} />;
  const { planId, status, plans, trialAvailable, canManage, enabled } = billing.data;
  const current = plans.find((plan) => plan.id === planId);
  const subscribed = status === 'Active' || status === 'Trialing' || status === 'PastDue';
  return (
    <article className="panel">
      <h2>Plan and billing</h2>
      {returned === 'success' && (
        <p role="status">Thank you: the plan changes as soon as the payment is confirmed.</p>
      )}
      <p>
        <strong>{current?.name ?? planId}</strong>
        {status === 'Trialing' && ` · trial until ${date(billing.data.trialEnd)}`}
        {status === 'Active' &&
          (billing.data.cancelAtPeriodEnd
            ? ` · ends ${date(billing.data.currentPeriodEnd)}`
            : ` · renews ${date(billing.data.currentPeriodEnd)}`)}
      </p>
      {status === 'PastDue' && (
        <p className="error" role="alert">
          The last payment failed. Update the payment method before {date(billing.data.graceEndsAt)}
          , or the workspace moves to the Free plan.
        </p>
      )}
      {enabled && canManage && (
        <button type="button" onClick={() => portal.mutate()} disabled={portal.isPending}>
          Manage billing
        </button>
      )}
      {enabled && !subscribed && (
        <ul className="plans">
          {plans
            .filter((plan) => plan.purchasable)
            .map((plan) => (
              <li key={plan.id}>
                <strong>{plan.name}</strong> {plan.price && `· ${plan.price}`}
                <br />
                {plan.maxStations} station(s) · {plan.maxListeners} listeners · up to{' '}
                {plan.maxBitrateKbps} kbps{plan.directoryListing && ' · RadioBrowser'}
                {plan.embed && ' · embed'}
                <br />
                <button
                  type="button"
                  onClick={() => checkout.mutate(plan.id)}
                  disabled={checkout.isPending}
                >
                  {trialAvailable ? 'Start free trial' : `Choose ${plan.name}`}
                </button>
              </li>
            ))}
        </ul>
      )}
      <Notice error={checkout.error ?? portal.error} />
    </article>
  );
}
