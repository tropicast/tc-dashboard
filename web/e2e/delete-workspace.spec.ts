import { expect, test, type Route } from '@playwright/test';

// An Owner deletes the workspace from the Account page by typing its slug (#125), against a mocked API.
const tenantId = '01927f5e-6c1a-7b3e-9a52-3f1d2c4b5a69';

async function json(route: Route, status: number, body?: object) {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: body ? JSON.stringify(body) : undefined,
  });
}

test('delete the workspace by typing its slug', async ({ page }) => {
  let deleted = false;
  const counts = {
    stations: 1,
    credentials: 1,
    liveSessions: 3,
    statsRollups: 12,
    tracks: 4,
    playlists: 1,
    members: 1,
    invitations: 0,
    auditEntries: 6,
  };
  await page.addInitScript((tenant) => {
    if (!sessionStorage.getItem('seeded')) {
      localStorage.setItem('tenant-id', tenant);
      sessionStorage.setItem('seeded', '1');
    }
  }, tenantId);
  await page.route('**/api/v1/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === '/api/v1/auth/me')
      return json(route, 200, {
        id: 'u1',
        email: 'owner@example.com',
        memberships: deleted
          ? []
          : [{ tenantId, tenantName: 'Radio Mada', tenantSlug: 'radio-mada', role: 'Owner' }],
        termsVersion: '2026-10-14',
        termsAccepted: true,
      });
    if (path === '/api/v1/auth/antiforgery') return json(route, 204);
    if (path === '/api/v1/tenants/current/deletion')
      return json(route, 200, {
        id: tenantId,
        name: 'Radio Mada',
        slug: 'radio-mada',
        planId: 'free',
        createdAt: '2026-10-01T00:00:00Z',
        subscriptionStatus: null,
        members: [{ email: 'owner@example.com', role: 'Owner', leftWithoutWorkspace: true }],
        stations: [
          {
            publicId: 'radiomada1',
            name: 'Radio Mada',
            deleted: false,
            live: false,
            listedOnRadioBrowser: false,
          },
        ],
        counts,
        blockers: [],
      });
    if (path === '/api/v1/tenants/current' && request.method() === 'DELETE') {
      expect(request.postDataJSON()).toEqual({ confirmSlug: 'radio-mada' });
      deleted = true;
      return json(route, 200, { counts });
    }
    if (path === '/api/v1/tenants/current') return json(route, deleted ? 403 : 404, {});
    if (path === '/api/v1/auth/devices' || path === '/api/v1/tenants/current/members')
      return json(route, 200, []);
    return json(route, 404, { title: 'Not found', status: 404 });
  });

  await page.goto('/account');
  await page.getByRole('button', { name: 'Delete workspace' }).click();
  const dialog = page.getByRole('dialog');
  const confirm = dialog.getByRole('button', { name: 'Delete workspace' });
  await expect(dialog.getByText(/This deletes 1 station/)).toBeVisible();
  await dialog.getByLabel('Type radio-mada to confirm').fill('radio-mad');
  await expect(confirm).toBeDisabled();
  await dialog.getByLabel('Type radio-mada to confirm').fill('radio-mada');
  await confirm.click();

  await expect(page.getByRole('heading', { name: 'Workspace deleted' })).toBeVisible();
  await page.getByRole('button', { name: 'Continue' }).click();
  // No workspace left: the app offers to create one.
  await expect(page).toHaveURL(/\/$/);
  expect(deleted).toBe(true);
  expect(await page.evaluate(() => localStorage.getItem('tenant-id'))).toBeNull();
});
