import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useRef, useState, type DragEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';
import { ArrowLeft, MusicNote01, Trash01, UploadCloud02 } from '@untitledui/icons';
import { Table } from '@/components/application/table/table';
import { Button } from '@/components/base/buttons/button';
import { ButtonUtility } from '@/components/base/buttons/button-utility';
import { ProgressBarBase } from '@/components/base/progress-indicators/progress-indicators';
import { useDocumentTitle } from '@/hooks/use-document-title';
import { useStation } from '@/hooks/use-station';
import { cx } from '@/utils/cx';
import { api } from '../../../api/client';
import { Alert } from '../../atoms/Alert';
import { AutoDjUpgrade } from '../../atoms/AutoDjUpgrade';
import { Notice } from '../../atoms/Notice';
import { Card } from '../../molecules/Card';
import { ConfirmDialog } from '../../molecules/ConfirmDialog';
import { Loading } from '../../molecules/Loading';
import { PageHeader } from '../../molecules/PageHeader';
import { formatStorage, formatTrackDuration } from '../../../lib/i18n';
import { data, problemMessage } from '../../../lib/request';
import type { Track, TrackLibrary, TrackUpload } from '../../../lib/types';
import { acceptedExtensions, isAccepted, maxTrackBytes, putFile } from '../../../lib/upload';

type UploadState = 'waiting' | 'uploading' | 'checking' | 'done' | 'error';

interface Upload {
  key: string;
  file: File;
  state: UploadState;
  progress: number;
  error?: string;
}

/** A station's tracks for AutoDJ (#108): drag-and-drop upload with progress, storage use, delete. */
export function MediaLibrary() {
  const { t } = useTranslation();
  const { id = '' } = useParams();
  const tenantId = localStorage.getItem('tenant-id');
  const queryClient = useQueryClient();
  const station = useStation(id);
  const library = useQuery({
    queryKey: ['tracks', tenantId, id],
    queryFn: () =>
      data<TrackLibrary>(
        api.GET('/api/v1/stations/{stationId}/tracks', { params: { path: { stationId: id } } }),
      ),
  });
  useDocumentTitle(t('media.title'), station.data?.station.name);
  const [uploads, setUploads] = useState<Upload[]>([]);
  const [announcement, setAnnouncement] = useState('');
  const [dragging, setDragging] = useState(false);
  const [toDelete, setToDelete] = useState<Track | null>(null);
  const input = useRef<HTMLInputElement>(null);
  const running = useRef(false);
  const queue = useRef<Upload[]>([]);

  const remove = useMutation({
    mutationFn: (trackId: string) =>
      data<void>(
        api.DELETE('/api/v1/stations/{stationId}/tracks/{trackId}', {
          params: { path: { stationId: id, trackId } },
        }),
      ),
    onSuccess: async () => {
      setToDelete(null);
      await refresh();
    },
  });

  const refresh = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ['tracks', tenantId, id] }),
      queryClient.invalidateQueries({ queryKey: ['tenant', tenantId] }),
      queryClient.invalidateQueries({ queryKey: ['autodj', tenantId, id] }),
      // A deleted track leaves every playlist.
      queryClient.invalidateQueries({ queryKey: ['playlists', tenantId, id] }),
      queryClient.invalidateQueries({ queryKey: ['playlist', tenantId, id] }),
    ]);

  const update = (key: string, change: Partial<Upload>) =>
    setUploads((all) =>
      all.map((upload) => (upload.key === key ? { ...upload, ...change } : upload)),
    );

  const fail = (upload: Upload, reason: string) => {
    update(upload.key, { state: 'error', error: reason });
    setAnnouncement(t('media.announceFailed', { name: upload.file.name, reason }));
  };

  /** One file after the other: each confirm sees the storage the previous ones took. */
  async function run() {
    if (running.current) return;
    running.current = true;
    let added = false;
    while (queue.current.length > 0) {
      const upload = queue.current.shift()!;
      setAnnouncement(t('media.announceStart', { name: upload.file.name }));
      update(upload.key, { state: 'uploading', progress: 0 });
      try {
        const started = await data<TrackUpload>(
          api.POST('/api/v1/stations/{stationId}/tracks/uploads', {
            params: { path: { stationId: id } },
            body: { fileName: upload.file.name, size: upload.file.size },
          }),
        );
        await putFile(started.uploadUrl, upload.file, started.headers, (fraction) =>
          update(upload.key, { progress: Math.round(fraction * 100) }),
        );
        update(upload.key, { state: 'checking', progress: 100 });
        await data<Track>(
          api.POST('/api/v1/stations/{stationId}/tracks', {
            params: { path: { stationId: id } },
            body: { uploadId: started.trackId },
          }),
        );
        update(upload.key, { state: 'done' });
        setAnnouncement(t('media.announceDone', { name: upload.file.name }));
        added = true;
      } catch (error) {
        fail(upload, error instanceof Error ? t('media.storageFailed') : problemMessage(error));
      }
    }
    running.current = false;
    if (added) await refresh();
  }

  function add(files: FileList | File[]) {
    const storage = library.data?.storage;
    // What is already queued counts too, so the client refuses what the server would.
    let used =
      Number(storage?.usedBytes ?? 0) +
      queue.current.reduce((sum, upload) => sum + upload.file.size, 0);
    const quota = Number(storage?.quotaBytes ?? 0);
    const added: Upload[] = [];
    for (const file of Array.from(files)) {
      const upload: Upload = { key: crypto.randomUUID(), file, state: 'waiting', progress: 0 };
      const error = !isAccepted(file.name)
        ? t('media.unsupported')
        : file.size > maxTrackBytes
          ? t('media.tooLarge')
          : used + file.size > quota
            ? t('media.overQuota')
            : undefined;
      if (error) {
        added.push({ ...upload, state: 'error', error });
        setAnnouncement(t('media.announceFailed', { name: file.name, reason: error }));
        continue;
      }
      used += file.size;
      added.push(upload);
      queue.current.push(upload);
    }
    setUploads((all) => [...added, ...all]);
    void run();
  }

  const onDrop = (event: DragEvent) => {
    event.preventDefault();
    setDragging(false);
    if (event.dataTransfer.files.length > 0) add(event.dataTransfer.files);
  };

  if (station.isPending || library.isPending) return <Loading label={t('media.loading')} />;
  if (!station.data) return <Notice error={station.error} />;
  if (!library.data) return <Notice error={library.error} />;
  const { tracks, storage } = library.data;
  const quota = Number(storage.quotaBytes);
  const used = Number(storage.usedBytes);
  const available = quota > 0;

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
        title={t('media.title')}
        description={t('media.intro')}
      />
      <p className="sr-only" aria-live="polite">
        {announcement}
      </p>
      {!available ? (
        <AutoDjUpgrade title={t('media.notIncluded')} />
      ) : (
        <>
          <Alert color="warning" title={t('media.rights')}>
            {t('media.rightsDetail')}{' '}
            <Link to="/terms" className="font-semibold underline">
              {t('legal.terms')}
            </Link>
          </Alert>
          <div className="grid gap-6 lg:grid-cols-3">
            <Card title={t('media.upload')} className="lg:col-span-2">
              <div
                data-testid="dropzone"
                onDragOver={(event) => {
                  event.preventDefault();
                  setDragging(true);
                }}
                onDragLeave={() => setDragging(false)}
                onDrop={onDrop}
                className={cx(
                  'flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-8 text-center',
                  dragging ? 'border-brand bg-brand-primary' : 'border-secondary bg-secondary',
                )}
              >
                <UploadCloud02 aria-hidden="true" className="size-8 text-fg-quaternary" />
                <p className="text-sm text-tertiary">{t('media.drop')}</p>
                <Button color="secondary" size="sm" onPress={() => input.current?.click()}>
                  {t('media.choose')}
                </Button>
                <input
                  ref={input}
                  type="file"
                  multiple
                  hidden
                  data-testid="file-input"
                  accept={acceptedExtensions.join(',')}
                  onChange={(event) => {
                    if (event.target.files?.length) add(event.target.files);
                    event.target.value = '';
                  }}
                />
                <p className="text-xs text-quaternary">{t('media.limits')}</p>
              </div>
              {uploads.length > 0 && (
                <ul className="flex flex-col gap-3" aria-label={t('media.uploads')}>
                  {uploads.map((upload) => (
                    <li key={upload.key} className="flex flex-col gap-1.5 text-sm">
                      <div className="flex items-center justify-between gap-3">
                        <span className="min-w-0 truncate font-medium text-primary">
                          {upload.file.name}
                        </span>
                        <span
                          className={cx(
                            'shrink-0',
                            upload.state === 'error' ? 'text-error-primary' : 'text-tertiary',
                          )}
                        >
                          {upload.state === 'error'
                            ? upload.error
                            : upload.state === 'uploading'
                              ? t('media.uploading', { percent: upload.progress })
                              : t(`media.${upload.state}`)}
                        </span>
                      </div>
                      {upload.state !== 'error' && (
                        <ProgressBarBase
                          value={upload.progress}
                          aria-label={t('media.progressLabel', { name: upload.file.name })}
                          aria-valuetext={t('media.uploading', { percent: upload.progress })}
                        />
                      )}
                    </li>
                  ))}
                </ul>
              )}
            </Card>
            <Card title={t('media.storage')}>
              <ProgressBarBase
                value={Math.min(used, quota)}
                max={quota}
                aria-label={t('media.storageLabel')}
                aria-valuetext={t('media.storageUsed', {
                  used: formatStorage(used),
                  quota: formatStorage(quota),
                })}
              />
              <p className="text-sm text-tertiary">
                {t('media.storageUsed', { used: formatStorage(used), quota: formatStorage(quota) })}
              </p>
            </Card>
          </div>
        </>
      )}
      <Card title={t('media.tracks')} bodyClassName="p-0 md:p-0">
        {tracks.length === 0 ? (
          <div className="flex items-center gap-3 p-4 text-sm text-tertiary md:p-6">
            <MusicNote01 aria-hidden="true" className="size-5 text-fg-quaternary" />
            {t('media.empty')}
          </div>
        ) : (
          <Table aria-label={t('media.tracks')} size="sm">
            <Table.Header>
              <Table.Head id="title" label={t('media.trackTitle')} isRowHeader />
              <Table.Head id="duration" label={t('media.duration')} />
              <Table.Head id="size" label={t('media.size')} className="max-md:hidden" />
              <Table.Head id="format" label={t('media.format')} className="max-md:hidden" />
              <Table.Head id="actions">
                <span className="sr-only">{t('media.delete')}</span>
              </Table.Head>
            </Table.Header>
            <Table.Body items={tracks}>
              {(track) => (
                <Table.Row id={track.id}>
                  <Table.Cell>
                    <div className="flex flex-col">
                      <span className="font-medium text-primary">{track.title}</span>
                      <span>{track.artist || t('media.unknownArtist')}</span>
                    </div>
                  </Table.Cell>
                  <Table.Cell>{formatTrackDuration(Number(track.durationMs))}</Table.Cell>
                  <Table.Cell className="max-md:hidden">
                    {formatStorage(Number(track.sizeBytes))}
                  </Table.Cell>
                  <Table.Cell className="max-md:hidden">
                    {t(`media.formats.${track.format}`)}
                  </Table.Cell>
                  <Table.Cell className="w-12 text-right">
                    <ButtonUtility
                      color="tertiary"
                      icon={Trash01}
                      tooltip={t('media.delete')}
                      aria-label={t('media.deleteLabel', { title: track.title })}
                      onPress={() => {
                        remove.reset();
                        setToDelete(track);
                      }}
                    />
                  </Table.Cell>
                </Table.Row>
              )}
            </Table.Body>
          </Table>
        )}
      </Card>
      {toDelete && (
        <ConfirmDialog
          title={t('media.deleteTitle', { title: toDelete.title })}
          confirmLabel={t('media.delete')}
          busy={remove.isPending}
          onConfirm={() => remove.mutate(toDelete.id)}
          onCancel={() => setToDelete(null)}
        >
          <p>{t('media.deleteText')}</p>
          <Notice error={remove.error} />
        </ConfirmDialog>
      )}
    </section>
  );
}
