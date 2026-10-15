import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { BadgeWithDot } from '@/components/base/badges/badges';
import { NativeSelect } from '@/components/base/select/select-native';
import { Toggle } from '@/components/base/toggle/toggle';
import { api } from '../../../api/client';
import { Alert } from '../../atoms/Alert';
import { Notice } from '../../atoms/Notice';
import { Card } from '../../molecules/Card';
import { data } from '../../../lib/request';
import type { AutoDj, PlaylistSummary } from '../../../lib/types';

/** The station's AutoDJ (#109): on or off, its playlist, and what is on air now. */
export function AutoDjPanel({ stationId, live }: { stationId: string; live?: boolean }) {
  const { t } = useTranslation();
  const tenantId = localStorage.getItem('tenant-id');
  const queryClient = useQueryClient();
  const autoDj = useQuery({
    queryKey: ['autodj', tenantId, stationId],
    queryFn: () =>
      data<AutoDj>(
        api.GET('/api/v1/stations/{stationId}/autodj', { params: { path: { stationId } } }),
      ),
  });
  const playlists = useQuery({
    queryKey: ['playlists', tenantId, stationId],
    queryFn: () =>
      data<PlaylistSummary[]>(
        api.GET('/api/v1/stations/{stationId}/playlists', { params: { path: { stationId } } }),
      ),
    enabled: autoDj.data?.available === true,
  });
  const update = useMutation({
    mutationFn: (body: { enabled: boolean; defaultPlaylistId: string | null }) =>
      data<AutoDj>(
        api.PUT('/api/v1/stations/{stationId}/autodj', { params: { path: { stationId } }, body }),
      ),
    onSuccess: (result) => queryClient.setQueryData(['autodj', tenantId, stationId], result),
  });
  if (!autoDj.data) return <Notice error={autoDj.error} />;
  const settings =
    update.isPending && update.variables ? { ...autoDj.data, ...update.variables } : autoDj.data;
  const links = (
    <div className="flex flex-wrap gap-4 text-sm font-semibold">
      <Link to={`/stations/${stationId}/playlists`} className="text-brand-secondary underline">
        {t('autodj.editPlaylists')}
      </Link>
      <Link to={`/stations/${stationId}/library`} className="text-brand-secondary underline">
        {t('media.title')}
      </Link>
    </div>
  );
  if (!autoDj.data.available) {
    return (
      <Card title={t('autodj.title')}>
        <Alert color="gray" title={t('autodj.notIncluded')}>
          <Link to="/account" className="font-semibold underline">
            {t('media.upgrade')}
          </Link>
        </Alert>
      </Card>
    );
  }
  const source = live
    ? { color: 'success' as const, label: t('autodj.sourceLive') }
    : autoDj.data.active
      ? { color: 'brand' as const, label: t('autodj.sourceAutoDj') }
      : { color: 'gray' as const, label: t('autodj.sourceNone') };
  const options = [
    { label: t('autodj.choosePlaylist'), value: '' },
    ...(playlists.data ?? []).map((playlist) => ({
      label: t('autodj.playlistOption', {
        name: playlist.name,
        count: Number(playlist.trackCount),
      }),
      value: playlist.id,
      disabled: Number(playlist.trackCount) === 0,
    })),
  ];
  const encoding = autoDj.data.encoding
    .map((e) =>
      t('autodj.encodingItem', {
        format: e.format === 'mp3' ? 'MP3' : 'Opus',
        bitrate: e.bitrateKbps,
        rate: new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(
          Number(e.sampleRate) / 1000,
        ),
      }),
    )
    .join(', ');
  return (
    <Card
      title={t('autodj.title')}
      description={t('autodj.intro')}
      actions={
        <BadgeWithDot type="pill-color" color={source.color} size="md">
          {source.label}
        </BadgeWithDot>
      }
    >
      <Toggle
        label={t('autodj.enable')}
        hint={t('autodj.enableHint')}
        isSelected={settings.enabled}
        isDisabled={update.isPending}
        onChange={(enabled) =>
          update.mutate({ enabled, defaultPlaylistId: autoDj.data.defaultPlaylistId ?? null })
        }
      />
      <NativeSelect
        label={t('autodj.playlist')}
        options={options}
        value={settings.defaultPlaylistId ?? ''}
        disabled={update.isPending}
        onChange={(event) =>
          update.mutate({
            enabled: autoDj.data.enabled,
            defaultPlaylistId: event.target.value || null,
          })
        }
      />
      {playlists.data?.length === 0 && (
        <p className="text-sm text-tertiary">{t('autodj.noPlaylist')}</p>
      )}
      <Notice error={update.error} />
      <p className="text-sm text-tertiary">{t('autodj.encoding', { encoding })}</p>
      {links}
    </Card>
  );
}
