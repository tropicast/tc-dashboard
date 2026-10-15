import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';
import { ArrowDown, ArrowLeft, ArrowUp, Plus, Trash01, XClose } from '@untitledui/icons';
import { Button } from '@/components/base/buttons/button';
import { ButtonUtility } from '@/components/base/buttons/button-utility';
import { Input } from '@/components/base/input/input';
import { Toggle } from '@/components/base/toggle/toggle';
import { useDocumentTitle } from '@/hooks/use-document-title';
import { cx } from '@/utils/cx';
import { api } from '../../../api/client';
import { Alert } from '../../atoms/Alert';
import { Notice } from '../../atoms/Notice';
import { Card } from '../../molecules/Card';
import { ConfirmDialog } from '../../molecules/ConfirmDialog';
import { Loading } from '../../molecules/Loading';
import { PageHeader } from '../../molecules/PageHeader';
import { formatTrackDuration } from '../../../lib/i18n';
import { data } from '../../../lib/request';
import type { AutoDj, Playlist, PlaylistSummary, Station, TrackLibrary } from '../../../lib/types';

/** The playlist editor (#109): create, rename, shuffle, add from the library, reorder by keyboard, delete. */
export function Playlists() {
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
  const autoDj = useQuery({
    queryKey: ['autodj', tenantId, id],
    queryFn: () =>
      data<AutoDj>(
        api.GET('/api/v1/stations/{stationId}/autodj', { params: { path: { stationId: id } } }),
      ),
  });
  const playlists = useQuery({
    queryKey: ['playlists', tenantId, id],
    queryFn: () =>
      data<PlaylistSummary[]>(
        api.GET('/api/v1/stations/{stationId}/playlists', { params: { path: { stationId: id } } }),
      ),
    enabled: autoDj.data?.available === true,
  });
  useDocumentTitle(t('playlists.title'), station.data?.station.name);
  const [selected, setSelected] = useState<string | null>(null);
  const [name, setName] = useState('');
  const current = selected ?? playlists.data?.[0]?.id ?? null;

  const create = useMutation({
    mutationFn: (value: string) =>
      data<Playlist>(
        api.POST('/api/v1/stations/{stationId}/playlists', {
          params: { path: { stationId: id } },
          body: { name: value, shuffle: false },
        }),
      ),
    onSuccess: async (playlist) => {
      setName('');
      setSelected(playlist.id);
      await queryClient.invalidateQueries({ queryKey: ['playlists', tenantId, id] });
    },
  });

  if (station.isPending || autoDj.isPending) return <Loading label={t('playlists.loading')} />;
  if (!station.data) return <Notice error={station.error} />;
  if (!autoDj.data) return <Notice error={autoDj.error} />;

  return (
    <section className="flex flex-col gap-6">
      <PageHeader
        breadcrumb={
          <div>
            <Button href={`/stations/${id}`} color="link-gray" size="sm" iconLeading={ArrowLeft}>
              {station.data.station.name}
            </Button>
          </div>
        }
        title={t('playlists.title')}
        description={t('playlists.intro')}
      />
      {!autoDj.data.available ? (
        <Alert color="gray" title={t('autodj.notIncluded')}>
          <Link to="/account" className="font-semibold underline">
            {t('media.upgrade')}
          </Link>
        </Alert>
      ) : (
        <div className="grid gap-6 lg:grid-cols-3">
          <Card title={t('playlists.list')}>
            <form
              className="flex items-end gap-2"
              onSubmit={(event) => {
                event.preventDefault();
                if (name.trim() && !create.isPending) create.mutate(name.trim());
              }}
            >
              <div className="min-w-0 flex-1">
                <Input label={t('playlists.newName')} value={name} onChange={setName} />
              </div>
              <Button type="submit" size="md" iconLeading={Plus} isLoading={create.isPending}>
                {t('playlists.create')}
              </Button>
            </form>
            <Notice error={create.error} />
            {playlists.data?.length === 0 ? (
              <p className="text-sm text-tertiary">{t('playlists.none')}</p>
            ) : (
              <ul className="flex flex-col gap-1" aria-label={t('playlists.list')}>
                {playlists.data?.map((playlist) => (
                  <li key={playlist.id}>
                    <button
                      type="button"
                      aria-current={playlist.id === current ? 'true' : undefined}
                      onClick={() => setSelected(playlist.id)}
                      className={cx(
                        'flex w-full flex-col rounded-lg px-3 py-2 text-left text-sm outline-focus-ring focus-visible:outline-2',
                        playlist.id === current
                          ? 'bg-active text-primary'
                          : 'text-secondary hover:bg-primary_hover',
                      )}
                    >
                      <span className="font-semibold">{playlist.name}</span>
                      <span className="text-tertiary">
                        {t('playlists.summary', {
                          count: Number(playlist.trackCount),
                          duration: formatTrackDuration(Number(playlist.durationMs)),
                        })}
                        {playlist.id === autoDj.data.defaultPlaylistId && autoDj.data.enabled
                          ? ` · ${t('playlists.playing')}`
                          : ''}
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </Card>
          <div className="lg:col-span-2">
            {current ? (
              <PlaylistEditor
                key={current}
                stationId={id}
                playlistId={current}
                onDeleted={() => setSelected(null)}
              />
            ) : (
              <Card title={t('playlists.editor')}>
                <p className="text-sm text-tertiary">{t('playlists.createFirst')}</p>
              </Card>
            )}
          </div>
        </div>
      )}
    </section>
  );
}

function PlaylistEditor({
  stationId,
  playlistId,
  onDeleted,
}: {
  stationId: string;
  playlistId: string;
  onDeleted: () => void;
}) {
  const { t } = useTranslation();
  const tenantId = localStorage.getItem('tenant-id');
  const queryClient = useQueryClient();
  const key = ['playlist', tenantId, stationId, playlistId];
  const playlist = useQuery({
    queryKey: key,
    queryFn: () =>
      data<Playlist>(
        api.GET('/api/v1/stations/{stationId}/playlists/{playlistId}', {
          params: { path: { stationId, playlistId } },
        }),
      ),
  });
  const library = useQuery({
    queryKey: ['tracks', tenantId, stationId],
    queryFn: () =>
      data<TrackLibrary>(
        api.GET('/api/v1/stations/{stationId}/tracks', { params: { path: { stationId } } }),
      ),
  });
  const [name, setName] = useState<string | null>(null);
  const [announcement, setAnnouncement] = useState('');
  const [deleting, setDeleting] = useState(false);
  const focusAfter = useRef<string | null>(null);
  const list = useRef<HTMLOListElement>(null);

  const saved = async (result: Playlist) => {
    queryClient.setQueryData(key, result);
    await queryClient.invalidateQueries({ queryKey: ['playlists', tenantId, stationId] });
  };
  const setTracks = useMutation({
    mutationFn: (trackIds: string[]) =>
      data<Playlist>(
        api.PUT('/api/v1/stations/{stationId}/playlists/{playlistId}/tracks', {
          params: { path: { stationId, playlistId } },
          body: { trackIds },
        }),
      ),
    onSuccess: saved,
  });
  const patch = useMutation({
    mutationFn: (body: { name?: string; shuffle?: boolean }) =>
      data<Playlist>(
        api.PATCH('/api/v1/stations/{stationId}/playlists/{playlistId}', {
          params: { path: { stationId, playlistId } },
          body,
        }),
      ),
    onSuccess: async (result) => {
      setName(null);
      await saved(result);
    },
  });
  const remove = useMutation({
    mutationFn: () =>
      data<void>(
        api.DELETE('/api/v1/stations/{stationId}/playlists/{playlistId}', {
          params: { path: { stationId, playlistId } },
        }),
      ),
    onSuccess: async () => {
      setDeleting(false);
      queryClient.removeQueries({ queryKey: key });
      await queryClient.invalidateQueries({ queryKey: ['playlists', tenantId, stationId] });
      onDeleted();
    },
  });

  // Keeps keyboard focus on the button that moved its track, in the track's new place.
  useEffect(() => {
    if (focusAfter.current && list.current) {
      list.current.querySelector<HTMLElement>(`[data-focus="${focusAfter.current}"]`)?.focus();
      focusAfter.current = null;
    }
  }, [playlist.data]);

  if (!playlist.data)
    return playlist.error ? (
      <Notice error={playlist.error} />
    ) : (
      <Loading label={t('playlists.loading')} />
    );
  const tracks = playlist.data.tracks;
  const ids = tracks.map((track) => track.trackId);
  const busy = setTracks.isPending || patch.isPending;

  const move = (index: number, by: -1 | 1) => {
    const next = [...ids];
    [next[index], next[index + by]] = [next[index + by], next[index]];
    const position = index + by;
    focusAfter.current = `${by < 0 ? 'up' : 'down'}-${position}`;
    if (position === 0 || position === ids.length - 1) {
      // The button in that direction disappears at the ends: stay on the other one.
      focusAfter.current = `${by < 0 ? 'down' : 'up'}-${position}`;
    }
    setAnnouncement(
      t('playlists.moved', {
        title: tracks[index].title,
        position: position + 1,
        count: ids.length,
      }),
    );
    setTracks.mutate(next);
  };

  return (
    <Card
      title={playlist.data.name}
      description={t('playlists.summary', {
        count: tracks.length,
        duration: formatTrackDuration(Number(playlist.data.durationMs)),
      })}
      actions={
        <Button
          color="secondary-destructive"
          size="sm"
          iconLeading={Trash01}
          onPress={() => setDeleting(true)}
        >
          {t('playlists.delete')}
        </Button>
      }
    >
      <p className="sr-only" aria-live="polite">
        {announcement}
      </p>
      <form
        className="flex items-end gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          if (name?.trim()) patch.mutate({ name: name.trim() });
        }}
      >
        <div className="min-w-0 flex-1">
          <Input
            label={t('playlists.name')}
            value={name ?? playlist.data.name}
            onChange={setName}
          />
        </div>
        <Button type="submit" color="secondary" size="md" isDisabled={!name?.trim() || busy}>
          {t('playlists.rename')}
        </Button>
      </form>
      <Toggle
        label={t('playlists.shuffle')}
        hint={t('playlists.shuffleHint')}
        isSelected={playlist.data.shuffle}
        isDisabled={busy}
        onChange={(shuffle) => patch.mutate({ shuffle })}
      />
      <Notice error={setTracks.error ?? patch.error} />
      <h3 className="text-md font-semibold text-primary">{t('playlists.inPlaylist')}</h3>
      {tracks.length === 0 ? (
        <p className="text-sm text-tertiary">{t('playlists.empty')}</p>
      ) : (
        <ol
          ref={list}
          className="flex flex-col divide-y divide-secondary rounded-lg ring-1 ring-secondary"
        >
          {tracks.map((track, index) => (
            <li
              key={`${track.trackId}-${index}`}
              className="flex items-center gap-3 px-3 py-2 text-sm"
            >
              <span className="w-6 text-right text-quaternary">{index + 1}</span>
              <div className="flex min-w-0 flex-1 flex-col">
                <span className="truncate font-medium text-primary">{track.title}</span>
                <span className="truncate text-tertiary">
                  {[track.artist, formatTrackDuration(Number(track.durationMs))]
                    .filter(Boolean)
                    .join(' · ')}
                </span>
              </div>
              {index > 0 && (
                <ButtonUtility
                  color="tertiary"
                  icon={ArrowUp}
                  data-focus={`up-${index}`}
                  isDisabled={busy}
                  aria-label={t('playlists.moveUp', { title: track.title })}
                  tooltip={t('playlists.moveUp', { title: track.title })}
                  onPress={() => move(index, -1)}
                />
              )}
              {index < tracks.length - 1 && (
                <ButtonUtility
                  color="tertiary"
                  icon={ArrowDown}
                  data-focus={`down-${index}`}
                  isDisabled={busy}
                  aria-label={t('playlists.moveDown', { title: track.title })}
                  tooltip={t('playlists.moveDown', { title: track.title })}
                  onPress={() => move(index, 1)}
                />
              )}
              <ButtonUtility
                color="tertiary"
                icon={XClose}
                isDisabled={busy}
                aria-label={t('playlists.remove', { title: track.title })}
                tooltip={t('playlists.remove', { title: track.title })}
                onPress={() => {
                  setAnnouncement(t('playlists.removed', { title: track.title }));
                  setTracks.mutate(ids.filter((_, i) => i !== index));
                }}
              />
            </li>
          ))}
        </ol>
      )}
      <h3 className="text-md font-semibold text-primary">{t('playlists.addFromLibrary')}</h3>
      {library.data?.tracks.length === 0 ? (
        <p className="text-sm text-tertiary">
          {t('playlists.libraryEmpty')}{' '}
          <Link
            to={`/stations/${stationId}/library`}
            className="font-semibold text-brand-secondary underline"
          >
            {t('media.title')}
          </Link>
        </p>
      ) : (
        <ul className="flex flex-col divide-y divide-secondary rounded-lg ring-1 ring-secondary">
          {library.data?.tracks.map((track) => (
            <li key={track.id} className="flex items-center gap-3 px-3 py-2 text-sm">
              <div className="flex min-w-0 flex-1 flex-col">
                <span className="truncate font-medium text-primary">{track.title}</span>
                <span className="truncate text-tertiary">
                  {[track.artist, formatTrackDuration(Number(track.durationMs))]
                    .filter(Boolean)
                    .join(' · ')}
                </span>
              </div>
              <ButtonUtility
                color="tertiary"
                icon={Plus}
                isDisabled={busy}
                aria-label={t('playlists.add', { title: track.title })}
                tooltip={t('playlists.add', { title: track.title })}
                onPress={() => {
                  setAnnouncement(t('playlists.added', { title: track.title }));
                  setTracks.mutate([...ids, track.id]);
                }}
              />
            </li>
          ))}
        </ul>
      )}
      {deleting && (
        <ConfirmDialog
          title={t('playlists.deleteTitle', { name: playlist.data.name })}
          confirmLabel={t('playlists.delete')}
          busy={remove.isPending}
          onConfirm={() => remove.mutate()}
          onCancel={() => setDeleting(false)}
        >
          <p>{t('playlists.deleteText')}</p>
          <Notice error={remove.error} />
        </ConfirmDialog>
      )}
    </Card>
  );
}
