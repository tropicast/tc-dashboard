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
import {
  formatBytes,
  formatDate,
  formatDateTime,
  formatListeningTime,
  formatNumber,
} from './lib/i18n';

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
  const egress = Number(stats.data.egressBytes);
  const hours = Number(stats.data.listenerHours);
  // Friendly values (#75); the exact one is in the tooltip, for operators.
  const metrics = [
    { label: t('stats.peak'), value: formatNumber(Number(stats.data.peakListeners)) },
    {
      label: t('stats.egress'),
      value: formatBytes(egress),
      exact: t('stats.exactBytes', { value: formatNumber(egress) }),
    },
    {
      label: t('stats.hours'),
      value: formatListeningTime(hours),
      exact: t('stats.exactHours', { value: formatNumber(hours, 2) }),
    },
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
            <dd className="text-display-xs font-semibold text-primary" title={metric.exact}>
              {metric.value}
            </dd>
          </div>
        ))}
      </dl>
      {/* Chart colours come from Untitled UI tokens through currentColor, so they follow the theme. */}
      <div className="h-60 text-xs text-tertiary">
        <ResponsiveContainer width="100%" height="100%">
          {/* Named for screen readers (#73); the accessibility layer keeps arrow-key exploration. */}
          <AreaChart
            data={stats.data.points}
            margin={{ top: 4, right: 4, bottom: 0, left: -16 }}
            title={t('stats.chartTitle')}
            desc={t('stats.chartDescription')}
          >
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
      {/* The chart's data as text (WCAG 1.1.1): read by screen readers, not shown. */}
      <table className="sr-only">
        <caption>{t('stats.tableCaption')}</caption>
        <thead>
          <tr>
            <th scope="col">{t('stats.periodStart')}</th>
            <th scope="col">{t('stats.peakListeners')}</th>
          </tr>
        </thead>
        <tbody>
          {stats.data.points.map((point) => (
            <tr key={point.start}>
              <td>{formatDateTime(point.start)}</td>
              <td>{formatNumber(Number(point.peakListeners))}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
