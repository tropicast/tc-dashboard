import { expect, test } from '@playwright/test';
import { newAccount, signUpWithStation } from './support/onboarding';

test.skip(process.env.REAL_E2E !== '1', 'Requires REAL_E2E=1 and a running Compose stack.');

test('signs up, confirms email, and creates a first station against Compose', async ({
  page,
  request,
}) => {
  await signUpWithStation(page, request, newAccount('playwright'));
  await expect(page.getByRole('link', { name: 'Open stations' })).toBeVisible();
});
