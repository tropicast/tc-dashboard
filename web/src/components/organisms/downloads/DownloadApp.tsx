import { useQuery } from '@tanstack/react-query';
import { Download01, LinkExternal01 } from '@untitledui/icons';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/base/buttons/button';
import { api } from '../../../api/client';
import type { components } from '../../../api/schema';
import { data } from '../../../lib/request';
import { type DesktopPlatform, suggestedPlatform } from '../../../lib/platform';

type Downloads = components['schemas']['StationDownloadsResponse'];

const installers: DesktopPlatform[] = ['win-x64', 'osx-arm64', 'osx-x64', 'linux-x64'];
const linkClass =
  'inline-flex items-center gap-1 text-sm font-semibold text-brand-secondary hover:text-brand-secondary_hover';

/** Download links of Tropicast Station: this computer's installer first, the others and the install guide below. */
export function DownloadApp() {
  const { t } = useTranslation();
  const downloads = useQuery({
    queryKey: ['downloads', 'station'],
    staleTime: 5 * 60_000,
    queryFn: () => data<Downloads>(api.GET('/api/v1/downloads/station')),
  });
  if (!downloads.data) return null;
  const { version, files, releasesPage } = downloads.data;
  const available = files.filter((f): f is typeof f & { platform: DesktopPlatform } =>
    installers.includes(f.platform as DesktopPlatform),
  );
  if (!version || available.length === 0)
    return (
      <a href={releasesPage} target="_blank" rel="noopener" className={linkClass}>
        {t('downloads.github')}
        <LinkExternal01 aria-hidden="true" className="size-4" />
      </a>
    );
  const suggested = suggestedPlatform(navigator.userAgent);
  const primary = available.find((f) => f.platform === suggested);
  const others = available.filter((f) => f !== primary);
  const guide = files.find((f) => f.platform === 'readme');
  return (
    <div className="flex flex-col gap-3">
      {primary && (
        // download: a server link, not a page of this app (no client-side navigation).
        <Button href={primary.url} download size="md" iconLeading={Download01}>
          {t('downloads.for', { platform: t(`downloads.platforms.${primary.platform}`) })}
        </Button>
      )}
      <p className="text-sm text-tertiary">{t('downloads.version', { version })}</p>
      <ul
        className="flex flex-col gap-1"
        aria-label={primary ? t('downloads.others') : t('downloads.all')}
      >
        {others.map((file) => (
          <li key={file.platform}>
            <a href={file.url} download className={linkClass}>
              {t(`downloads.platforms.${file.platform}`)}
            </a>
          </li>
        ))}
      </ul>
      {guide && (
        <a href={guide.url} download className={linkClass}>
          {t('downloads.guide')}
        </a>
      )}
    </div>
  );
}
