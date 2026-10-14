import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, test, vi } from 'vitest';
import App from '../App';
import i18n from '../lib/i18n';
import { queryClient } from '../lib/query-client';
import { legalTexts } from './content';
import legal from './legal.json';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  localStorage.clear();
  queryClient.clear();
  void i18n.changeLanguage('en');
});

const me = {
  id: 'u1',
  email: 'owner@example.com',
  memberships: [{ tenantId: 't1', tenantName: 'Radio', tenantSlug: 'radio', role: 'Owner' }],
  termsVersion: legal.termsVersion,
  termsAccepted: true,
};

function renderAt(path: string, fetcher: (request: Request) => Response | Promise<Response>) {
  const fetch = vi.fn(fetcher);
  vi.stubGlobal('fetch', fetch);
  render(
    <MemoryRouter initialEntries={[path]}>
      <App />
    </MemoryRouter>,
  );
  return fetch;
}

function calls(fetch: ReturnType<typeof vi.fn>, method: string, path: string) {
  return fetch.mock.calls
    .map(([request]) => request as Request)
    .filter((request) => request.method === method && new URL(request.url).pathname === path);
}

test('sign-up needs the terms ticked, then sends the acceptance', async () => {
  const fetch = renderAt('/signup', () => new Response(null, { status: 202 }));
  fireEvent.change(await screen.findByLabelText('Email'), { target: { value: 'new@example.com' } });
  fireEvent.change(screen.getByLabelText('Password (12 or more characters)'), {
    target: { value: 'a long enough passphrase' },
  });
  fireEvent.click(screen.getByRole('button', { name: 'Create account' }));
  expect(
    await screen.findByText(
      'Accept the Terms of Service and the Privacy Policy to create an account.',
    ),
  ).toBeInTheDocument();
  expect(calls(fetch, 'POST', '/api/v1/auth/register')).toHaveLength(0);
  // The form's own link, before the footer's.
  expect(screen.getAllByRole('link', { name: 'Terms of Service' })[0]).toHaveAttribute(
    'href',
    '/terms',
  );

  fireEvent.click(
    screen.getByRole('checkbox', { name: /^I accept the Terms of Service and the Privacy Policy/ }),
  );
  // React Aria ignores a second synthetic click on the same button in jsdom: submit the form itself.
  fireEvent.submit(screen.getByRole('button', { name: 'Create account' }).closest('form')!);
  await waitFor(() => expect(calls(fetch, 'POST', '/api/v1/auth/register')).toHaveLength(1));
  expect(await calls(fetch, 'POST', '/api/v1/auth/register')[0].json()).toEqual({
    email: 'new@example.com',
    password: 'a long enough passphrase',
    acceptTerms: true,
  });
});

test('the privacy policy lists every cookie and storage entry, with its purpose', async () => {
  renderAt('/privacy', () => Response.json({}, { status: 401 }));
  expect(
    await screen.findByRole('heading', { level: 1, name: 'Privacy Policy' }),
  ).toBeInTheDocument();
  const table = screen.getByRole('table', { name: 'What we store in your browser' });
  for (const entry of legal.cookies) {
    const row = within(table).getByRole('rowheader', { name: entry.name }).closest('tr')!;
    expect(row).toHaveTextContent(legalTexts.en.storage[entry.name]);
    expect(row).toHaveTextContent(legalTexts.en.lifetimes[entry.lifetime]);
  }
  expect(screen.getByRole('note')).toHaveTextContent('Draft');
});

test('every storage entry has a purpose and duration in both languages', () => {
  for (const texts of [legalTexts.en, legalTexts.fr]) {
    for (const entry of legal.cookies) {
      expect(texts.storage[entry.name], entry.name).toBeTruthy();
      expect(texts.lifetimes[entry.lifetime], entry.lifetime).toBeTruthy();
    }
  }
});

test('the terms page follows the interface language', async () => {
  await i18n.changeLanguage('fr');
  renderAt('/terms', () => Response.json({}, { status: 401 }));
  expect(
    await screen.findByRole('heading', { level: 1, name: 'Conditions d’utilisation' }),
  ).toBeInTheDocument();
  expect(screen.getByRole('heading', { name: legalTexts.fr.terms[0].heading })).toBeInTheDocument();
});

test('sign-in and dashboard pages link to the legal pages', async () => {
  renderAt('/login', () => Response.json({}, { status: 401 }));
  const legalNav = await screen.findByRole('navigation', { name: 'Legal' });
  expect(within(legalNav).getByRole('link', { name: 'Privacy Policy' })).toHaveAttribute(
    'href',
    '/privacy',
  );
  expect(within(legalNav).getByRole('link', { name: 'Cookies' })).toHaveAttribute(
    'href',
    '/privacy#cookies',
  );
});

test('a user who has not accepted the current terms accepts them before going on', async () => {
  let accepted = false;
  const fetch = renderAt('/', (request) => {
    const path = new URL(request.url).pathname;
    if (path === '/api/v1/auth/me') return Response.json({ ...me, termsAccepted: accepted });
    if (path === '/api/v1/auth/terms') {
      accepted = true;
      return new Response(null, { status: 204 });
    }
    return Response.json({ items: [], page: 1, pageSize: 20, total: 0 });
  });
  expect(
    await screen.findByRole('heading', { name: 'Our terms have changed' }),
  ).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Accept and continue' }));
  await waitFor(() =>
    expect(
      screen.queryByRole('heading', { name: 'Our terms have changed' }),
    ).not.toBeInTheDocument(),
  );
  expect(await calls(fetch, 'POST', '/api/v1/auth/terms')[0].json()).toEqual({
    version: legal.termsVersion,
  });
});

test('deletes the account after confirming with the password', async () => {
  const fetch = renderAt('/account', (request) => {
    const path = new URL(request.url).pathname;
    if (path === '/api/v1/auth/me') return Response.json(me);
    if (path === '/api/v1/auth/account') return new Response(null, { status: 204 });
    if (path.endsWith('/members') || path === '/api/v1/auth/devices') return Response.json([]);
    return Response.json({ title: 'Not found', status: 404 }, { status: 404 });
  });
  fireEvent.click(await screen.findByRole('button', { name: 'Delete account' }));
  const dialog = await screen.findByRole('dialog', { name: 'Delete your account?' });
  fireEvent.change(within(dialog).getByLabelText('Your password'), {
    target: { value: 'my secret passphrase' },
  });
  fireEvent.click(within(dialog).getByRole('button', { name: 'Delete account' }));
  await waitFor(() => expect(calls(fetch, 'DELETE', '/api/v1/auth/account')).toHaveLength(1));
  expect(await calls(fetch, 'DELETE', '/api/v1/auth/account')[0].json()).toEqual({
    password: 'my secret passphrase',
  });
});
