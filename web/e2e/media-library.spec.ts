import { expect, test, type Route } from '@playwright/test';

// The media library against a mocked API and bucket (#108): drop-in upload with progress, then the track appears.
const stationId = '01927f5e-6c1a-7b3e-9a52-3f1d2c4b5a70';
const tenantId = '01927f5e-6c1a-7b3e-9a52-3f1d2c4b5a69';
const GiB = 1024 ** 3;

async function json(route: Route, status: number, body?: object, headers?: Record<string, string>) {
  await route.fulfill({
    status,
    contentType: 'application/json',
    headers,
    body: body ? JSON.stringify(body) : undefined,
  });
}

test('upload a track to the media library', async ({ page }) => {
  const tracks: object[] = [];
  let put: { contentType?: string; size: number } | undefined;

  await page.addInitScript((tenant) => localStorage.setItem('tenant-id', tenant), tenantId);
  await page.route('https://bucket.example.test/**', async (route) => {
    const request = route.request();
    if (request.method() === 'OPTIONS') {
      return route.fulfill({
        status: 200,
        headers: {
          'access-control-allow-origin': '*',
          'access-control-allow-methods': 'PUT',
          'access-control-allow-headers': 'content-type',
        },
      });
    }
    put = {
      contentType: request.headers()['content-type'],
      size: request.postDataBuffer()?.length ?? 0,
    };
    return route.fulfill({ status: 200, headers: { 'access-control-allow-origin': '*' } });
  });
  await page.route('**/api/v1/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === '/api/v1/auth/me')
      return json(route, 200, {
        id: 'u1',
        email: 'owner@example.com',
        memberships: [
          { tenantId, tenantName: 'Radio Mada', tenantSlug: 'radio-mada', role: 'Owner' },
        ],
        termsVersion: '2026-10-14',
        termsAccepted: true,
      });
    if (path === '/api/v1/auth/antiforgery') return json(route, 204);
    if (path === `/api/v1/stations/${stationId}`)
      return json(
        route,
        200,
        {
          id: stationId,
          publicId: 'radiomada1',
          name: 'Radio Mada',
          slug: 'radio-mada',
          description: '',
          genre: '',
          country: null,
          language: null,
          logoUrl: null,
          website: null,
          listInDirectory: false,
          createdAt: '2026-10-10T00:00:00Z',
          listenerUrls: { mp3: 'https://listen.example.test/live.mp3', opus: null },
        },
        { ETag: '"1"' },
      );
    if (path === `/api/v1/stations/${stationId}/tracks` && request.method() === 'GET')
      return json(route, 200, { tracks, storage: { usedBytes: 0, quotaBytes: GiB } });
    if (path === `/api/v1/stations/${stationId}/tracks/uploads`)
      return json(route, 200, {
        trackId: 'up1',
        uploadUrl: `https://bucket.example.test/stations/${stationId}/tracks/up1?X-Amz-Signature=x`,
        headers: { 'Content-Type': 'audio/mpeg' },
        expiresAt: '2026-10-15T11:00:00Z',
      });
    if (path === `/api/v1/stations/${stationId}/tracks` && request.method() === 'POST') {
      const track = {
        id: 'tr1',
        fileName: 'salegy.mp3',
        title: 'Salegy Morning',
        artist: 'Radio Mada',
        format: 'Mp3',
        durationMs: 125_000,
        sizeBytes: 4096,
        createdAt: '2026-10-15T10:00:00Z',
      };
      tracks.push(track);
      return json(route, 201, track);
    }
    return json(route, 404, { title: 'Not found' });
  });

  await page.goto(`/stations/${stationId}/library`);
  await expect(page.getByRole('heading', { name: 'Media library' })).toBeVisible();
  await expect(page.getByText('No tracks yet. Upload some to build a playlist.')).toBeVisible();

  await page.getByTestId('file-input').setInputFiles({
    name: 'salegy.mp3',
    mimeType: 'audio/mpeg',
    buffer: Buffer.alloc(4096),
  });

  await expect(page.getByRole('row', { name: /Salegy Morning/ })).toBeVisible();
  await expect(page.getByText('Added', { exact: true })).toBeVisible();
  expect(put).toEqual({ contentType: 'audio/mpeg', size: 4096 });
  await expect(page.getByRole('row', { name: /Salegy Morning/ }).getByText('2:05')).toBeVisible();
});
