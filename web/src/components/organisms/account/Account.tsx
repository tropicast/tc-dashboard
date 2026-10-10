import { useMutation, useQuery } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { api } from '../../../api/client';
import type { components } from '../../../api/schema';
import { Field } from '../../atoms/Field';
import { Notice } from '../../atoms/Notice';
import { data } from '../../../lib/request';
import type { Tenant } from '../../../lib/types';

export function Account() {
  const tenant = useQuery({
    queryKey: ['tenant'],
    queryFn: () => data<Tenant>(api.GET('/api/v1/tenants/current')),
  });
  const members = useQuery({
    queryKey: ['members'],
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
      <h1>Account</h1>
      <div className="grid">
        <article className="panel">
          <h2>Plan and usage</h2>
          <p>
            <strong>{tenant.data?.plan.name ?? 'Loading…'}</strong>
          </p>
          <p>
            {tenant.data
              ? `${tenant.data.stationCount} / ${tenant.data.plan.maxStations} stations · ${tenant.data.plan.maxListeners} listeners per station`
              : ''}
          </p>
        </article>
        <article className="panel">
          <h2>Invite member</h2>
          <form onSubmit={invite.handleSubmit((value) => inviteMutation.mutate(value))}>
            <Field label="Email">
              <input {...invite.register('email', { required: true })} />
            </Field>
            <Field label="Role">
              <select {...invite.register('role')}>
                <option>Broadcaster</option>
                <option>Admin</option>
                <option>Owner</option>
              </select>
            </Field>
            <button>Send invite</button>
          </form>
          <Notice error={inviteMutation.error} />
        </article>
      </div>
      <article className="panel">
        <h2>Members</h2>
        <ul>
          {members.data?.map((member) => (
            <li key={member.userId}>
              {member.email} — {member.role}
            </li>
          ))}
        </ul>
      </article>
      <article className="panel">
        <h2>Desktop devices</h2>
        <ul>
          {devices.data?.map((device) => (
            <li key={device.id}>
              {device.deviceName}
              {device.current ? ' (this device)' : ''} — last used{' '}
              {new Date(device.lastUsedAt).toLocaleString()}
            </li>
          ))}
        </ul>
      </article>
    </section>
  );
}
