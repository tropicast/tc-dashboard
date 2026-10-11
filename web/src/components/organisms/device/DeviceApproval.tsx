import { useMutation, useQuery } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useSearchParams } from 'react-router-dom';
import { api } from '../../../api/client';
import type { components } from '../../../api/schema';
import { Field } from '../../atoms/Field';
import { Notice } from '../../atoms/Notice';
import { data } from '../../../lib/request';
import type { Problem } from '../../../lib/types';

type DeviceRequest = components['schemas']['DeviceRequestResponse'];

/** The page a desktop app opens during sign-in (device code flow): check the device, then approve or deny it. */
export function DeviceApproval() {
  const [params, setParams] = useSearchParams();
  const code = params.get('code')?.trim() ?? '';
  return (
    <section>
      <h1>Sign in a desktop app</h1>
      {code ? (
        <Decision code={code} />
      ) : (
        <CodeForm onCode={(value) => setParams({ code: value })} />
      )}
    </section>
  );
}

function CodeForm({ onCode }: { onCode: (code: string) => void }) {
  const form = useForm({ defaultValues: { code: '' } });
  return (
    <form onSubmit={form.handleSubmit((value) => onCode(value.code.trim()))}>
      <Field label="Code shown in Tropicast Station">
        <input
          autoComplete="off"
          autoCapitalize="characters"
          placeholder="BCDF-GHJK"
          {...form.register('code', { required: true })}
        />
      </Field>
      <button>Continue</button>
    </form>
  );
}

function Decision({ code }: { code: string }) {
  const request = useQuery({
    queryKey: ['device-request', code],
    queryFn: () =>
      data<DeviceRequest>(
        api.GET('/api/v1/auth/device/{userCode}', { params: { path: { userCode: code } } }),
      ),
    retry: false,
  });
  const decide = useMutation({
    mutationFn: (decision: 'approve' | 'deny') =>
      data<void>(
        decision === 'approve'
          ? api.POST('/api/v1/auth/device/approve', { body: { userCode: code } })
          : api.POST('/api/v1/auth/device/deny', { body: { userCode: code } }),
      ),
  });

  if (decide.isSuccess) {
    return (
      <p role="status">
        {decide.variables === 'approve'
          ? 'Done. Go back to Tropicast Station: it is signed in.'
          : 'Refused. Tropicast Station was not signed in.'}
      </p>
    );
  }
  if (request.isPending) return <p>Checking the code…</p>;
  if (request.isError) {
    return (request.error as Problem | undefined)?.status === 404 ? (
      <p role="alert" className="notice toast error">
        The code {code} is unknown, expired or already used. Start the sign-in again in Tropicast
        Station.
      </p>
    ) : (
      <Notice error={request.error} />
    );
  }
  const device = request.data;
  return (
    <>
      <p>
        <strong>{device.deviceName}</strong> asks to sign in to your account with the code{' '}
        <strong>{device.userCode}</strong>. Check that this code is the one shown in Tropicast
        Station.
      </p>
      <p>
        Asked at {new Date(device.createdAt).toLocaleString()}. Expires at{' '}
        {new Date(device.expiresAt).toLocaleString()}.
      </p>
      <Notice error={decide.error} />
      <p>
        <button disabled={decide.isPending} onClick={() => decide.mutate('approve')}>
          Approve
        </button>{' '}
        <button disabled={decide.isPending} onClick={() => decide.mutate('deny')}>
          Deny
        </button>
      </p>
    </>
  );
}
