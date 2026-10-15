import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import type { Station } from '../lib/types';

/** A station of the selected workspace, with the ETag its edits send as If-Match. */
export function useStation(id: string) {
  const tenantId = localStorage.getItem('tenant-id');
  return useQuery({
    queryKey: ['station', tenantId, id],
    queryFn: async () => {
      const result = await api.GET('/api/v1/stations/{id}', { params: { path: { id } } });
      if (result.error) throw result.error;
      return { station: result.data as Station, etag: result.response.headers.get('ETag') ?? '' };
    },
  });
}
