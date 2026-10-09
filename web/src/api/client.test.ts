import { afterEach, expect, test, vi } from 'vitest';
import { api } from './client';

afterEach(() => {
  vi.unstubAllGlobals();
  document.cookie = 'XSRF-TOKEN=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
});

test('unsafe requests echo the antiforgery cookie, safe ones do not', async () => {
  document.cookie = 'XSRF-TOKEN=token%2Bvalue; path=/';
  const fetch = vi.fn(async (request: Request) => {
    void request;
    return new Response(null, { status: 204 });
  });
  vi.stubGlobal('fetch', fetch);

  await api.POST('/api/v1/auth/logout');
  await api.GET('/api/v1/version');

  const [post, get] = fetch.mock.calls.map(([request]) => request);
  expect(post.headers.get('X-XSRF-TOKEN')).toBe('token+value');
  expect(get.headers.get('X-XSRF-TOKEN')).toBeNull();
});
