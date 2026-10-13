import { useMutation, useQuery } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { Trans, useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { api } from '../../../api/client';
import type { components } from '../../../api/schema';
import { CheckCircle, Laptop01, XCircle } from '@untitledui/icons';
import { Button } from '@/components/base/buttons/button';
import { FeaturedIcon } from '@/components/foundations/featured-icon/featured-icon';
import { Alert } from '../../atoms/Alert';
import { TextField } from '../../atoms/FormFields';
import { Card } from '../../molecules/Card';
import { Loading } from '../../molecules/Loading';
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
    <section className="mx-auto flex max-w-lg flex-col gap-6">
      <div className="flex flex-col items-center gap-4 text-center">
        <FeaturedIcon icon={Laptop01} color="brand" theme="modern" size="xl" />
        <h1 className="text-display-xs font-semibold text-primary">{t('device.title')}</h1>
      </div>
      <Card bodyClassName="gap-5">
        {code ? (
          <Decision code={code} />
        ) : (
          <CodeForm onCode={(value) => setParams({ code: value })} />
        )}
      </Card>
    </section>
  );
}

function CodeForm({ onCode }: { onCode: (code: string) => void }) {
  const { t } = useTranslation();
  const form = useForm({ defaultValues: { code: '' } });
  return (
    <form
      className="flex flex-col gap-5"
      onSubmit={form.handleSubmit((value) => onCode(value.code.trim()))}
    >
      <TextField
        control={form.control}
        name="code"
        rules={{ required: true }}
        label={t('device.codeLabel')}
        autoComplete="off"
        placeholder="BCDF-GHJK"
        inputClassName="font-mono text-lg tracking-widest uppercase"
      />
      <Button type="submit" size="lg">
        {t('common.continue')}
      </Button>
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
      <Alert
        color={decide.variables === 'approve' ? 'success' : 'gray'}
        title={decide.variables === 'approve' ? t('device.approved') : t('device.denied')}
      />
    );
  }
  if (request.isPending) return <Loading label={t('device.checking')} />;
  if (request.isError) {
    return (request.error as Problem | undefined)?.status === 404 ? (
      <Alert color="error" title={t('device.unknown', { code })} />
    ) : (
      <Notice error={request.error} />
    );
  }
  const device = request.data;
  return (
    <>
      <p className="text-md text-secondary">
        <Trans
          i18nKey="device.asks"
          values={{ name: device.deviceName, code: device.userCode }}
          components={{ b: <strong /> }}
        />
      </p>
      <p className="text-sm text-tertiary">
        {t('device.times', {
          created: formatDateTime(device.createdAt),
          expires: formatDateTime(device.expiresAt),
        })}
      </p>
      <Notice error={decide.error} />
      <div className="grid gap-3 sm:grid-cols-2">
        <Button
          color="secondary-destructive"
          size="lg"
          iconLeading={XCircle}
          isDisabled={decide.isPending}
          onPress={() => decide.mutate('deny')}
        >
          {t('device.deny')}
        </Button>
        <Button
          size="lg"
          iconLeading={CheckCircle}
          isDisabled={decide.isPending}
          onPress={() => decide.mutate('approve')}
        >
          {t('device.approve')}
        </Button>
      </div>
    </>
  );
}
