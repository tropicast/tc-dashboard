import { useMutation, useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '../../../api/client';
import type { components } from '../../../api/schema';
import { formatDate } from '../../../lib/i18n';
import { data } from '../../../lib/request';
import { Notice } from '../../atoms/Notice';

type Billing = components['schemas']['BillingResponse'];

const date = (value?: string | null) => (value ? formatDate(value) : '');

/** Plan, subscription and the way to pay or manage it (Stripe's hosted pages; no card data here). */
export function BillingPanel() {
  const { t } = useTranslation();
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
      <h2>{t('billing.title')}</h2>
      {returned === 'success' && <p role="status">{t('billing.thanks')}</p>}
      <p>
        <strong>{current?.name ?? planId}</strong>
        {status === 'Trialing' &&
          ` · ${t('billing.trialUntil', { date: date(billing.data.trialEnd) })}`}
        {status === 'Active' &&
          (billing.data.cancelAtPeriodEnd
            ? ` · ${t('billing.ends', { date: date(billing.data.currentPeriodEnd) })}`
            : ` · ${t('billing.renews', { date: date(billing.data.currentPeriodEnd) })}`)}
      </p>
      {status === 'PastDue' && (
        <p className="error" role="alert">
          {t('billing.pastDue', { date: date(billing.data.graceEndsAt) })}
        </p>
      )}
      {enabled && canManage && (
        <button type="button" onClick={() => portal.mutate()} disabled={portal.isPending}>
          {t('billing.manage')}
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
                {t('billing.planLine', {
                  count: plan.maxStations,
                  listeners: plan.maxListeners,
                  bitrate: plan.maxBitrateKbps,
                })}
                {plan.directoryListing && ` · ${t('billing.radioBrowser')}`}
                {plan.embed && ` · ${t('billing.embed')}`}
                <br />
                <button
                  type="button"
                  onClick={() => checkout.mutate(plan.id)}
                  disabled={checkout.isPending}
                >
                  {trialAvailable
                    ? t('billing.startTrial')
                    : t('billing.choose', { plan: plan.name })}
                </button>
              </li>
            ))}
        </ul>
      )}
      <Notice error={checkout.error ?? portal.error} />
    </article>
  );
}
