import { expect, test, type Page } from '@playwright/test';

// Public station page and player (#12) against a running stack with a live station:
//   REAL_E2E_PLAYER=1 PUBLIC_STATION_PATH=/listen/<tenant>/<station> npx playwright test e2e/public-player.spec.ts
// The station must be live in MP3 and Opus (two sources), its listener URLs reachable from the browser.
const path = process.env.PUBLIC_STATION_PATH ?? '';

test.skip(
  process.env.REAL_E2E_PLAYER !== '1' || !path,
  'Requires REAL_E2E_PLAYER=1 and PUBLIC_STATION_PATH.',
);
test.use({ baseURL: process.env.REAL_E2E_BASE_URL ?? 'http://localhost:8080' });

/** Records every media element that plays, so the test can read its progress. */
async function trackMedia(page: Page) {
  await page.addInitScript(() => {
    const play = HTMLMediaElement.prototype.play;
    HTMLMediaElement.prototype.play = function (this: HTMLMediaElement) {
      ((window as unknown as { media: HTMLMediaElement[] }).media ??= []).push(this);
      return play.call(this);
    };
  });
}

const playedSeconds = (page: Page) =>
  page.evaluate(() => {
    const media = (window as unknown as { media?: HTMLMediaElement[] }).media ?? [];
    return media.length ? media[media.length - 1].currentTime : 0;
  });

test('the page weighs under 100 KB before audio and plays Opus', async ({ page }) => {
  await trackMedia(page);
  let bytes = 0;
  page.on('response', async (response) => {
    if (!/live\.(mp3|opus)/.test(response.url()))
      bytes += (await response.body().catch(() => Buffer.alloc(0))).length;
  });
  await page.goto(path);
  await page.waitForLoadState('networkidle');
  expect(bytes).toBeLessThan(100_000);

  const listener = page.waitForRequest(/live\.opus/);
  await page.locator('#play').click();
  await listener;
  await expect.poll(() => playedSeconds(page), { timeout: 15_000 }).toBeGreaterThan(2);
  await expect(page.locator('#play')).toHaveAttribute('aria-pressed', 'true');
});

test('a browser without Opus plays MP3', async ({ page }) => {
  await trackMedia(page);
  await page.addInitScript(() => {
    const canPlay = HTMLMediaElement.prototype.canPlayType;
    HTMLMediaElement.prototype.canPlayType = function (this: HTMLMediaElement, type: string) {
      return type.includes('ogg') ? '' : canPlay.call(this, type);
    };
  });
  await page.goto(path);
  const listener = page.waitForRequest(/live\.mp3/);
  await page.locator('#play').click();
  await listener;
  await expect.poll(() => playedSeconds(page), { timeout: 15_000 }).toBeGreaterThan(2);
});

test('the player reconnects after the stream fails', async ({ page }) => {
  await trackMedia(page);
  let attempts = 0;
  await page.route(/live\.(mp3|opus)/, (route) =>
    ++attempts === 1 ? route.abort() : route.continue(),
  );
  await page.goto(path);
  await page.locator('#play').click();
  await expect.poll(() => playedSeconds(page), { timeout: 20_000 }).toBeGreaterThan(2);
  expect(attempts).toBeGreaterThanOrEqual(2);
});
