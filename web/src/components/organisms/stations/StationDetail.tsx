import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { zodResolver } from '@hookform/resolvers/zod';
import { lazy, Suspense, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router-dom';
import { ArrowLeft, Key01, Laptop01, Plus, Trash01 } from '@untitledui/icons';
import { Table } from '@/components/application/table/table';
import { BadgeWithDot } from '@/components/base/badges/badges';
import { Button } from '@/components/base/buttons/button';
import { ButtonUtility } from '@/components/base/buttons/button-utility';
import { FeaturedIcon } from '@/components/foundations/featured-icon/featured-icon';
import { api } from '../../../api/client';
import type { components } from '../../../api/schema';
import { Alert } from '../../atoms/Alert';
import { CopyButton } from '../../atoms/CopyButton';
import { CheckboxField, TextAreaField, TextField } from '../../atoms/FormFields';
import { Notice } from '../../atoms/Notice';
import { Card } from '../../molecules/Card';
import { ConfirmDialog } from '../../molecules/ConfirmDialog';
import { CopyField } from '../../molecules/CopyField';
import { Loading } from '../../molecules/Loading';
import { PageHeader } from '../../molecules/PageHeader';
import { DirectoryStatus } from './DirectoryStatus';
import { SharePanel } from './SharePanel';
import { formatDateTime } from '../../../lib/i18n';
import { data } from '../../../lib/request';
import { stationSchema } from '../../../lib/station-validation';
import type { Credential, Station, Status } from '../../../lib/types';

const StationStats = lazy(() => import('../../../StationStats'));

export function StationDetail() {
  const { t } = useTranslation();
  const { id = '' } = useParams();
  const tenantId = localStorage.getItem('tenant-id');
  const queryClient = useQueryClient();
  const station = useQuery({
    queryKey: ['station', tenantId, id],
    queryFn: async () => {
      const result = await api.GET('/api/v1/stations/{id}', { params: { path: { id } } });
      if (result.error) throw result.error;
      return { station: result.data as Station, etag: result.response.headers.get('ETag') ?? '' };
    },
  });
  const status = useQuery({
    queryKey: ['status', tenantId, id],
    queryFn: () =>
      data<Status>(api.GET('/api/v1/stations/{id}/status', { params: { path: { id } } })),
    refetchInterval: 15_000,
  });
  const credentials = useQuery({
    queryKey: ['credentials', tenantId, id],
    queryFn: () =>
      data<Credential[]>(
        api.GET('/api/v1/stations/{stationId}/credentials', {
          params: { path: { stationId: id } },
        }),
      ),
  });
  const form = useForm({
    resolver: zodResolver(stationSchema),
    values: station.data
      ? {
          name: station.data.station.name,
          slug: station.data.station.slug,
          description: station.data.station.description,
          genre: station.data.station.genre,
          country: station.data.station.country ?? '',
          language: station.data.station.language ?? '',
          website: station.data.station.website ?? '',
          listInDirectory: station.data.station.listInDirectory,
        }
      : undefined,
  });
  const update = useMutation({
    mutationFn: (value: components['schemas']['UpdateStationCommand']) =>
      data<Station>(
        api.PATCH('/api/v1/stations/{id}', {
          params: { path: { id }, header: { 'If-Match': station.data?.etag ?? '' } },
          body: {
            ...value,
            country: value.country || null,
            language: value.language || null,
            website: value.website || null,
          },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['station', tenantId, id] });
      void queryClient.invalidateQueries({ queryKey: ['directory', tenantId, id] });
    },
  });
  const credentialForm = useForm({ defaultValues: { deviceLabel: '' } });
  const issue = useMutation({
    mutationFn: (body: { deviceLabel: string }) =>
      data<components['schemas']['IssuedCredentialResponse']>(
        api.POST('/api/v1/stations/{stationId}/credentials', {
          params: { path: { stationId: id } },
          body,
        }),
      ),
    onSuccess: () =>
      void queryClient.invalidateQueries({ queryKey: ['credentials', tenantId, id] }),
  });
  const [toRevoke, setToRevoke] = useState<Credential | null>(null);
  const revoke = useMutation({
    mutationFn: (credentialId: string) =>
      data<void>(
        api.DELETE('/api/v1/stations/{stationId}/credentials/{credentialId}', {
          params: { path: { stationId: id, credentialId } },
        }),
      ),
    onSuccess: async () => {
      setToRevoke(null);
      await queryClient.invalidateQueries({ queryKey: ['credentials', tenantId, id] });
    },
  });
  if (station.isPending) return <Loading label={t('station.loading')} />;
  if (!station.data) return <Notice error={station.error} />;
  const item = station.data.station;
  const badge = status.data?.stale
    ? { color: 'warning' as const, label: t('station.statusUnavailable') }
    : status.data?.live
      ? { color: 'success' as const, label: t('station.live') }
      : { color: 'gray' as const, label: t('station.offline') };
  return (
    <section className="flex flex-col gap-6">
      <PageHeader
        breadcrumb={
          <div>
            <Button href="/stations" color="link-gray" size="sm" iconLeading={ArrowLeft}>
              {t('station.back')}
            </Button>
          </div>
        }
        title={item.name}
        badge={
          <BadgeWithDot type="pill-color" color={badge.color} size="md">
            {badge.label}
          </BadgeWithDot>
        }
      />
      <div className="grid gap-6 lg:grid-cols-2">
        <Card title={t('station.liveStatus')}>
          <p className="text-display-sm font-semibold text-primary">
            {status.data ? t('common.listeners', { count: Number(status.data.listeners) }) : '—'}
          </p>
          {status.data?.outputs.length ? (
            <ul className="flex flex-col gap-1 text-sm text-tertiary">
              {status.data.outputs.map((output) => (
                <li key={output.format}>
                  {t('station.output', {
                    format: output.format,
                    count: Number(output.listeners),
                    bitrate: output.bitrateKbps ?? '—',
                  })}
                </li>
              ))}
            </ul>
          ) : null}
        </Card>
        <Card title={t('station.listenerUrls')}>
          <CopyField label="MP3" value={item.listenerUrls.mp3} />
          {item.listenerUrls.opus && <CopyField label="Opus" value={item.listenerUrls.opus} />}
        </Card>
      </div>
      <SharePanel stationSlug={item.slug} />
      <Card title={t('station.settings')}>
        <form
          className="grid gap-5 md:grid-cols-2"
          onSubmit={form.handleSubmit((value) => !update.isPending && update.mutate(value))}
        >
          <TextField control={form.control} name="name" label={t('common.name')} />
          <TextField control={form.control} name="slug" label={t('common.slug')} />
          <TextField control={form.control} name="genre" label={t('common.genre')} />
          <TextField control={form.control} name="country" label={t('common.country')} />
          <TextField control={form.control} name="language" label={t('common.language')} />
          <TextField control={form.control} name="website" label={t('common.website')} />
          <div className="md:col-span-2">
            <TextAreaField
              control={form.control}
              name="description"
              label={t('common.description')}
            />
          </div>
          <div className="flex flex-col gap-2 md:col-span-2">
            <CheckboxField
              control={form.control}
              name="listInDirectory"
              label={t('common.listInDirectory')}
            />
            <DirectoryStatus stationId={id} />
          </div>
          <div className="flex flex-col gap-4 border-t border-secondary pt-5 md:col-span-2">
            <Notice error={update.error} />
            <div className="flex justify-end">
              <Button type="submit" size="md" isLoading={update.isPending} showTextWhileLoading>
                {t('station.save')}
              </Button>
            </div>
          </div>
        </form>
      </Card>
      <Card title={t('station.analytics')}>
        <Suspense fallback={<Loading label={t('station.loadingAnalytics')} />}>
          <StationStats stationId={id} />
        </Suspense>
      </Card>
      <div className="grid gap-6 lg:grid-cols-3">
        <Card title={t('station.devices')} className="lg:col-span-2">
          <form
            className="flex flex-col gap-3 sm:flex-row sm:items-start"
            onSubmit={credentialForm.handleSubmit(
              (value) => !issue.isPending && issue.mutate(value),
            )}
          >
            <div className="flex-1">
              <TextField
                control={credentialForm.control}
                name="deviceLabel"
                rules={{ required: true }}
                label={t('station.deviceLabel')}
              />
            </div>
            <Button
              type="submit"
              size="md"
              iconLeading={Plus}
              isLoading={issue.isPending}
              showTextWhileLoading
              className="sm:mt-6"
            >
              {t('station.issue')}
            </Button>
          </form>
          <Notice error={issue.error} />
          {issue.data && <IssuedCredential issued={issue.data} />}
          <ActiveCredentials
            credentials={credentials.data?.filter((credential) => !credential.revokedAt) ?? []}
            onRevoke={(credential) => {
              revoke.reset();
              setToRevoke(credential);
            }}
          />
          {toRevoke && (
            <ConfirmDialog
              title={t('credentials.revokeTitle', { label: toRevoke.deviceLabel })}
              confirmLabel={t('credentials.revoke')}
              busy={revoke.isPending}
              onConfirm={() => revoke.mutate(toRevoke.id)}
              onCancel={() => setToRevoke(null)}
            >
              <p>{t('credentials.revokeText')}</p>
              <Notice error={revoke.error} />
            </ConfirmDialog>
          )}
        </Card>
        <Card>
          <FeaturedIcon icon={Laptop01} color="brand" theme="light" size="lg" />
          <div className="flex flex-col gap-1">
            <h2 className="text-lg font-semibold text-primary">{t('station.desktopTitle')}</h2>
            <p className="text-sm text-tertiary">{t('station.desktopText')}</p>
          </div>
        </Card>
      </div>
    </section>
  );
}

/** Connection settings for a broadcast tool (Tropicast Station's manual profile, BUTT, Mixxx…), one copyable field each. */
function IssuedCredential({
  issued,
}: {
  issued: components['schemas']['IssuedCredentialResponse'];
}) {
  const { t } = useTranslation();
  const ingest = new URL(issued.ingestUrl);
  const port = ingest.port || (ingest.protocol === 'https:' ? '443' : '80');
  const fields: [string, string][] = [
    [t('credentials.server'), ingest.hostname],
    [t('credentials.port'), port],
    [t('credentials.mount'), ingest.pathname],
    [t('credentials.username'), issued.username],
    [t('credentials.password'), issued.secret],
  ];
  return (
    <Alert color="warning" title={t('credentials.savePassword')}>
      <dl className="mt-2 flex flex-col gap-2">
        {fields.map(([label, value]) => (
          <div key={label} className="grid gap-1 sm:grid-cols-[8rem_1fr] sm:items-center">
            <dt className="font-medium text-secondary">{label}</dt>
            <dd className="flex items-center gap-1">
              <code className="min-w-0 flex-1 rounded-md bg-primary px-2 py-1 font-mono text-sm break-all text-primary ring-1 ring-secondary ring-inset">
                {value}
              </code>
              <CopyButton value={value} />
            </dd>
          </div>
        ))}
      </dl>
      <p className="mt-3">
        {t('credentials.tlsNote')} <code className="font-mono">live.opus</code>.
      </p>
    </Alert>
  );
}

/** Active credentials only: a revoked one cannot be used again, so it is not listed (#59). */
function ActiveCredentials({
  credentials,
  onRevoke,
}: {
  credentials: Credential[];
  onRevoke: (credential: Credential) => void;
}) {
  const { t } = useTranslation();
  if (credentials.length === 0) {
    return (
      <div className="flex items-center gap-3 rounded-lg bg-secondary p-4 text-sm text-tertiary">
        <Key01 aria-hidden="true" className="size-5 text-fg-quaternary" />
        {t('credentials.none')}
      </div>
    );
  }
  return (
    <div className="-mx-4 overflow-hidden border-y border-secondary md:-mx-6">
      <Table aria-label={t('station.devices')} size="sm">
        <Table.Header>
          <Table.Head id="device" label={t('station.deviceLabel')} isRowHeader />
          <Table.Head id="actions">
            <span className="sr-only">{t('credentials.revoke')}</span>
          </Table.Head>
        </Table.Header>
        <Table.Body items={credentials}>
          {(credential) => (
            <Table.Row id={credential.id}>
              <Table.Cell>
                <div className="flex flex-col">
                  <span className="font-medium text-primary">{credential.deviceLabel}</span>
                  <span>
                    {credential.lastUsedAt
                      ? t('credentials.lastUsed', { date: formatDateTime(credential.lastUsedAt) })
                      : t('credentials.neverUsed')}
                  </span>
                </div>
              </Table.Cell>
              <Table.Cell className="w-12 text-right">
                <ButtonUtility
                  color="tertiary"
                  icon={Trash01}
                  tooltip={t('credentials.revoke')}
                  aria-label={t('credentials.revokeLabel', { label: credential.deviceLabel })}
                  onPress={() => onRevoke(credential)}
                />
              </Table.Cell>
            </Table.Row>
          )}
        </Table.Body>
      </Table>
    </div>
  );
}
