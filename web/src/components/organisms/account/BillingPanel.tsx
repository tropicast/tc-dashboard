import { useMutation, useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '../../../api/client';
import type { components } from '../../../api/schema';
import { formatDate } from '../../../lib/i18n';
import { data } from '../../../lib/request';
import { CreditCard02 } from '@untitledui/icons';
import { Badge } from '@/components/base/badges/badges';
import { Button } from '@/components/base/buttons/button';
import { Alert } from '../../atoms/Alert';
import { Notice } from '../../atoms/Notice';
import { Card } from '../../molecules/Card';

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
    <Card
      title={t('billing.title')}
      actions={
        enabled &&
        canManage && (
          <Button
            color="secondary"
            size="sm"
            iconLeading={CreditCard02}
            isLoading={portal.isPending}
            showTextWhileLoading
            onPress={() => portal.mutate()}
          >
            {t('billing.manage')}
          </Button>
        )
      }
    >
      {returned === 'success' && <Alert color="success" title={t('billing.thanks')} />}
      <p className="flex flex-wrap items-center gap-2 text-sm text-tertiary">
        <span className="text-display-xs font-semibold text-primary">
          {current?.name ?? planId}
        </span>
        {status === 'Trialing' && (
          <Badge type="pill-color" color="brand" size="sm">
            {t('billing.trialUntil', { date: date(billing.data.trialEnd) })}
          </Badge>
        )}
        {status === 'Active' && (
          <Badge type="pill-color" color="gray" size="sm">
            {billing.data.cancelAtPeriodEnd
              ? t('billing.ends', { date: date(billing.data.currentPeriodEnd) })
              : t('billing.renews', { date: date(billing.data.currentPeriodEnd) })}
          </Badge>
        )}
      </p>
      {status === 'PastDue' && (
        <Alert
          color="error"
          title={t('billing.pastDue', { date: date(billing.data.graceEndsAt) })}
        />
      )}
      {enabled && !subscribed && (
        <ul className="flex flex-col gap-3">
          {plans
            .filter((plan) => plan.purchasable)
            .map((plan) => (
              <li
                key={plan.id}
                className="flex flex-col gap-3 rounded-xl p-4 ring-1 ring-secondary sm:flex-row sm:items-center"
              >
                <div className="flex min-w-0 flex-1 flex-col gap-0.5 text-sm">
                  <p className="font-semibold text-primary">
                    {plan.name}
                    {plan.price && (
                      <span className="font-normal text-tertiary"> · {plan.price}</span>
                    )}
                  </p>
                  <p className="text-tertiary">
                    {t('billing.planLine', {
                      count: plan.maxStations,
                      listeners: plan.maxListeners,
                      bitrate: plan.maxBitrateKbps,
                    })}
                    {plan.directoryListing && ` · ${t('billing.radioBrowser')}`}
                    {plan.embed && ` · ${t('billing.embed')}`}
                  </p>
                </div>
                <Button
                  size="sm"
                  isDisabled={checkout.isPending}
                  onPress={() => checkout.mutate(plan.id)}
                >
                  {trialAvailable
                    ? t('billing.startTrial')
                    : t('billing.choose', { plan: plan.name })}
                </Button>
              </li>
            ))}
        </ul>
      )}
      <Notice error={checkout.error ?? portal.error} />
    </Card>
  );
}
