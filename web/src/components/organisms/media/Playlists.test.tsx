import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import App from '../../../App';
import { queryClient } from '../../../lib/query-client';

const me = {
  id: 'u1',
  email: 'owner@example.com',
  memberships: [{ tenantId: 't1', tenantName: 'Radio', tenantSlug: 'radio', role: 'Owner' }],
  termsVersion: '2026-10-14',
  termsAccepted: true,
};
const station = {
  id: 's1',
  publicId: 'radio1',
  name: 'Radio One',
  slug: 'radio-one',
  description: '',
  genre: '',
  country: null,
  language: null,
  logoUrl: null,
  website: null,
  listInDirectory: false,
  createdAt: '2026-01-01T00:00:00Z',
  listenerUrls: { mp3: 'https://listen.test/live.mp3', opus: null },
};
const library = [
  { id: 'a', title: 'Jingle', artist: '', durationMs: 30_000 },
  { id: 'b', title: 'Morning', artist: 'Radio Mada', durationMs: 180_000 },
  { id: 'c', title: 'Night', artist: '', durationMs: 240_000 },
];

interface Call {
  method: string;
  path: string;
  body?: Record<string, unknown>;
}

/** A small in-memory API: one station, its library, playlists and AutoDJ. */
function fakeApi(
  options: {
    available?: boolean;
    live?: boolean;
    deleteConflict?: boolean;
    enableConflict?: boolean;
  } = {},
) {
  const calls: Call[] = [];
  let playlists = [{ id: 'p1', name: 'Fallback', shuffle: false, trackIds: ['a', 'b'] }];
  const autodj = {
    enabled: false,
    defaultPlaylistId: null as string | null,
    available: options.available ?? true,
    active: false,
    scheduling: false,
    encoding: [
      { format: 'mp3', bitrateKbps: 128, sampleRate: 44100, channels: 2, pinned: true },
      { format: 'opus', bitrateKbps: 64, sampleRate: 48000, channels: 2, pinned: false },
    ],
  };
  const full = (p: (typeof playlists)[number]) => {
    const tracks = p.trackIds.map((id) => {
      const track = library.find((t) => t.id === id)!;
      return {
        trackId: id,
        title: track.title,
        artist: track.artist,
        durationMs: track.durationMs,
      };
    });
    return {
      id: p.id,
      name: p.name,
      shuffle: p.shuffle,
      tracks,
      durationMs: tracks.reduce((sum, t) => sum + t.durationMs, 0),
      updatedAt: '2026-10-15T10:00:00Z',
    };
  };
  vi.stubGlobal(
    'fetch',
    vi.fn(async (request: Request) => {
      const path = new URL(request.url).pathname;
      const text = request.method === 'GET' ? '' : await request.clone().text();
      const body = text ? (JSON.parse(text) as Record<string, unknown>) : undefined;
      calls.push({ method: request.method, path, body });
      const playlistMatch = /^\/api\/v1\/stations\/s1\/playlists\/(\w+)(\/tracks)?$/.exec(path);
      if (path === '/api/v1/auth/me') return Response.json(me);
      if (path === '/api/v1/auth/antiforgery') return new Response(null, { status: 204 });
      if (path === '/api/v1/stations/s1')
        return Response.json(station, { headers: { ETag: '"1"' } });
      if (path === '/api/v1/stations/s1/status')
        return Response.json({
          live: options.live ?? false,
          stale: false,
          listeners: 0,
          outputs: [],
        });
      if (path === '/api/v1/stations/s1/autodj' && request.method === 'GET')
        return Response.json(autodj);
      if (path === '/api/v1/stations/s1/autodj' && request.method === 'PUT') {
        if (options.enableConflict && body?.enabled)
          return Response.json(
            {
              title: 'Choose a default playlist with at least one track before turning AutoDJ on.',
            },
            { status: 409 },
          );
        autodj.enabled = Boolean(body?.enabled);
        autodj.defaultPlaylistId = (body?.defaultPlaylistId as string | null) ?? null;
        autodj.active = autodj.enabled;
        return Response.json(autodj);
      }
      if (path === '/api/v1/stations/s1/tracks')
        return Response.json({
          tracks: library.map((t) => ({
            ...t,
            fileName: `${t.title}.mp3`,
            format: 'Mp3',
            sizeBytes: 1000,
            createdAt: '2026-10-15T10:00:00Z',
          })),
          storage: { usedBytes: 3000, quotaBytes: 1024 ** 3 },
        });
      if (path === '/api/v1/stations/s1/playlists' && request.method === 'GET')
        return Response.json(
          playlists.map((p) => {
            const value = full(p);
            return {
              id: p.id,
              name: p.name,
              shuffle: p.shuffle,
              trackCount: p.trackIds.length,
              durationMs: value.durationMs,
              updatedAt: value.updatedAt,
            };
          }),
        );
      if (path === '/api/v1/stations/s1/playlists' && request.method === 'POST') {
        const created = {
          id: `p${playlists.length + 1}`,
          name: String(body?.name),
          shuffle: false,
          trackIds: [],
        };
        playlists = [...playlists, created];
        return Response.json(full(created), { status: 201 });
      }
      if (playlistMatch) {
        const playlist = playlists.find((p) => p.id === playlistMatch[1]);
        if (!playlist) return Response.json({ title: 'Not found' }, { status: 404 });
        if (request.method === 'GET') return Response.json(full(playlist));
        if (request.method === 'PUT') playlist.trackIds = body?.trackIds as string[];
        if (request.method === 'PATCH') Object.assign(playlist, body);
        if (request.method === 'DELETE') {
          if (options.deleteConflict)
            return Response.json(
              {
                title:
                  'AutoDJ plays this playlist: turn AutoDJ off or choose another playlist before deleting it.',
              },
              { status: 409 },
            );
          playlists = playlists.filter((p) => p !== playlist);
          return new Response(null, { status: 204 });
        }
        return Response.json(full(playlist));
      }
      return Response.json({ title: 'Not found' }, { status: 404 });
    }),
  );
  return calls;
}

function renderAt(path: string) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <App />
    </MemoryRouter>,
  );
}

const lastPut = (calls: Call[], suffix: string) =>
  calls
    .filter((c) => (c.method === 'PUT' || c.method === 'PATCH') && c.path.endsWith(suffix))
    .at(-1)?.body;

beforeEach(() => localStorage.setItem('tenant-id', 't1'));

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  localStorage.clear();
  queryClient.clear();
});

test('the station page turns AutoDJ on with a playlist and shows it on air', async () => {
  const calls = fakeApi();
  renderAt('/stations/s1');
  const panel = (await screen.findByRole('heading', { name: 'AutoDJ' })).closest(
    'div.rounded-xl',
  ) as HTMLElement;
  expect(within(panel).getByText('Off air')).toBeInTheDocument();
  expect(
    within(panel).getByText(
      'AutoDJ encodes like your live stream: MP3 128 kbps, 44.1 kHz, Opus 64 kbps, 48 kHz.',
    ),
  ).toBeInTheDocument();

  fireEvent.change(await within(panel).findByLabelText('Playlist'), { target: { value: 'p1' } });
  await waitFor(() =>
    expect(lastPut(calls, '/autodj')).toEqual({ enabled: false, defaultPlaylistId: 'p1' }),
  );
  fireEvent.click(within(panel).getByRole('switch', { name: /Play AutoDJ when no DJ is live/ }));

  await waitFor(() =>
    expect(lastPut(calls, '/autodj')).toEqual({ enabled: true, defaultPlaylistId: 'p1' }),
  );
  expect(await within(panel).findByText('On air: AutoDJ')).toBeInTheDocument();
  expect(within(panel).getByRole('switch', { name: /Play AutoDJ/ })).toBeChecked();
});

test('a live DJ is shown as the source, and a refusal shows its reason', async () => {
  fakeApi({ live: true, enableConflict: true });
  renderAt('/stations/s1');
  const panel = (await screen.findByRole('heading', { name: 'AutoDJ' })).closest(
    'div.rounded-xl',
  ) as HTMLElement;
  expect(await within(panel).findByText('On air: live DJ')).toBeInTheDocument();

  fireEvent.click(within(panel).getByRole('switch', { name: /Play AutoDJ/ }));

  expect(
    await within(panel).findByText(
      'Choose a default playlist with at least one track before turning AutoDJ on.',
    ),
  ).toBeInTheDocument();
  expect(within(panel).getByRole('switch', { name: /Play AutoDJ/ })).not.toBeChecked();
});

test('plans without AutoDJ get an upgrade prompt on the station and playlists pages', async () => {
  fakeApi({ available: false });
  renderAt('/stations/s1/playlists');
  expect(await screen.findByText('AutoDJ is included from the Starter plan.')).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'See the plans' })).toHaveAttribute('href', '/account');
  expect(screen.queryByLabelText('New playlist name')).not.toBeInTheDocument();
});

test('builds a playlist: add from the library, reorder by keyboard, remove, shuffle, rename', async () => {
  const calls = fakeApi();
  renderAt('/stations/s1/playlists');
  const tracks = () => screen.getAllByRole('listitem').filter((item) => item.closest('ol'));
  await waitFor(() =>
    expect(
      tracks().map((item) => within(item).getByText(/Jingle|Morning|Night/).textContent),
    ).toEqual(['Jingle', 'Morning']),
  );

  fireEvent.click(screen.getByRole('button', { name: 'Add Night' }));
  await waitFor(() => expect(lastPut(calls, '/tracks')).toEqual({ trackIds: ['a', 'b', 'c'] }));

  const down = await screen.findByRole('button', { name: 'Move Jingle down' });
  down.focus();
  fireEvent.click(down);
  await waitFor(() => expect(lastPut(calls, '/tracks')).toEqual({ trackIds: ['b', 'a', 'c'] }));
  await waitFor(() =>
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Move Jingle down' })),
  );
  expect(screen.getByText('Jingle moved to position 2 of 3.')).toBeInTheDocument();

  fireEvent.click(screen.getByRole('button', { name: 'Remove Morning from the playlist' }));
  await waitFor(() => expect(lastPut(calls, '/tracks')).toEqual({ trackIds: ['a', 'c'] }));

  fireEvent.click(screen.getByRole('switch', { name: /Shuffle/ }));
  await waitFor(() => expect(lastPut(calls, '/playlists/p1')).toEqual({ shuffle: true }));

  const name = screen.getByLabelText('Name');
  fireEvent.change(name, { target: { value: 'Night rotation' } });
  fireEvent.click(screen.getByRole('button', { name: 'Rename' }));
  await waitFor(() => expect(lastPut(calls, '/playlists/p1')).toEqual({ name: 'Night rotation' }));
  expect(await screen.findByRole('heading', { name: 'Night rotation' })).toBeInTheDocument();
});

test('creates a playlist and selects it', async () => {
  const calls = fakeApi();
  renderAt('/stations/s1/playlists');
  fireEvent.change(await screen.findByLabelText('New playlist name'), {
    target: { value: 'Weekend' },
  });
  fireEvent.click(screen.getByRole('button', { name: 'Create' }));

  expect(await screen.findByRole('heading', { name: 'Weekend' })).toBeInTheDocument();
  expect(calls.find((c) => c.method === 'POST' && c.path.endsWith('/playlists'))?.body).toEqual({
    name: 'Weekend',
    shuffle: false,
  });
  expect(screen.getByText('No tracks yet: add some from the library below.')).toBeInTheDocument();
});

test('deleting the playlist AutoDJ plays shows why it is refused', async () => {
  fakeApi({ deleteConflict: true });
  renderAt('/stations/s1/playlists');
  fireEvent.click(await screen.findByRole('button', { name: 'Delete playlist' }));
  const dialog = await screen.findByRole('dialog');
  fireEvent.click(within(dialog).getByRole('button', { name: 'Delete playlist' }));

  expect(
    await within(dialog).findByText(
      'AutoDJ plays this playlist: turn AutoDJ off or choose another playlist before deleting it.',
    ),
  ).toBeInTheDocument();
});
