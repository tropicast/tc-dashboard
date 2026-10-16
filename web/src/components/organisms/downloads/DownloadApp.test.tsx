import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, test, vi } from 'vitest';
import '../../../lib/i18n';
import { suggestedPlatform } from '../../../lib/platform';
import { RouteProvider } from '../../../providers/route-provider';
import { DownloadApp } from './DownloadApp';

const file = (platform: string, name: string) => ({
  platform,
  name,
  size: 1,
  sha256: 'a'.repeat(64),
  url: `https://app.test/download/station/${platform}`,
});
const release = {
  version: '0.2.1',
  releasesPage: 'https://github.com/tropicast/tc-station/releases/latest',
  files: [
    file('win-x64', 'Tropicast-Station-0.2.1-win-x64.msix'),
    file('osx-arm64', 'Tropicast-Station-0.2.1-osx-arm64.dmg'),
    file('osx-x64', 'Tropicast-Station-0.2.1-osx-x64.dmg'),
    file('linux-x64', 'Tropicast-Station-0.2.1-x86_64.AppImage'),
    file('readme', 'README.md'),
  ],
};

function renderWith(body: object, userAgent: string) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => Response.json(body)),
  );
  vi.spyOn(navigator, 'userAgent', 'get').mockReturnValue(userAgent);
  render(
    // In the app's router: absolute links must stay absolute.
    <MemoryRouter>
      <RouteProvider>
        <QueryClientProvider
          client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
        >
          <DownloadApp />
        </QueryClientProvider>
      </RouteProvider>
    </MemoryRouter>,
  );
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

test('suggests the installer of this computer and lists the others with the install guide', async () => {
  renderWith(release, 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)');

  const primary = await screen.findByRole('link', { name: 'Download for Windows 10 or 11' });
  expect(primary.getAttribute('href')).toBe('https://app.test/download/station/win-x64');
  expect(screen.getByText('Tropicast Station 0.2.1')).toBeTruthy();
  const others = screen.getByRole('list', { name: 'Other systems' });
  expect(others.textContent).toContain('macOS (Apple silicon)');
  expect(others.textContent).toContain('Linux (AppImage)');
  expect(others.textContent).not.toContain('Windows');
  expect(
    screen.getByRole('link', { name: 'Install guide and troubleshooting' }).getAttribute('href'),
  ).toBe('https://app.test/download/station/readme');
});

test('links to GitHub when no release is served from the dashboard', async () => {
  renderWith(
    { version: null, releasesPage: release.releasesPage, files: [] },
    'Mozilla/5.0 (X11; Linux x86_64)',
  );

  const link = await screen.findByRole('link', { name: 'Download Tropicast Station on GitHub' });
  expect(link.getAttribute('href')).toBe(release.releasesPage);
});

test('suggests a build from the user agent', () => {
  expect(suggestedPlatform('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)')).toBe('osx-arm64');
  expect(suggestedPlatform('Mozilla/5.0 (X11; Linux x86_64)')).toBe('linux-x64');
  expect(suggestedPlatform('Mozilla/5.0 (Linux; Android 14)')).toBeUndefined();
  expect(suggestedPlatform('Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X)')).toBeUndefined();
});
