import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, test, vi } from 'vitest';
import App from './App';
import { CopyButton } from './components/atoms/CopyButton';
import i18n from './lib/i18n';
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

test('goes home after confirming the email', async () => {
  let confirmed = false;
  renderAt('/confirm-email?userId=u1&code=abc', (request) => {
    const path = new URL(request.url).pathname;
    if (path === '/api/v1/auth/confirm-email') {
      confirmed = true;
      return new Response(null, { status: 200 });
    }
    if (path === '/api/v1/auth/me') {
      return Response.json({ id: 'u1', email: 'owner@example.com', memberships: [] });
    }
    return new Response(null, { status: 204 });
  });

  fireEvent.click(await screen.findByRole('button', { name: 'Continue' }));

  expect(await screen.findByRole('heading', { name: 'Create your workspace' })).toBeInTheDocument();
  expect(confirmed).toBe(true);
});

test('translates the page content, not only the header, when French is chosen', async () => {
  localStorage.setItem('language', 'fr');
  await i18n.changeLanguage('fr');
  try {
    renderAt('/stations', (request) =>
      new URL(request.url).pathname === '/api/v1/auth/me'
        ? Response.json(signedIn)
        : Response.json({ items: [], page: 1, pageSize: 50, totalCount: 0 }),
    );
    expect(await screen.findByText('Les stations de cet espace de travail.')).toBeInTheDocument();
    expect(await screen.findByText('Aucune station pour l’instant.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Compte' })).toBeInTheDocument();
  } finally {
    await i18n.changeLanguage('en');
  }
});

test('ignores a second submit while the first request is pending', async () => {
  let posts = 0;
  let release: () => void = () => {};
  renderAt('/forgot-password', (request) => {
    if (new URL(request.url).pathname !== '/api/v1/auth/forgot-password') {
      return new Response(null, { status: 204 });
    }
    posts += 1;
    return new Promise((resolve) => {
      release = () => resolve(new Response(null, { status: 204 }));
    });
  });

  fireEvent.change(await screen.findByLabelText('Email'), {
    target: { value: 'owner@example.com' },
  });
  const form = screen.getByLabelText('Email').closest('form')!;
  fireEvent.submit(form);
  await vi.waitFor(() => expect(posts).toBe(1));
  // Enter in a one-field form submits it again: the pending request must not be repeated.
  fireEvent.submit(form);
  await new Promise((resolve) => setTimeout(resolve, 50));
  expect(posts).toBe(1);
  release();
  expect(await screen.findByRole('button', { name: 'Email sent' })).toBeInTheDocument();
});

test('does not claim a copy that failed', async () => {
  vi.stubGlobal('navigator', { ...navigator, clipboard: undefined });
  document.execCommand = vi.fn(() => false);
  render(<CopyButton value="secret" />);

  fireEvent.click(screen.getByRole('button', { name: 'Copy' }));

  await new Promise((resolve) => setTimeout(resolve, 20));
  expect(document.execCommand).toHaveBeenCalledWith('copy');
  expect(screen.getByRole('button', { name: 'Copy' })).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Copied' })).not.toBeInTheDocument();
});

test('each page has its own title, page first (#69)', async () => {
  renderAt('/login', () => Response.json({ title: 'Unauthorized' }, { status: 401 }));
  await screen.findByRole('heading', { name: 'Sign in' });
  await waitFor(() => expect(document.title).toBe('Sign in · Tropicast'));
  cleanup();
  queryClient.clear();

  renderAt('/stations', (request) =>
    new URL(request.url).pathname === '/api/v1/auth/me'
      ? Response.json(signedIn)
      : Response.json({ items: [], page: 1, pageSize: 50, totalCount: 0 }),
  );
  await waitFor(() => expect(document.title).toBe('Stations · Tropicast'));
});

test('the station usage bar has a name and a spoken value (#72)', async () => {
  renderAt('/account', (request) => {
    const path = new URL(request.url).pathname;
    if (path === '/api/v1/auth/me') return Response.json(signedIn);
    if (path === '/api/v1/tenants/current') {
      return Response.json({
        id: 't1',
        name: 'Radio',
        slug: 'radio',
        stationCount: 1,
        plan: { id: 'growth', name: 'Growth', maxStations: 3, maxListeners: 5000 },
      });
    }
    if (path === '/api/v1/billing') {
      return Response.json({
        planId: 'growth',
        status: null,
        plans: [],
        trialAvailable: false,
        canManage: false,
        enabled: false,
      });
    }
    if (path.endsWith('/members') || path === '/api/v1/auth/devices') return Response.json([]);
    return Response.json({ title: 'Not found', status: 404 }, { status: 404 });
  });

  const bar = await screen.findByRole('progressbar', { name: 'Stations used' });

  expect(bar).toHaveAttribute('aria-valuetext', '1 of 3 stations');
});

test('moves focus to the new page heading after navigating (#70)', async () => {
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
  renderAt('/stations', (request) => {
    const path = new URL(request.url).pathname;
    if (path === '/api/v1/auth/me') return Response.json(signedIn);
    if (path === '/api/v1/stations')
      return Response.json({ items: [station], page: 1, pageSize: 50, totalCount: 1 });
    if (path === '/api/v1/stations/s1') return Response.json(station);
    if (path === '/api/v1/stations/s1/status')
      return Response.json({ live: false, listeners: 0, outputs: [] });
    if (path === '/api/v1/stations/s1/credentials') return Response.json([]);
    return Response.json({ title: 'Not found', status: 404 }, { status: 404 });
  });

  fireEvent.click(await screen.findByRole('link', { name: 'Radio One' }));

  const heading = await screen.findByRole('heading', { level: 1, name: 'Radio One' });
  await waitFor(() => expect(document.activeElement).toBe(heading));
});

test('the first link skips the header and focuses the page content (#71)', async () => {
  renderAt('/stations', (request) =>
    new URL(request.url).pathname === '/api/v1/auth/me'
      ? Response.json(signedIn)
      : Response.json({ items: [], page: 1, pageSize: 50, totalCount: 0 }),
  );
  const skip = await screen.findByRole('link', { name: 'Skip to content' });
  const focusable = document.querySelectorAll('a[href], button, select, input');
  expect(focusable[0]).toBe(skip);

  fireEvent.click(skip);

  expect(document.activeElement).toBe(document.getElementById('main'));
});

test('the analytics data is also a table for screen readers (#73)', async () => {
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
    if (path === '/api/v1/stations/s1/status')
      return Response.json({ live: false, listeners: 0, outputs: [] });
    if (path === '/api/v1/stations/s1/credentials') return Response.json([]);
    if (path === '/api/v1/stations/s1/stats') {
      return Response.json({
        peakListeners: 1200,
        egressBytes: 1_534_000_000,
        listenerHours: 3.4166,
        points: [
          {
            start: '2026-10-12T10:00:00Z',
            peakListeners: 5,
            listenerHours: 0.4,
            egressBytes: 1000,
          },
          {
            start: '2026-10-12T10:05:00Z',
            peakListeners: 1200,
            listenerHours: 3,
            egressBytes: 2000,
          },
        ],
      });
    }
    return Response.json({ title: 'Not found', status: 404 }, { status: 404 });
  });

  const table = await screen.findByRole('table', { name: 'Peak listeners per period' });

  const rows = within(table).getAllByRole('row');
  expect(rows).toHaveLength(3);
  expect(within(rows[2]).getByText('1,200')).toBeInTheDocument();
  expect(screen.getByText('1.5 GB')).toBeInTheDocument();
  expect(screen.getByText('3 h 25 min')).toBeInTheDocument();
});
