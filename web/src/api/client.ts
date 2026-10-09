import createClient from 'openapi-fetch';
import type { paths } from './schema';

/** Typed client for the dashboard API, generated from web/openapi/openapi.json (npm run generate:api). */
export const api = createClient<paths>({
  // Same origin as the SPA: the API serves it, and Vite proxies /api in development.
  baseUrl: globalThis.location.origin,
  // Resolve fetch per call, so tests and polyfills that replace it are honoured.
  fetch: (request) => globalThis.fetch(request),
});
