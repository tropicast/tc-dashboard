import { expect, test, type Page, type Route } from '@playwright/test';

const tenantId = '01927f5e-6c1a-7b3e-9a52-3f1d2c4b5a69';

async function fulfillJson(route: Route, status: number, body?: object) {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: body ? JSON.stringify(body) : undefined,
  });
}

async function mockApi(page: Page) {
  let tenantCreated = false;

  await page.route('**/api/v1/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());

    if (url.pathname === '/api/v1/auth/register' && request.method() === 'POST')
      return fulfillJson(route, 202);

    if (url.pathname === '/api/v1/auth/me' && request.method() === 'GET') {
      return fulfillJson(route, 200, {
        id: '01927f5e-6c1a-7b3e-9a52-3f1d2c4b5a60',
        email: 'owner@example.com',
        memberships: tenantCreated
          ? [
              {
                tenantId,
                tenantName: 'Radio Mada',
                tenantSlug: 'radio-mada',
                role: 'Owner',
              },
            ]
          : [],
      });
    }

    if (url.pathname === '/api/v1/tenants' && request.method() === 'POST') {
      tenantCreated = true;
      return fulfillJson(route, 201, {
        id: tenantId,
        name: 'Radio Mada',
        slug: 'radio-mada',
        status: 'Active',
        plan: {
          id: 'free',
          name: 'Free',
          maxStations: 1,
          maxListeners: 10,
          maxBitrateKbps: 128,
          formats: ['mp3'],
          directoryListing: false,
          embed: false,
          analytics: false,
        },
        stationCount: 0,
      });
    }

    if (url.pathname === '/api/v1/stations' && request.method() === 'POST') {
      return fulfillJson(route, 201, {
        id: '01927f5e-6c1a-7b3e-9a52-3f1d2c4b5a70',
        publicId: 'radio-mada',
        name: 'Radio Mada',
        slug: 'radio-mada',
        description: 'News and salegy from Antananarivo',
        genre: 'Talk',
        country: 'MG',
        language: 'mg',
        logoUrl: null,
        website: 'https://radiomada.example',
        listInDirectory: true,
        createdAt: '2026-10-10T00:00:00Z',
        listenerUrls: {
          mp3: 'https://listen.example.test/live.mp3',
          opus: null,
        },
      });
    }

    await route.abort('failed');
  });
}

test('signs up then creates a workspace and first station', async ({ page }) => {
  await mockApi(page);

  await page.goto('/signup');
  await page.getByLabel('Email').fill('owner@example.com');
  await page.getByLabel('Password (8 or more characters)').fill('password123');
  const registration = page.waitForRequest(
    (request) => request.url().endsWith('/api/v1/auth/register') && request.method() === 'POST',
  );
  await page.getByRole('button', { name: 'Create account' }).click();
  expect((await registration).postDataJSON()).toEqual({
    email: 'owner@example.com',
    password: 'password123',
  });
  await expect(page.getByRole('button', { name: 'Check your email' })).toBeVisible();

  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Create your workspace' })).toBeVisible();
  await page.getByLabel('Organization name').fill('Radio Mada');
  await page.getByLabel('Workspace slug').fill('radio-mada');
  const tenant = page.waitForRequest(
    (request) => request.url().endsWith('/api/v1/tenants') && request.method() === 'POST',
  );
  await page.getByRole('button', { name: 'Create workspace' }).click();
  expect((await tenant).postDataJSON()).toEqual({ name: 'Radio Mada', slug: 'radio-mada' });
  await expect(page.getByRole('heading', { name: 'Create first station' })).toBeVisible();

  await page.getByLabel('Station name').fill('Radio Mada');
  await page.getByLabel('Station slug').fill('radio-mada');
  await page.getByLabel('Description').fill('News and salegy from Antananarivo');
  await page.getByLabel('Genre').fill('Talk');
  await page.getByLabel('Country').fill('MG');
  await page.getByLabel('Language').fill('mg');
  await page.getByLabel('Website').fill('https://radiomada.example');
  await page.getByLabel('List in directory').check();
  const station = page.waitForRequest(
    (request) => request.url().endsWith('/api/v1/stations') && request.method() === 'POST',
  );
  await page.getByRole('button', { name: 'Create station' }).click();
  expect((await station).postDataJSON()).toEqual({
    name: 'Radio Mada',
    slug: 'radio-mada',
    description: 'News and salegy from Antananarivo',
    genre: 'Talk',
    country: 'MG',
    language: 'mg',
    website: 'https://radiomada.example',
    listInDirectory: true,
  });
  await expect(page.getByRole('heading', { name: 'Station ready' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Open stations' })).toBeVisible();
});
