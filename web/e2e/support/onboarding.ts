import { expect, type APIRequestContext, type Page } from '@playwright/test';

const mailpitUrl = process.env.REAL_E2E_MAILPIT_URL ?? 'http://localhost:8025';

type MailpitMessage = {
  ID: string;
  To: { Address: string }[];
};

export type Account = {
  email: string;
  password: string;
  slug: string;
};

/** A fresh account for one test run; the slug doubles as workspace and station slug. */
export function newAccount(prefix: string): Account {
  const suffix = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
  return {
    email: `${prefix}-${suffix}@example.test`,
    password: 'Playwright-real-e2e-123!',
    slug: `${prefix}-${suffix}`,
  };
}

async function confirmationLink(
  request: APIRequestContext,
  email: string,
): Promise<string | undefined> {
  const messagesResponse = await request.get(`${mailpitUrl}/api/v1/messages?limit=100`);
  expect(messagesResponse.ok()).toBeTruthy();

  const { messages } = (await messagesResponse.json()) as { messages: MailpitMessage[] };
  const message = messages.find(({ To }) =>
    To.some(({ Address }) => Address.toLowerCase() === email.toLowerCase()),
  );
  if (!message) return undefined;

  const detailResponse = await request.get(`${mailpitUrl}/api/v1/message/${message.ID}`);
  expect(detailResponse.ok()).toBeTruthy();
  const { Text } = (await detailResponse.json()) as { Text: string };
  return Text.match(/https?:\/\/[^\s]+\/confirm-email\?[^\s]+/)?.[0];
}

async function apiIsHealthy(request: APIRequestContext): Promise<boolean> {
  try {
    return (await request.get('/health')).ok();
  } catch {
    return false;
  }
}

/**
 * Signs up in the UI, confirms the email through Mailpit, signs in, then creates the workspace and its first
 * station. Ends on the "Station ready" step.
 */
export async function signUpWithStation(
  page: Page,
  request: APIRequestContext,
  { email, password, slug }: Account,
): Promise<void> {
  await expect.poll(() => apiIsHealthy(request), { timeout: 30_000 }).toBe(true);
  await page.goto('/signup');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password (12 or more characters)').fill(password);
  await page.getByText('I accept the Terms of Service and the Privacy Policy').click();
  await page.getByRole('button', { name: 'Create account' }).click();
  await expect(page.getByRole('button', { name: 'Check your email' })).toBeVisible();

  let link = '';
  await expect
    .poll(
      async () => {
        link = (await confirmationLink(request, email)) ?? '';
        return link;
      },
      { timeout: 30_000 },
    )
    .not.toBe('');

  const confirmation = new URL(link);
  await page.goto(`${confirmation.pathname}${confirmation.search}`);
  await expect(page.getByRole('heading', { name: 'Confirm your email' })).toBeVisible();
  await page.getByRole('button', { name: 'Continue' }).click();
  // Confirmed: the page goes home, and the guard sends this signed-out visitor to sign in (#60).
  await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();

  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Create your workspace' })).toBeVisible();

  await page.getByLabel('Organization name').fill(`Playwright ${slug}`);
  await page.getByLabel('Workspace slug').fill(slug);
  await page.getByRole('button', { name: 'Create workspace' }).click();
  await expect(page.getByRole('heading', { name: 'Create first station' })).toBeVisible();

  await page.getByLabel('Station name').fill(`Playwright ${slug}`);
  await page.getByLabel('Station slug').fill(slug);
  await page.getByRole('button', { name: 'Create station' }).click();
  await expect(page.getByRole('heading', { name: 'Station ready' })).toBeVisible();
}
