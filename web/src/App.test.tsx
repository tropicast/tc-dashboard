import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, test, vi } from 'vitest';
import App from './App';

afterEach(() => {
  vi.unstubAllGlobals();
  localStorage.clear();
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
