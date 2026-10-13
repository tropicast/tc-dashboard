import { useMutation, useQuery } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { api } from '../../../api/client';
import type { components } from '../../../api/schema';
import { Field } from '../../atoms/Field';
import { Notice } from '../../atoms/Notice';
import { BillingPanel } from './BillingPanel';
import { formatDateTime } from '../../../lib/i18n';
import { data } from '../../../lib/request';
import type { Tenant } from '../../../lib/types';

export function Account() {
  const { t } = useTranslation();
  const tenantId = localStorage.getItem('tenant-id');
  const tenant = useQuery({
    queryKey: ['tenant', tenantId],
    queryFn: () => data<Tenant>(api.GET('/api/v1/tenants/current')),
  });
  const members = useQuery({
    queryKey: ['members', tenantId],
    queryFn: () =>
      data<components['schemas']['MemberResponse'][]>(api.GET('/api/v1/tenants/current/members')),
  });
  const devices = useQuery({
    queryKey: ['devices'],
    queryFn: () => data<components['schemas']['DeviceResponse'][]>(api.GET('/api/v1/auth/devices')),
  });
  const invite = useForm({ defaultValues: { email: '', role: 'Broadcaster' as const } });
  const inviteMutation = useMutation({
    mutationFn: (value: { email: string; role: 'Owner' | 'Admin' | 'Broadcaster' }) =>
      data<components['schemas']['InvitationResponse']>(
        api.POST('/api/v1/tenants/current/invitations', { body: value }),
      ),
  });
  return (
    <section>
      <h1>{t('account.title')}</h1>
      <div className="grid">
        <article className="panel">
          <h2>{t('account.usage')}</h2>
          <p>
            <strong>{tenant.data?.plan.name ?? '…'}</strong>
          </p>
          <p>
            {tenant.data
              ? t('account.usageLine', {
                  count: tenant.data.stationCount,
                  max: tenant.data.plan.maxStations,
                  listeners: tenant.data.plan.maxListeners,
                })
              : ''}
          </p>
        </article>
        <BillingPanel />
        <article className="panel">
          <h2>{t('account.invite')}</h2>
          <form onSubmit={invite.handleSubmit((value) => inviteMutation.mutate(value))}>
            <Field label={t('common.email')}>
              <input {...invite.register('email', { required: true })} />
            </Field>
            <Field label={t('account.role')}>
              <select {...invite.register('role')}>
                <option value="Broadcaster">{t('account.roles.Broadcaster')}</option>
                <option value="Admin">{t('account.roles.Admin')}</option>
                <option value="Owner">{t('account.roles.Owner')}</option>
              </select>
            </Field>
            <button>{t('account.sendInvite')}</button>
          </form>
          <Notice error={inviteMutation.error} />
        </article>
      </div>
      <article className="panel">
        <h2>{t('account.members')}</h2>
        <ul>
          {members.data?.map((member) => (
            <li key={member.userId}>
              {member.email} — {t(`account.roles.${member.role}`)}
            </li>
          ))}
        </ul>
      </article>
      <article className="panel">
        <h2>{t('account.devices')}</h2>
        <ul>
          {devices.data?.map((device) => (
            <li key={device.id}>
              {device.deviceName}
              {device.current ? ` ${t('account.thisDevice')}` : ''} —{' '}
              {t('account.lastUsed', { date: formatDateTime(device.lastUsedAt) })}
            </li>
          ))}
        </ul>
      </article>
    </section>
  );
}
