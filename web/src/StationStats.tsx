import { useQuery } from '@tanstack/react-query';
import { Area, AreaChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { api } from './api/client';
import type { components } from './api/schema';

export default function StationStats({ stationId }: { stationId: string }) {
  const stats = useQuery({
    queryKey: ['stats', stationId],
    queryFn: async () => {
      const result = await api.GET('/api/v1/stations/{id}/stats', {
        params: { path: { id: stationId } },
      });
      if (result.error) throw result.error;
      return result.data as components['schemas']['StationStatsResponse'];
    },
  });
  if (stats.isPending) return <p>Loading stats…</p>;
  if (stats.isError) return <p role="alert">Analytics unavailable.</p>;
  return (
    <article className="panel stats">
      <p>
        <strong>{stats.data.peakListeners}</strong> peak listeners ·{' '}
        <strong>{Number(stats.data.egressBytes) / 1_000_000_000}</strong> GB egress ·{' '}
        {stats.data.listenerHours} listener-hours
      </p>
      <ResponsiveContainer width="100%" height={240}>
        <AreaChart data={stats.data.points}>
          <XAxis dataKey="start" tickFormatter={(value) => new Date(value).toLocaleDateString()} />
          <YAxis />
          <Tooltip labelFormatter={(value) => new Date(String(value)).toLocaleString()} />
          <Area dataKey="peakListeners" name="Peak listeners" stroke="#7c3aed" fill="#c4b5fd" />
        </AreaChart>
      </ResponsiveContainer>
    </article>
  );
}
