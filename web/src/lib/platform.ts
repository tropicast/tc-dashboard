/** Desktop app platforms of the download links (tc-dashboard#127). */
export type DesktopPlatform = 'win-x64' | 'osx-arm64' | 'osx-x64' | 'linux-x64';

/**
 * The installer to suggest from the browser's user agent. A Mac is offered the Apple silicon build: browsers report
 * "Intel" on every Mac, and Intel Macs are the minority; both stay listed.
 */
export function suggestedPlatform(userAgent: string): DesktopPlatform | undefined {
  if (/Windows/i.test(userAgent)) return 'win-x64';
  if (/Macintosh|Mac OS X/i.test(userAgent) && !/iPhone|iPad/i.test(userAgent)) return 'osx-arm64';
  if (/Linux/i.test(userAgent) && !/Android/i.test(userAgent)) return 'linux-x64';
  return undefined;
}
