import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { BadgeWithDot } from '@/components/base/badges/badges';
import { NativeSelect } from '@/components/base/select/select-native';
import { Toggle } from '@/components/base/toggle/toggle';
import { useTenantRole } from '@/hooks/use-me';
import { api } from '../../../api/client';
import { AutoDjUpgrade } from '../../atoms/AutoDjUpgrade';
import { Notice } from '../../atoms/Notice';
import { Card } from '../../molecules/Card';
import { data } from '../../../lib/request';
import type { AutoDj, PlaylistSummary } from '../../../lib/types';

/**
 * The station's AutoDJ (#109): on or off, its playlist, and what is on air now, as the streaming node reports it.
 * `onAir` is null while the station's status is unknown (loading, failed or stale).
 */
/** What a station plays now: a live DJ, AutoDJ, or nothing. */
export type OnAir = 'live' | 'autoDj' | 'none';

export function AutoDjPanel({ stationId, onAir }: { stationId: string; onAir: OnAir | null }) {
  const { t } = useTranslation();
  const tenantId = localStorage.getItem('tenant-id');
  const queryClient = useQueryClient();
  const role = useTenantRole();
  // PUT /autodj needs an Admin: others see the settings, read-only.
  const canEdit = role === 'Owner' || role === 'Admin';
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
    onSuccess: async (result) => {
      queryClient.setQueryData(['autodj', tenantId, stationId], result);
      // What is on air comes from the streaming node: ask it again rather than wait for the next poll.
      await queryClient.invalidateQueries({ queryKey: ['status', tenantId, stationId] });
    },
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
        <AutoDjUpgrade title={t('autodj.notIncluded')} />
      </Card>
    );
  }
  const source =
    onAir === null
      ? { color: 'gray' as const, label: t('autodj.sourceUnknown') }
      : onAir === 'live'
        ? { color: 'success' as const, label: t('autodj.sourceLive') }
        : onAir === 'autoDj'
          ? { color: 'brand' as const, label: t('autodj.sourceAutoDj') }
          : { color: 'gray' as const, label: t('autodj.sourceNone') };
  const playable = (playlists.data ?? []).filter((playlist) => Number(playlist.trackCount) > 0);
  // The server refuses AutoDJ on without a playlist that has tracks: offer only what it accepts.
  const canTurnOn = playable.some((playlist) => playlist.id === settings.defaultPlaylistId);
  const options = [
    // No playlist only while AutoDJ is off.
    { label: t('autodj.choosePlaylist'), value: '', disabled: settings.enabled },
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
        isDisabled={!canEdit || update.isPending || (!settings.enabled && !canTurnOn)}
        onChange={(enabled) =>
          update.mutate({ enabled, defaultPlaylistId: autoDj.data.defaultPlaylistId ?? null })
        }
      />
      <NativeSelect
        label={t('autodj.playlist')}
        options={options}
        value={settings.defaultPlaylistId ?? ''}
        disabled={!canEdit || update.isPending}
        onChange={(event) =>
          update.mutate({
            enabled: autoDj.data.enabled,
            defaultPlaylistId: event.target.value || null,
          })
        }
      />
      {!canEdit && role && <p className="text-sm text-tertiary">{t('autodj.adminOnly')}</p>}
      {canEdit && playlists.data && playable.length === 0 && (
        <p className="text-sm text-tertiary">{t('autodj.noPlaylist')}</p>
      )}
      {canEdit && playable.length > 0 && !settings.enabled && !canTurnOn && (
        <p className="text-sm text-tertiary">{t('autodj.choosePlaylistFirst')}</p>
      )}
      <Notice error={playlists.error ?? update.error} />
      <p className="text-sm text-tertiary">{t('autodj.encoding', { encoding })}</p>
      {links}
    </Card>
  );
}
