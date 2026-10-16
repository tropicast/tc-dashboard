import { QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, expect, test, vi } from 'vitest';
import '../../../lib/i18n';
import { queryClient } from '../../../lib/query-client';
import { RouteProvider } from '../../../providers/route-provider';
import { DeleteWorkspacePanel } from './DeleteWorkspacePanel';

const report = (blockers: string[] = []) => ({
  id: 't1',
  name: 'Radio Mada',
  slug: 'radio-mada',
  planId: 'starter',
  createdAt: '2026-01-01T00:00:00Z',
  subscriptionStatus: null,
  members: [
    { email: 'owner@example.test', role: 'Owner', leftWithoutWorkspace: false },
    { email: 'dj@example.test', role: 'Broadcaster', leftWithoutWorkspace: true },
  ],
  stations: [
    {
      publicId: 'k3m9x2p7qa',
      name: 'Radio',
      deleted: false,
      live: false,
      listedOnRadioBrowser: true,
    },
  ],
  counts: {
    stations: 1,
    credentials: 2,
    liveSessions: 4,
    statsRollups: 30,
    tracks: 12,
    playlists: 2,
    members: 2,
    invitations: 0,
    auditEntries: 9,
  },
  blockers,
});

function renderPanel(blockers: string[] = []) {
  const deletes: unknown[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (request: Request) => {
      const path = new URL(request.url).pathname;
      if (path === '/api/v1/tenants/current/deletion') return Response.json(report(blockers));
      if (path === '/api/v1/tenants/current' && request.method === 'DELETE') {
        deletes.push(await request.json());
        return Response.json({ counts: report().counts });
      }
      return Response.json({ title: 'Not found', status: 404 }, { status: 404 });
    }),
  );
  localStorage.setItem('tenant-id', 't1');
  render(
    <MemoryRouter initialEntries={['/account']}>
      <RouteProvider>
        <QueryClientProvider client={queryClient}>
          <Routes>
            <Route path="/account" element={<DeleteWorkspacePanel />} />
            <Route path="/" element={<p>Home</p>} />
          </Routes>
        </QueryClientProvider>
      </RouteProvider>
    </MemoryRouter>,
  );
  return deletes;
}

afterEach(() => {
  cleanup();
  queryClient.clear();
  localStorage.clear();
  vi.unstubAllGlobals();
});

const confirmButton = () => screen.getAllByRole('button', { name: 'Delete workspace' }).at(-1)!;

test('deletes only once the exact slug is typed, then shows what was deleted', async () => {
  const deletes = renderPanel();
  fireEvent.click(screen.getByRole('button', { name: 'Delete workspace' }));

  const field = await screen.findByLabelText('Type radio-mada to confirm');
  expect(screen.getByText(/1 member is left without any workspace/)).toBeTruthy();
  expect(screen.getByText(/1 station is listed on RadioBrowser/)).toBeTruthy();
  for (const typed of ['', 'radio', 'Radio-Mada']) {
    fireEvent.change(field, { target: { value: typed } });
    expect(confirmButton()).toBeDisabled();
  }
  fireEvent.change(field, { target: { value: 'radio-mada' } });
  await waitFor(() => expect(confirmButton()).toBeEnabled());
  fireEvent.click(confirmButton());

  expect(await screen.findByText('Workspace deleted')).toBeTruthy();
  expect(deletes).toEqual([{ confirmSlug: 'radio-mada' }]);
  expect(localStorage.getItem('tenant-id')).toBe('t1');
  fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
  expect(await screen.findByText('Home')).toBeTruthy();
  expect(localStorage.getItem('tenant-id')).toBeNull();
});

test('a blocker keeps the delete disabled even with the slug typed', async () => {
  const deletes = renderPanel(['Live']);
  fireEvent.click(screen.getByRole('button', { name: 'Delete workspace' }));

  expect(await screen.findByText('A station is live. Stop the broadcast first.')).toBeTruthy();
  fireEvent.change(screen.getByLabelText('Type radio-mada to confirm'), {
    target: { value: 'radio-mada' },
  });
  expect(confirmButton()).toBeDisabled();
  expect(deletes).toEqual([]);
});
