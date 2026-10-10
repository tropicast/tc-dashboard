import { useQuery } from '@tanstack/react-query';
import { api, refreshAntiforgery } from '../api/client';
import { data } from '../lib/request';
import type { Me } from '../lib/types';

export function useMe() {
  return useQuery({
    queryKey: ['me'],
    queryFn: async () => {
      const me = await data<Me>(api.GET('/api/v1/auth/me'));
      await refreshAntiforgery();
      return me;
    },
  });
}
