import { useMutation, useQuery } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { api } from '../../../api/client';
import type { components } from '../../../api/schema';
import { Laptop01, Send01 } from '@untitledui/icons';
import { Table } from '@/components/application/table/table';
import { Avatar } from '@/components/base/avatar/avatar';
import { Badge } from '@/components/base/badges/badges';
import { Button } from '@/components/base/buttons/button';
import { ProgressBarBase } from '@/components/base/progress-indicators/progress-indicators';
import { SelectField, TextField } from '../../atoms/FormFields';
import { Card } from '../../molecules/Card';
import { PageHeader } from '../../molecules/PageHeader';
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
    <section className="flex flex-col gap-6">
      <PageHeader title={t('account.title')} />
      <div className="grid gap-6 lg:grid-cols-2">
        <Card title={t('account.usage')}>
          <p className="text-display-xs font-semibold text-primary">
            {tenant.data?.plan.name ?? '…'}
          </p>
          {tenant.data && (
            <div className="flex flex-col gap-2">
              <ProgressBarBase
                value={Number(tenant.data.stationCount)}
                max={Math.max(Number(tenant.data.plan.maxStations), 1)}
              />
              <p className="text-sm text-tertiary">
                {t('account.usageLine', {
                  count: tenant.data.stationCount,
                  max: tenant.data.plan.maxStations,
                  listeners: tenant.data.plan.maxListeners,
                })}
              </p>
            </div>
          )}
        </Card>
        <BillingPanel />
      </div>
      <Card title={t('account.invite')}>
        <form
          className="grid gap-4 md:grid-cols-[1fr_12rem_auto] md:items-end"
          onSubmit={invite.handleSubmit(
            (value) => !inviteMutation.isPending && inviteMutation.mutate(value),
          )}
        >
          <TextField
            control={invite.control}
            name="email"
            rules={{ required: true }}
            label={t('common.email')}
            autoComplete="off"
          />
          <SelectField
            control={invite.control}
            name="role"
            label={t('account.role')}
            options={(['Broadcaster', 'Admin', 'Owner'] as const).map((role) => ({
              value: role,
              label: t(`account.roles.${role}`),
            }))}
          />
          <Button
            type="submit"
            size="md"
            iconLeading={Send01}
            isLoading={inviteMutation.isPending}
            showTextWhileLoading
          >
            {t('account.sendInvite')}
          </Button>
        </form>
        <Notice error={inviteMutation.error} />
      </Card>
      <Card title={t('account.members')} bodyClassName="p-0 md:p-0">
        <Table aria-label={t('account.members')} size="sm">
          <Table.Header>
            <Table.Head id="email" label={t('common.email')} isRowHeader />
            <Table.Head id="role" label={t('account.role')} />
          </Table.Header>
          <Table.Body items={members.data ?? []}>
            {(member) => (
              <Table.Row id={member.userId}>
                <Table.Cell>
                  <div className="flex items-center gap-3">
                    <Avatar size="sm" initials={member.email.slice(0, 2).toUpperCase()} />
                    <span className="font-medium text-primary">{member.email}</span>
                  </div>
                </Table.Cell>
                <Table.Cell>
                  <Badge
                    type="pill-color"
                    color={member.role === 'Owner' ? 'brand' : 'gray'}
                    size="sm"
                  >
                    {t(`account.roles.${member.role}`)}
                  </Badge>
                </Table.Cell>
              </Table.Row>
            )}
          </Table.Body>
        </Table>
      </Card>
      <Card title={t('account.devices')} bodyClassName="p-0 md:p-0">
        <Table aria-label={t('account.devices')} size="sm">
          <Table.Header>
            <Table.Head id="device" label={t('account.devices')} isRowHeader />
            <Table.Head id="used" label={t('account.lastUsedColumn')} />
          </Table.Header>
          <Table.Body items={devices.data ?? []}>
            {(device) => (
              <Table.Row id={device.id}>
                <Table.Cell>
                  <div className="flex items-center gap-3">
                    <Laptop01 aria-hidden="true" className="size-5 shrink-0 text-fg-quaternary" />
                    <span className="font-medium text-primary">{device.deviceName}</span>
                    {device.current && (
                      <Badge type="pill-color" color="success" size="sm">
                        {t('account.thisDevice')}
                      </Badge>
                    )}
                  </div>
                </Table.Cell>
                <Table.Cell>
                  {t('account.lastUsed', { date: formatDateTime(device.lastUsedAt) })}
                </Table.Cell>
              </Table.Row>
            )}
          </Table.Body>
        </Table>
      </Card>
    </section>
  );
}
