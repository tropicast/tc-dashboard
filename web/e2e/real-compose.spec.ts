import { expect, test, type APIRequestContext } from '@playwright/test';

const mailpitUrl = process.env.REAL_E2E_MAILPIT_URL ?? 'http://localhost:8025';

type MailpitMessage = {
  ID: string;
  To: { Address: string }[];
};

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

test.skip(process.env.REAL_E2E !== '1', 'Requires REAL_E2E=1 and a running Compose stack.');

test('signs up, confirms email, and creates a first station against Compose', async ({
  page,
  request,
}) => {
  const suffix = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
  const email = `playwright-${suffix}@example.test`;
  const slug = `playwright-${suffix}`;
  const password = 'Playwright-real-e2e-123!';

  await page.goto('/signup');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password (8 or more characters)').fill(password);
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
  await expect(page.getByRole('button', { name: 'Email sent' })).toBeVisible();

  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Create your workspace' })).toBeVisible();

  await page.getByLabel('Organization name').fill(`Playwright ${suffix}`);
  await page.getByLabel('Workspace slug').fill(slug);
  await page.getByRole('button', { name: 'Create workspace' }).click();
  await expect(page.getByRole('heading', { name: 'Create first station' })).toBeVisible();

  await page.getByLabel('Station name').fill(`Playwright ${suffix}`);
  await page.getByLabel('Station slug').fill(slug);
  await page.getByRole('button', { name: 'Create station' }).click();
  await expect(page.getByRole('heading', { name: 'Station ready' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Open stations' })).toBeVisible();
});
