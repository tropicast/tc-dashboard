import createClient, { type Middleware } from 'openapi-fetch';
import type { paths } from './schema';

const unsafeMethods = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

/** Reads the readable XSRF-TOKEN cookie set by GET /api/v1/auth/antiforgery. */
function antiforgeryToken(): string | undefined {
  const match = /(?:^|;\s*)XSRF-TOKEN=([^;]+)/.exec(globalThis.document?.cookie ?? '');
  return match ? decodeURIComponent(match[1]) : undefined;
}

/** Echoes the antiforgery token on unsafe requests; the API requires it with the session cookie. */
export const antiforgery: Middleware = {
  onRequest({ request }) {
    const token = antiforgeryToken();
    if (token && unsafeMethods.has(request.method)) {
      request.headers.set('X-XSRF-TOKEN', token);
    }
    return request;
  },
};

/** Keeps API requests scoped to the tenant selected by the signed-in account. */
export const tenantScope: Middleware = {
  onRequest({ request }) {
    const tenantId = globalThis.localStorage?.getItem('tenant-id');
    if (tenantId) request.headers.set('X-Tenant-Id', tenantId);
    return request;
  },
};

/** Typed client for the dashboard API, generated from web/openapi/openapi.json (npm run generate:api). */
export const api = createClient<paths>({
  // Same origin as the SPA: the API serves it, and Vite proxies /api in development.
  baseUrl: globalThis.location.origin,
  // Resolve fetch per call, so tests and polyfills that replace it are honoured.
  fetch: (request) => globalThis.fetch(request),
  credentials: 'same-origin',
});
api.use(antiforgery);
api.use(tenantScope);
