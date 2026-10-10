import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { lazy, Suspense } from 'react';
import { useForm } from 'react-hook-form';
import { Link, useParams } from 'react-router-dom';
import { api } from '../../../api/client';
import type { components } from '../../../api/schema';
import { CopyButton } from '../../atoms/CopyButton';
import { Field } from '../../atoms/Field';
import { Notice } from '../../atoms/Notice';
import { data } from '../../../lib/request';
import type { Credential, Station, Status } from '../../../lib/types';

const StationStats = lazy(() => import('../../../StationStats'));

export function StationDetail() {
  const { id = '' } = useParams();
  const queryClient = useQueryClient();
  const station = useQuery({
    queryKey: ['station', id],
    queryFn: async () => {
      const result = await api.GET('/api/v1/stations/{id}', { params: { path: { id } } });
      if (result.error) throw result.error;
      return { station: result.data as Station, etag: result.response.headers.get('ETag') ?? '' };
    },
  });
  const status = useQuery({
    queryKey: ['status', id],
    queryFn: () =>
      data<Status>(api.GET('/api/v1/stations/{id}/status', { params: { path: { id } } })),
    refetchInterval: 15_000,
  });
  const credentials = useQuery({
    queryKey: ['credentials', id],
    queryFn: () =>
      data<Credential[]>(
        api.GET('/api/v1/stations/{stationId}/credentials', {
          params: { path: { stationId: id } },
        }),
      ),
  });
  const form = useForm({
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
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['station', id] }),
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
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['credentials', id] }),
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
            <input type="checkbox" {...form.register('listInDirectory')} /> List in directory
          </label>
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
          {issue.data && (
            <div className="secret" role="status">
              <strong>Save this one-time secret now.</strong>
              <code>
                {issue.data.username}:{issue.data.secret}
              </code>
            </div>
          )}
          <ul>
            {credentials.data?.map((credential) => (
              <li key={credential.id}>
                {credential.deviceLabel} — {credential.revokedAt ? 'revoked' : 'active'}
              </li>
            ))}
          </ul>
        </article>
        <article className="panel">
          <h2>Desktop app</h2>
          <p>
            Open Tropicast Desktop, choose this station, and enter a newly issued device credential.
            The secret cannot be displayed again.
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
