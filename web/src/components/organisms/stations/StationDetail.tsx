import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { zodResolver } from '@hookform/resolvers/zod';
import { lazy, Suspense, useState } from 'react';
import { useForm } from 'react-hook-form';
import { Link, useParams } from 'react-router-dom';
import { api } from '../../../api/client';
import type { components } from '../../../api/schema';
import { CopyButton } from '../../atoms/CopyButton';
import { Field } from '../../atoms/Field';
import { Notice } from '../../atoms/Notice';
import { TrashIcon } from '../../atoms/TrashIcon';
import { ConfirmDialog } from '../../molecules/ConfirmDialog';
import { DirectoryStatus } from './DirectoryStatus';
import { SharePanel } from './SharePanel';
import { data } from '../../../lib/request';
import { stationSchema } from '../../../lib/station-validation';
import type { Credential, Station, Status } from '../../../lib/types';

const StationStats = lazy(() => import('../../../StationStats'));

export function StationDetail() {
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
  if (station.isPending) return <p>Loading station…</p>;
  if (!station.data) return <Notice error={station.error} />;
  const item = station.data.station;
  return (
    <section>
      <div className="heading">
        <div>
          <Link to="/stations">← Stations</Link>
          <h1>{item.name}</h1>
        </div>
        <span className={`badge ${status.data?.live ? 'live' : ''}`}>
          {status.data?.stale ? 'Status unavailable' : status.data?.live ? 'Live' : 'Offline'}
        </span>
      </div>
      <div className="grid">
        <article>
          <h2>Live status</h2>
          <strong>{status.data?.listeners ?? '—'} listeners</strong>
          {status.data?.outputs.map((output) => (
            <p key={output.format}>
              {output.format}: {output.listeners} listeners · {output.bitrateKbps ?? '—'} kbps
            </p>
          ))}
        </article>
        <article>
          <h2>Listener URLs</h2>
          <CopyUrl label="MP3" value={item.listenerUrls.mp3} />
          {item.listenerUrls.opus && <CopyUrl label="Opus" value={item.listenerUrls.opus} />}
        </article>
      </div>
      <SharePanel stationSlug={item.slug} />
      <article className="panel">
        <h2>Station settings</h2>
        <form className="form-grid" onSubmit={form.handleSubmit((value) => update.mutate(value))}>
          <Field label="Name">
            <input {...form.register('name')} />
          </Field>
          <Field label="Slug">
            <input {...form.register('slug')} />
          </Field>
          <Field label="Genre">
            <input {...form.register('genre')} />
          </Field>
          <Field label="Country">
            <input {...form.register('country')} />
          </Field>
          <Field label="Language">
            <input {...form.register('language')} />
          </Field>
          <Field label="Website">
            <input {...form.register('website')} />
          </Field>
          <Field label="Description">
            <textarea {...form.register('description')} />
          </Field>
          <label className="checkbox">
            <input type="checkbox" {...form.register('listInDirectory')} /> List on RadioBrowser
          </label>
          <DirectoryStatus stationId={id} />
          <Notice error={update.error} />
          <button>Save settings</button>
        </form>
      </article>
      <section>
        <h2>Analytics</h2>
        <Suspense fallback={<p>Loading analytics…</p>}>
          <StationStats stationId={id} />
        </Suspense>
      </section>
      <div className="grid">
        <article className="panel">
          <h2>Broadcast devices</h2>
          <form onSubmit={credentialForm.handleSubmit((value) => issue.mutate(value))}>
            <Field label="Device label">
              <input {...credentialForm.register('deviceLabel', { required: true })} />
            </Field>
            <button>Issue credential</button>
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
              title={`Revoke “${toRevoke.deviceLabel}”?`}
              confirmLabel="Revoke"
              busy={revoke.isPending}
              onConfirm={() => revoke.mutate(toRevoke.id)}
              onCancel={() => setToRevoke(null)}
            >
              <p>
                Any broadcast tool using this credential can no longer connect. A broadcast that is
                live now continues until it reconnects. This cannot be undone.
              </p>
              <Notice error={revoke.error} />
            </ConfirmDialog>
          )}
        </article>
        <article className="panel">
          <h2>Desktop app</h2>
          <p>
            In Tropicast Station, sign in with your account and choose this station: no credential
            to enter. Issue a credential only for another broadcast tool, or for a manual connection
            profile.
          </p>
        </article>
      </div>
    </section>
  );
}

function CopyUrl({ label, value }: { label: string; value: string }) {
  return (
    <p>
      <strong>{label}</strong> <code>{value}</code> <CopyButton value={value} />
    </p>
  );
}

/** Connection settings for a broadcast tool (Tropicast Station's manual profile, BUTT, Mixxx…), one copyable field each. */
function IssuedCredential({
  issued,
}: {
  issued: components['schemas']['IssuedCredentialResponse'];
}) {
  const ingest = new URL(issued.ingestUrl);
  const port = ingest.port || (ingest.protocol === 'https:' ? '443' : '80');
  const fields: [string, string][] = [
    ['Server', ingest.hostname],
    ['Port', port],
    ['Mount', ingest.pathname],
    ['Username', issued.username],
    ['Password', issued.secret],
  ];
  return (
    <div className="secret" role="status">
      <strong>Save the password now: it cannot be shown again.</strong>
      <dl>
        {fields.map(([label, value]) => (
          <div key={label}>
            <dt>{label}</dt>
            <dd>
              <code>{value}</code> <CopyButton value={value} />
            </dd>
          </div>
        ))}
      </dl>
      <p>
        Use TLS (HTTPS). For Opus, use the same settings with the mount ending in{' '}
        <code>live.opus</code>.
      </p>
    </div>
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
  if (credentials.length === 0) return <p>No active credential.</p>;
  return (
    <ul className="credentials">
      {credentials.map((credential) => (
        <li key={credential.id}>
          <span>
            {credential.deviceLabel}{' '}
            <small>
              {credential.lastUsedAt
                ? `last used ${new Date(credential.lastUsedAt).toLocaleString()}`
                : 'never used'}
            </small>
          </span>
          <button
            type="button"
            className="quiet icon"
            aria-label={`Revoke ${credential.deviceLabel}`}
            title="Revoke"
            onClick={() => onRevoke(credential)}
          >
            <TrashIcon />
          </button>
        </li>
      ))}
    </ul>
  );
}
