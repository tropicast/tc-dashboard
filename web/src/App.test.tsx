import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, test, vi } from 'vitest';
import App from './App';
import { queryClient } from './lib/query-client';

afterEach(() => {
  // Vitest runs without globals, so Testing Library does not unmount between tests by itself.
  cleanup();
  vi.unstubAllGlobals();
  localStorage.clear();
  queryClient.clear();
});

function renderAt(path: string, fetcher: (request: Request) => Response | Promise<Response>) {
  vi.stubGlobal('fetch', vi.fn(fetcher));
  return render(
    <MemoryRouter initialEntries={[path]}>
      <App />
    </MemoryRouter>,
  );
}

test('auth guard redirects anonymous visitors to sign in', async () => {
  renderAt('/stations', () => Response.json({ title: 'Unauthorized' }, { status: 401 }));
  expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
});

test('shows tenant onboarding when account has no membership', async () => {
  renderAt('/', () => Response.json({ id: 'u1', email: 'owner@example.com', memberships: [] }));
  expect(await screen.findByRole('heading', { name: 'Create your workspace' })).toBeInTheDocument();
});

test('shows stations from API contract response', async () => {
  renderAt('/stations', (request) => {
    if (request.url.includes('/auth/me')) {
      return Response.json({
        id: 'u1',
        email: 'owner@example.com',
        memberships: [{ tenantId: 't1', tenantName: 'Radio', tenantSlug: 'radio', role: 'Owner' }],
      });
    }
    return Response.json({
      items: [
        {
          id: 's1',
          publicId: 'radio1',
          name: 'Radio One',
          slug: 'radio-one',
          description: '',
          genre: 'Music',
          country: 'MG',
          language: 'mg',
          logoUrl: null,
          website: null,
          listInDirectory: false,
          createdAt: '2026-01-01T00:00:00Z',
          listenerUrls: { mp3: 'https://example.test/live.mp3', opus: null },
        },
      ],
      page: 1,
      pageSize: 50,
      totalCount: 1,
    });
  });
  expect(await screen.findByRole('link', { name: 'Radio One' })).toBeInTheDocument();
  expect(screen.getByText('Music · MG')).toBeInTheDocument();
});

test('restores antiforgery before loading tenant state', async () => {
  const requests: string[] = [];
  renderAt('/stations', (request) => {
    const path = new URL(request.url).pathname;
    requests.push(path);
    if (path === '/api/v1/auth/me') {
      return Response.json({
        id: 'u1',
        email: 'owner@example.com',
        memberships: [{ tenantId: 't1', tenantName: 'Radio', tenantSlug: 'radio', role: 'Owner' }],
      });
    }
    if (path === '/api/v1/auth/antiforgery') return new Response(null, { status: 204 });
    if (path === '/api/v1/stations')
      return Response.json({ items: [], page: 1, pageSize: 50, totalCount: 0 });
    return Response.json({ title: 'Not found' }, { status: 404 });
  });

  await screen.findByText('No stations yet.');
  expect(requests.slice(0, 3)).toEqual([
    '/api/v1/auth/me',
    '/api/v1/auth/antiforgery',
    '/api/v1/stations',
  ]);
});

const signedIn = {
  id: 'u1',
  email: 'owner@example.com',
  memberships: [{ tenantId: 't1', tenantName: 'Radio', tenantSlug: 'radio', role: 'Owner' }],
};

test('approves a desktop sign-in from the link the app opens', async () => {
  const approved: unknown[] = [];
  renderAt('/device?code=BCDF-GHJK', async (request) => {
    const path = new URL(request.url).pathname;
    if (path === '/api/v1/auth/me') return Response.json(signedIn);
    if (path === '/api/v1/auth/device/BCDF-GHJK') {
      return Response.json({
        userCode: 'BCDF-GHJK',
        deviceName: 'Studio PC',
        createdAt: '2026-10-11T20:00:00Z',
        expiresAt: '2026-10-11T20:10:00Z',
      });
    }
    if (path === '/api/v1/auth/device/approve') {
      approved.push(await request.json());
      return new Response(null, { status: 204 });
    }
    return new Response(null, { status: 204 });
  });

  expect(await screen.findByText('Studio PC')).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Approve' }));

  expect(await screen.findByRole('status')).toHaveTextContent('it is signed in');
  expect(approved).toEqual([{ userCode: 'BCDF-GHJK' }]);
});

test('explains an unknown or expired desktop code', async () => {
  renderAt('/device?code=ZZZZ-ZZZZ', (request) => {
    const path = new URL(request.url).pathname;
    if (path === '/api/v1/auth/me') return Response.json(signedIn);
    if (path === '/api/v1/auth/device/ZZZZ-ZZZZ') {
      return Response.json({ title: 'Not found', status: 404 }, { status: 404 });
    }
    return new Response(null, { status: 204 });
  });

  expect(await screen.findByRole('alert')).toHaveTextContent('unknown, expired or already used');
});

test('comes back to the desktop sign-in page after signing in', async () => {
  let session = false;
  renderAt('/device?code=BCDF-GHJK', (request) => {
    const path = new URL(request.url).pathname;
    if (path === '/api/v1/auth/login') {
      session = true;
      return new Response(null, { status: 200 });
    }
    if (path === '/api/v1/auth/me') {
      return session
        ? Response.json(signedIn)
        : Response.json({ title: 'Unauthorized' }, { status: 401 });
    }
    if (path === '/api/v1/auth/device/BCDF-GHJK') {
      return Response.json({
        userCode: 'BCDF-GHJK',
        deviceName: 'Studio PC',
        createdAt: '2026-10-11T20:00:00Z',
        expiresAt: '2026-10-11T20:10:00Z',
      });
    }
    return new Response(null, { status: 204 });
  });

  await screen.findByRole('heading', { name: 'Sign in' });
  fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'owner@example.com' } });
  fireEvent.change(screen.getByLabelText('Password (12 or more characters)'), {
    target: { value: 'correct horse battery' },
  });
  fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

  expect(await screen.findByRole('heading', { name: 'Sign in a desktop app' })).toBeInTheDocument();
  expect(await screen.findByText('Studio PC')).toBeInTheDocument();
});

test('shows a new credential as separate connection settings', async () => {
  const station = {
    id: 's1',
    publicId: 'radio1',
    name: 'Radio One',
    slug: 'radio-one',
    description: '',
    genre: 'Music',
    country: 'MG',
    language: 'mg',
    logoUrl: null,
    website: null,
    listInDirectory: false,
    createdAt: '2026-01-01T00:00:00Z',
    listenerUrls: { mp3: 'https://listen.example.test/stations/radio1/live.mp3', opus: null },
  };
  renderAt('/stations/s1', (request) => {
    const path = new URL(request.url).pathname;
    if (path === '/api/v1/auth/me') return Response.json(signedIn);
    if (path === '/api/v1/stations/s1') return Response.json(station);
    if (path === '/api/v1/stations/s1/status') {
      return Response.json({ live: false, listeners: 0, outputs: [] });
    }
    if (path === '/api/v1/stations/s1/credentials' && request.method === 'GET')
      return Response.json([]);
    if (path === '/api/v1/stations/s1/credentials') {
      return Response.json(
        {
          credential: {
            id: 'c1',
            deviceLabel: 'BUTT',
            createdAt: '2026-10-11T23:00:00Z',
            lastUsedAt: null,
            revokedAt: null,
          },
          username: 'radio1',
          secret: 'Xr4-secret_43-characters-long-aaaaaaaaaaaa',
          ingestUrl: 'https://ingest.example.test/stations/radio1/live.mp3',
        },
        { status: 201 },
      );
    }
    return Response.json({ title: 'Not found', status: 404 }, { status: 404 });
  });

  fireEvent.change(await screen.findByLabelText('Device label'), { target: { value: 'BUTT' } });
  fireEvent.click(screen.getByRole('button', { name: 'Issue credential' }));

  const issued = await screen.findByText('Save the password now: it cannot be shown again.');
  const panel = issued.closest('div')!;
  const value = (label: string) =>
    within(panel).getByText(label).closest('div')!.querySelector('code')!.textContent;
  expect(value('Server')).toBe('ingest.example.test');
  expect(value('Port')).toBe('443');
  expect(value('Mount')).toBe('/stations/radio1/live.mp3');
  expect(value('Username')).toBe('radio1');
  expect(value('Password')).toBe('Xr4-secret_43-characters-long-aaaaaaaaaaaa');
  expect(within(panel).getAllByRole('button', { name: 'Copy' })).toHaveLength(5);
});

test('lists only active credentials and revokes one after confirmation', async () => {
  const deleted: string[] = [];
  const credentials = [
    {
      id: 'c1',
      deviceLabel: 'BUTT',
      createdAt: '2026-10-11T22:00:00Z',
      lastUsedAt: null,
      revokedAt: null,
    },
    {
      id: 'c2',
      deviceLabel: 'Old laptop',
      createdAt: '2026-10-10T22:00:00Z',
      lastUsedAt: null,
      revokedAt: '2026-10-11T08:00:00Z',
    },
  ];
  renderAt('/stations/s1', (request) => {
    const path = new URL(request.url).pathname;
    if (path === '/api/v1/auth/me') return Response.json(signedIn);
    if (path === '/api/v1/stations/s1') {
      return Response.json({
        id: 's1',
        publicId: 'radio1',
        name: 'Radio One',
        slug: 'radio-one',
        description: '',
        genre: 'Music',
        country: 'MG',
        language: 'mg',
        logoUrl: null,
        website: null,
        listInDirectory: false,
        createdAt: '2026-01-01T00:00:00Z',
        listenerUrls: { mp3: 'https://listen.example.test/stations/radio1/live.mp3', opus: null },
      });
    }
    if (path === '/api/v1/stations/s1/status') {
      return Response.json({ live: false, listeners: 0, outputs: [] });
    }
    if (path === '/api/v1/stations/s1/credentials') {
      return Response.json(credentials.filter((c) => !deleted.includes(c.id) || c.revokedAt));
    }
    if (path === '/api/v1/stations/s1/credentials/c1' && request.method === 'DELETE') {
      deleted.push('c1');
      return new Response(null, { status: 204 });
    }
    return Response.json({ title: 'Not found', status: 404 }, { status: 404 });
  });

  const revokeButton = await screen.findByRole('button', { name: 'Revoke BUTT' });
  expect(screen.queryByText(/Old laptop/)).not.toBeInTheDocument();

  fireEvent.click(revokeButton);
  const dialog = await screen.findByRole('dialog', { name: 'Revoke “BUTT”?' });
  fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  expect(deleted).toEqual([]);

  fireEvent.click(screen.getByRole('button', { name: 'Revoke BUTT' }));
  fireEvent.click(
    within(await screen.findByRole('dialog')).getByRole('button', { name: 'Revoke' }),
  );

  expect(await screen.findByText('No active credential.')).toBeInTheDocument();
  expect(deleted).toEqual(['c1']);
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
});
