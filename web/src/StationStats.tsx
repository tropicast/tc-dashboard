import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Area, AreaChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { api } from './api/client';
import type { components } from './api/schema';
import { formatDate, formatDateTime } from './lib/i18n';

export default function StationStats({ stationId }: { stationId: string }) {
  const { t } = useTranslation();
  const tenantId = localStorage.getItem('tenant-id');
  const stats = useQuery({
    queryKey: ['stats', tenantId, stationId],
    queryFn: async () => {
      const result = await api.GET('/api/v1/stations/{id}/stats', {
        params: { path: { id: stationId } },
      });
      if (result.error) throw result.error;
      return result.data as components['schemas']['StationStatsResponse'];
    },
  });
  if (stats.isPending) return <p>{t('stats.loading')}</p>;
  if (stats.isError) return <p role="alert">{t('stats.unavailable')}</p>;
  return (
    <article className="panel stats">
      <p>
        <strong>{stats.data.peakListeners}</strong> {t('stats.peak')} ·{' '}
        <strong>{Number(stats.data.egressBytes) / 1_000_000_000}</strong> {t('stats.egress')} ·{' '}
        {stats.data.listenerHours} {t('stats.hours')}
      </p>
      <ResponsiveContainer width="100%" height={240}>
        <AreaChart data={stats.data.points}>
          <XAxis dataKey="start" tickFormatter={(value) => formatDate(value)} />
          <YAxis />
          <Tooltip labelFormatter={(value) => formatDateTime(String(value))} />
          <Area
            dataKey="peakListeners"
            name={t('stats.peakListeners')}
            stroke="#7c3aed"
            fill="#c4b5fd"
          />
        </AreaChart>
      </ResponsiveContainer>
    </article>
  );
}
