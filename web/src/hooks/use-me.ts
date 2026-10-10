import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { data } from '../lib/request';
import type { Me } from '../lib/types';

export function useMe() {
  return useQuery({ queryKey: ['me'], queryFn: () => data<Me>(api.GET('/api/v1/auth/me')) });
}
