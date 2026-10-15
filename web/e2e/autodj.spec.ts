import { expect, test, type Route } from '@playwright/test';

// Build a playlist from the library and turn AutoDJ on (#109), against a mocked API.
const stationId = '01927f5e-6c1a-7b3e-9a52-3f1d2c4b5a70';
const tenantId = '01927f5e-6c1a-7b3e-9a52-3f1d2c4b5a69';
const library = [
  { id: 'a', title: 'Station jingle', artist: '', durationMs: 30_000 },
  { id: 'b', title: 'Salegy Morning', artist: 'Radio Mada', durationMs: 180_000 },
];

async function json(route: Route, status: number, body?: object) {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: body ? JSON.stringify(body) : undefined,
  });
}

test('build a playlist and enable AutoDJ', async ({ page }) => {
  const playlists: { id: string; name: string; shuffle: boolean; trackIds: string[] }[] = [];
  const autodj = {
    enabled: false,
    defaultPlaylistId: null as string | null,
    available: true,
    active: false,
    scheduling: false,
    encoding: [{ format: 'mp3', bitrateKbps: 128, sampleRate: 44100, channels: 2, pinned: false }],
  };
  const full = (p: (typeof playlists)[number]) => {
    const tracks = p.trackIds.map((id) => {
      const t = library.find((track) => track.id === id)!;
      return { trackId: id, title: t.title, artist: t.artist, durationMs: t.durationMs };
    });
    const durationMs = tracks.reduce((sum, t) => sum + t.durationMs, 0);
    return { ...p, tracks, durationMs, updatedAt: '2026-10-15T10:00:00Z' };
  };

  await page.addInitScript((tenant) => localStorage.setItem('tenant-id', tenant), tenantId);
  await page.route('**/api/v1/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const body = request.postDataJSON() as Record<string, unknown> | null;
    const base = `/api/v1/stations/${stationId}`;
    const one = new RegExp(`^${base}/playlists/(\\w+)(/tracks)?$`).exec(path);
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
    if (path === base)
      return json(route, 200, {
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
      });
    if (path === `${base}/status`)
      return json(route, 200, { live: false, stale: false, listeners: 0, outputs: [] });
    if (path === `${base}/autodj` && request.method() === 'GET') return json(route, 200, autodj);
    if (path === `${base}/autodj`) {
      Object.assign(autodj, body, { active: Boolean(body?.enabled) });
      return json(route, 200, autodj);
    }
    if (path === `${base}/tracks`)
      return json(route, 200, {
        tracks: library.map((t) => ({
          ...t,
          fileName: `${t.id}.mp3`,
          format: 'Mp3',
          sizeBytes: 1000,
          createdAt: '2026-10-15T10:00:00Z',
        })),
        storage: { usedBytes: 2000, quotaBytes: 1024 ** 3 },
      });
    if (path === `${base}/playlists` && request.method() === 'GET')
      return json(
        route,
        200,
        playlists.map((p) => ({
          ...p,
          trackCount: p.trackIds.length,
          durationMs: full(p).durationMs,
          updatedAt: '2026-10-15T10:00:00Z',
        })),
      );
    if (path === `${base}/playlists`) {
      const created = {
        id: `p${playlists.length + 1}`,
        name: String(body?.name),
        shuffle: false,
        trackIds: [],
      };
      playlists.push(created);
      return json(route, 201, full(created));
    }
    if (one) {
      const playlist = playlists.find((p) => p.id === one[1])!;
      if (request.method() === 'PUT') playlist.trackIds = body?.trackIds as string[];
      return json(route, 200, full(playlist));
    }
    return json(route, 404, { title: 'Not found' });
  });

  await page.goto(`/stations/${stationId}/playlists`);
  await page.getByLabel('New playlist name').fill('Fallback');
  await page.getByRole('button', { name: 'Create' }).click();
  await expect(page.getByRole('heading', { name: 'Fallback' })).toBeVisible();

  await page.getByRole('button', { name: 'Add Salegy Morning' }).click();
  await page.getByRole('button', { name: 'Add Station jingle' }).click();
  // Keyboard: put the jingle first.
  await page.getByRole('button', { name: 'Move Station jingle up' }).focus();
  await page.keyboard.press('Enter');
  const order = page.locator('ol li');
  await expect(order.first()).toContainText('Station jingle');
  await expect(order.nth(1)).toContainText('Salegy Morning');

  await page.getByRole('link', { name: 'Radio Mada' }).click();
  const panel = page.locator('div.rounded-xl', {
    has: page.getByRole('heading', { name: 'AutoDJ' }),
  });
  await panel.getByLabel('Playlist').selectOption('p1');
  // The choice is saved first; the switch is disabled meanwhile.
  await expect(panel.getByRole('switch', { name: /Play AutoDJ/ })).toBeEnabled();
  await panel
    .getByRole('switch', { name: /Play AutoDJ when no DJ is live/ })
    .click({ force: true });
  await expect(panel.getByText('On air: AutoDJ')).toBeVisible();
  expect(autodj).toMatchObject({ enabled: true, defaultPlaylistId: 'p1' });
});
