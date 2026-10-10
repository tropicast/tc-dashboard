import { afterEach, expect, test, vi } from 'vitest';
import { api, refreshAntiforgery } from './client';

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

test('fetches an antiforgery token before an authenticated mutation', async () => {
  const fetch = vi.fn(async (request: Request) => {
    if (request.url.endsWith('/antiforgery')) {
      document.cookie = 'XSRF-TOKEN=session-token; path=/';
      return new Response(null, { status: 204 });
    }
    return new Response(null, { status: 204 });
  });
  vi.stubGlobal('fetch', fetch);

  await refreshAntiforgery();
  await api.POST('/api/v1/auth/logout');

  const [antiforgery, logout] = fetch.mock.calls.map(([request]) => request);
  expect(antiforgery.url).toMatch(/\/antiforgery$/);
  expect(logout.headers.get('X-XSRF-TOKEN')).toBe('session-token');
});
