import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  Area,
  AreaChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import { Loading } from './components/molecules/Loading';
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
  if (stats.isPending) return <Loading label={t('stats.loading')} />;
  if (stats.isError) {
    return (
      <p role="alert" className="text-sm text-tertiary">
        {t('stats.unavailable')}
      </p>
    );
  }
  const metrics = [
    { label: t('stats.peak'), value: stats.data.peakListeners },
    { label: t('stats.egress'), value: Number(stats.data.egressBytes) / 1_000_000_000 },
    { label: t('stats.hours'), value: stats.data.listenerHours },
  ];
  return (
    <div className="flex flex-col gap-6">
      <dl className="grid gap-4 sm:grid-cols-3">
        {metrics.map((metric) => (
          <div
            key={metric.label}
            className="flex flex-col gap-2 rounded-xl p-4 ring-1 ring-secondary"
          >
            <dt className="text-sm font-medium text-tertiary first-letter:uppercase">
              {metric.label}
            </dt>
            <dd className="text-display-xs font-semibold text-primary">{metric.value}</dd>
          </div>
        ))}
      </dl>
      {/* Chart colours come from Untitled UI tokens through currentColor, so they follow the theme. */}
      <div className="h-60 text-xs text-tertiary">
        <ResponsiveContainer width="100%" height="100%">
          <AreaChart data={stats.data.points} margin={{ top: 4, right: 4, bottom: 0, left: -16 }}>
            <CartesianGrid vertical={false} stroke="currentColor" strokeOpacity={0.15} />
            <XAxis
              dataKey="start"
              tickFormatter={(value) => formatDate(value)}
              tick={{ fill: 'currentColor' }}
              axisLine={false}
              tickLine={false}
            />
            <YAxis tick={{ fill: 'currentColor' }} axisLine={false} tickLine={false} />
            <Tooltip
              labelFormatter={(value) => formatDateTime(String(value))}
              contentStyle={{
                background: 'var(--color-bg-primary)',
                border: '1px solid var(--color-border-secondary)',
                borderRadius: 8,
                color: 'var(--color-text-primary)',
              }}
            />
            <Area
              dataKey="peakListeners"
              name={t('stats.peakListeners')}
              className="text-fg-brand-primary"
              stroke="currentColor"
              strokeWidth={2}
              fill="currentColor"
              fillOpacity={0.15}
            />
          </AreaChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}
