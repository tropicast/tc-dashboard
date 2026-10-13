import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '../../../api/client';
import type { components } from '../../../api/schema';
import { useMe } from '../../../hooks/use-me';
import { embedSnippets } from '../../../lib/embed';
import { data } from '../../../lib/request';
import { CopyButton } from '../../atoms/CopyButton';

type PublicStation = components['schemas']['PublicStationResponse'];

/** The station's public page and, when the plan includes it, the embeddable player. */
export function SharePanel({ stationSlug }: { stationSlug: string }) {
  const { t } = useTranslation();
  const me = useMe();
  const tenantId = localStorage.getItem('tenant-id');
  const tenantSlug = me.data?.memberships.find((m) => m.tenantId === tenantId)?.tenantSlug;
  const station = useQuery({
    queryKey: ['public-station', tenantSlug, stationSlug],
    enabled: Boolean(tenantSlug),
    queryFn: () =>
      data<PublicStation>(
        api.GET('/api/v1/public/stations/{tenantSlug}/{stationSlug}', {
          params: { path: { tenantSlug: tenantSlug!, stationSlug } },
        }),
      ),
  });
  if (!station.data) return null;
  const { pageUrl, embedUrl, name } = station.data;
  const snippets = embedUrl ? embedSnippets(embedUrl, name) : undefined;
  return (
    <article className="panel">
      <h2>{t('share.title')}</h2>
      <p>
        <strong>{t('share.publicPage')}</strong>{' '}
        <a href={pageUrl} target="_blank" rel="noopener">
          {pageUrl}
        </a>{' '}
        <CopyButton value={pageUrl} />
      </p>
      {snippets ? (
        <>
          <p>
            <strong>{t('share.iframe')}</strong> <CopyButton value={snippets.iframe} />
          </p>
          <code className="snippet">{snippets.iframe}</code>
          <p>
            <strong>{t('share.script')}</strong> <CopyButton value={snippets.script} />
          </p>
          <code className="snippet">{snippets.script}</code>
        </>
      ) : (
        <p>{t('share.notIncluded')}</p>
      )}
    </article>
  );
}
