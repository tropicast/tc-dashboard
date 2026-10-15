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
const jingle = {
  id: 'tr1',
  fileName: 'jingle.mp3',
  title: 'Station jingle',
  artist: '',
  format: 'Mp3',
  durationMs: 185_000,
  sizeBytes: 3 * 1024 * 1024,
  createdAt: '2026-10-15T10:00:00Z',
};
const GiB = 1024 ** 3;

interface Sent {
  url: string;
  headers: Record<string, string>;
}

let puts: Sent[] = [];
let putStatus = 200;

/** The browser's PUT to the bucket: reports half, then all, then answers. */
class FakeXhr {
  upload: {
    onprogress:
      ((event: { lengthComputable: boolean; loaded: number; total: number }) => void) | null;
  } = {
    onprogress: null,
  };
  status = 0;
  onload: (() => void) | null = null;
  onerror: (() => void) | null = null;
  private url = '';
  private headers: Record<string, string> = {};
  open(_method: string, url: string) {
    this.url = url;
  }
  setRequestHeader(name: string, value: string) {
    this.headers[name] = value;
  }
  send() {
    puts.push({ url: this.url, headers: this.headers });
    setTimeout(() => {
      this.upload.onprogress?.({ lengthComputable: true, loaded: 50, total: 100 });
      this.status = putStatus;
      this.onload?.();
    }, 0);
  }
}

beforeEach(() => {
  localStorage.setItem('tenant-id', 't1');
  puts = [];
  putStatus = 200;
  vi.stubGlobal('XMLHttpRequest', FakeXhr);
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  localStorage.clear();
  queryClient.clear();
});

type Handler = (request: Request, path: string) => Response | Promise<Response> | undefined;

function renderLibrary(
  options: { quota?: number; used?: number; tracks?: object[]; handler?: Handler } = {},
) {
  const calls: { method: string; path: string; body?: unknown }[] = [];
  let tracks = options.tracks ?? [jingle];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (request: Request) => {
      const path = new URL(request.url).pathname;
      const body = request.method === 'GET' ? undefined : await request.clone().text();
      calls.push({ method: request.method, path, body: body ? JSON.parse(body) : undefined });
      const handled = await options.handler?.(request, path);
      if (handled) return handled;
      if (path === '/api/v1/auth/me') return Response.json(me);
      if (path === '/api/v1/auth/antiforgery') return new Response(null, { status: 204 });
      if (path === '/api/v1/stations/s1')
        return Response.json(station, { headers: { ETag: '"1"' } });
      if (path === '/api/v1/stations/s1/tracks' && request.method === 'GET')
        return Response.json({
          tracks,
          storage: {
            usedBytes: options.used ?? jingle.sizeBytes,
            quotaBytes: options.quota ?? GiB,
          },
        });
      if (path === '/api/v1/stations/s1/tracks/uploads')
        return Response.json({
          trackId: 'up1',
          uploadUrl: 'https://bucket.test/stations/s1/tracks/up1?signature=x',
          headers: { 'Content-Type': 'audio/mpeg' },
          expiresAt: '2026-10-15T11:00:00Z',
        });
      if (path === '/api/v1/stations/s1/tracks' && request.method === 'POST') {
        const added = { ...jingle, id: 'tr2', fileName: 'morning.mp3', title: 'Morning' };
        tracks = [added, ...tracks];
        return Response.json(added, { status: 201 });
      }
      if (path.startsWith('/api/v1/stations/s1/tracks/') && request.method === 'DELETE') {
        tracks = tracks.filter((track) => !path.endsWith((track as { id: string }).id));
        return new Response(null, { status: 204 });
      }
      return Response.json({ title: 'Not found' }, { status: 404 });
    }),
  );
  render(
    <MemoryRouter initialEntries={['/stations/s1/library']}>
      <App />
    </MemoryRouter>,
  );
  return calls;
}

function choose(...files: File[]) {
  fireEvent.change(screen.getByTestId('file-input'), { target: { files } });
}

const mp3 = (name: string, size = 1000) =>
  new File([new Uint8Array(size)], name, { type: 'audio/mpeg' });

test('lists the tracks and the storage used out of the quota', async () => {
  renderLibrary();
  expect(await screen.findByRole('heading', { name: 'Media library' })).toBeInTheDocument();
  const row = screen.getByRole('row', { name: /Station jingle/ });
  expect(within(row).getByText('3:05')).toBeInTheDocument();
  expect(within(row).getByText('Unknown artist')).toBeInTheDocument();
  expect(screen.getByText('3 MB of 1 GB used')).toBeInTheDocument();
  expect(screen.getByRole('progressbar', { name: 'Audio storage used' })).toHaveAttribute(
    'aria-valuetext',
    '3 MB of 1 GB used',
  );
  expect(
    screen.getByText('Upload only music you have the right to broadcast.'),
  ).toBeInTheDocument();
});

test('uploads a file: presigned PUT with the signed headers, then confirm, and announces it', async () => {
  const calls = renderLibrary();
  await screen.findByRole('heading', { name: 'Media library' });

  choose(mp3('morning.mp3'));

  expect(await screen.findByRole('row', { name: /Morning/ })).toBeInTheDocument();
  expect(puts).toEqual([
    {
      url: 'https://bucket.test/stations/s1/tracks/up1?signature=x',
      headers: { 'Content-Type': 'audio/mpeg' },
    },
  ]);
  expect(calls.find((c) => c.path.endsWith('/uploads'))?.body).toEqual({
    fileName: 'morning.mp3',
    size: 1000,
  });
  expect(
    calls.find((c) => c.method === 'POST' && c.path === '/api/v1/stations/s1/tracks')?.body,
  ).toEqual({
    uploadId: 'up1',
  });
  expect(screen.getByText('Added')).toBeInTheDocument();
  await waitFor(() => expect(screen.getByText('morning.mp3 added.')).toBeInTheDocument());
});

test('refuses unsupported, oversized and over-quota files before uploading', async () => {
  const calls = renderLibrary({ quota: GiB, used: GiB - 500 });
  await screen.findByRole('heading', { name: 'Media library' });

  choose(
    new File(['x'], 'cover.jpg', { type: 'image/jpeg' }),
    { name: 'huge.flac', size: 201 * 1024 * 1024 } as File,
    mp3('one-more.mp3', 1000),
  );

  expect(await screen.findByText('Not an MP3, FLAC or Ogg file.')).toBeInTheDocument();
  expect(screen.getByText('Larger than 200 MB.')).toBeInTheDocument();
  expect(
    screen.getByText('Does not fit in the storage left. Delete tracks or upgrade.'),
  ).toBeInTheDocument();
  expect(calls.some((c) => c.path.endsWith('/uploads'))).toBe(false);
  expect(puts).toEqual([]);
});

test('shows the server’s reason when it refuses the stored file', async () => {
  renderLibrary({
    handler: (request, path) =>
      path === '/api/v1/stations/s1/tracks' && request.method === 'POST'
        ? Response.json(
            {
              title:
                'This file is not MP3, FLAC or Ogg (Opus or Vorbis) audio the server can read.',
            },
            { status: 422 },
          )
        : undefined,
  });
  await screen.findByRole('heading', { name: 'Media library' });

  choose(mp3('renamed.mp3'));

  expect(
    await screen.findByText(
      'This file is not MP3, FLAC or Ogg (Opus or Vorbis) audio the server can read.',
    ),
  ).toBeInTheDocument();
  await waitFor(() =>
    expect(screen.getByText(/renamed\.mp3 not added: This file is not MP3/)).toBeInTheDocument(),
  );
});

test('a failed upload to storage is reported', async () => {
  putStatus = 403;
  renderLibrary();
  await screen.findByRole('heading', { name: 'Media library' });

  choose(mp3('song.mp3'));

  expect(await screen.findByText('The upload to storage failed. Try again.')).toBeInTheDocument();
});

test('a plan without AutoDJ shows the upgrade note instead of the upload', async () => {
  renderLibrary({ quota: 0, used: 0, tracks: [] });
  expect(
    await screen.findByText('AutoDJ and the media library are included from the Starter plan.'),
  ).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Choose files' })).not.toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'See the plans' })).toHaveAttribute('href', '/account');
});

test('deletes a track after confirmation', async () => {
  const calls = renderLibrary();
  fireEvent.click(await screen.findByRole('button', { name: 'Delete Station jingle' }));
  const dialog = await screen.findByRole('dialog');
  fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));

  expect(
    await screen.findByText('No tracks yet. Upload some to build a playlist.'),
  ).toBeInTheDocument();
  expect(
    calls.some((c) => c.method === 'DELETE' && c.path === '/api/v1/stations/s1/tracks/tr1'),
  ).toBe(true);
});

test('drops files on the drop zone', async () => {
  renderLibrary();
  await screen.findByRole('heading', { name: 'Media library' });

  fireEvent.drop(screen.getByTestId('dropzone'), { dataTransfer: { files: [mp3('dropped.mp3')] } });

  expect(await screen.findByRole('row', { name: /Morning/ })).toBeInTheDocument();
  expect(puts).toHaveLength(1);
});

test('the account usage card shows the audio storage used', async () => {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (request: Request) => {
      const path = new URL(request.url).pathname;
      if (path === '/api/v1/auth/me') return Response.json(me);
      if (path === '/api/v1/tenants/current')
        return Response.json({
          id: 't1',
          name: 'Radio',
          slug: 'radio',
          status: 'Active',
          plan: {
            id: 'starter',
            name: 'Starter',
            maxStations: 1,
            maxListeners: 500,
            maxBitrateKbps: 128,
            formats: ['mp3', 'opus'],
            directoryListing: true,
            embed: false,
            analytics: true,
            autoDj: true,
            autoDjScheduling: false,
            storageQuotaBytes: GiB,
          },
          stationCount: 1,
          storageUsedBytes: 512 * 1024 * 1024,
        });
      return Response.json({ title: 'Not found' }, { status: 404 });
    }),
  );
  render(
    <MemoryRouter initialEntries={['/account']}>
      <App />
    </MemoryRouter>,
  );
  expect(await screen.findByRole('progressbar', { name: 'Audio storage used' })).toHaveAttribute(
    'aria-valuetext',
    '512 MB of 1 GB used',
  );
});
