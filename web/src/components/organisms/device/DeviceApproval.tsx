import { useMutation, useQuery } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { Trans, useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { api } from '../../../api/client';
import type { components } from '../../../api/schema';
import { Field } from '../../atoms/Field';
import { Notice } from '../../atoms/Notice';
import { formatDateTime } from '../../../lib/i18n';
import { data } from '../../../lib/request';
import type { Problem } from '../../../lib/types';

type DeviceRequest = components['schemas']['DeviceRequestResponse'];

/** The page a desktop app opens during sign-in (device code flow): check the device, then approve or deny it. */
export function DeviceApproval() {
  const { t } = useTranslation();
  const [params, setParams] = useSearchParams();
  const code = params.get('code')?.trim() ?? '';
  return (
    <section>
      <h1>{t('device.title')}</h1>
      {code ? (
        <Decision code={code} />
      ) : (
        <CodeForm onCode={(value) => setParams({ code: value })} />
      )}
    </section>
  );
}

function CodeForm({ onCode }: { onCode: (code: string) => void }) {
  const { t } = useTranslation();
  const form = useForm({ defaultValues: { code: '' } });
  return (
    <form onSubmit={form.handleSubmit((value) => onCode(value.code.trim()))}>
      <Field label={t('device.codeLabel')}>
        <input
          autoComplete="off"
          autoCapitalize="characters"
          placeholder="BCDF-GHJK"
          {...form.register('code', { required: true })}
        />
      </Field>
      <button>{t('common.continue')}</button>
    </form>
  );
}

function Decision({ code }: { code: string }) {
  const { t } = useTranslation();
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
        {decide.variables === 'approve' ? t('device.approved') : t('device.denied')}
      </p>
    );
  }
  if (request.isPending) return <p>{t('device.checking')}</p>;
  if (request.isError) {
    return (request.error as Problem | undefined)?.status === 404 ? (
      <p role="alert" className="notice toast error">
        {t('device.unknown', { code })}
      </p>
    ) : (
      <Notice error={request.error} />
    );
  }
  const device = request.data;
  return (
    <>
      <p>
        <Trans
          i18nKey="device.asks"
          values={{ name: device.deviceName, code: device.userCode }}
          components={{ b: <strong /> }}
        />
      </p>
      <p>
        {t('device.times', {
          created: formatDateTime(device.createdAt),
          expires: formatDateTime(device.expiresAt),
        })}
      </p>
      <Notice error={decide.error} />
      <p>
        <button disabled={decide.isPending} onClick={() => decide.mutate('approve')}>
          {t('device.approve')}
        </button>{' '}
        <button disabled={decide.isPending} onClick={() => decide.mutate('deny')}>
          {t('device.deny')}
        </button>
      </p>
    </>
  );
}
