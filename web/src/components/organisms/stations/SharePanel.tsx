import { useQuery } from '@tanstack/react-query';
import { LinkExternal01 } from '@untitledui/icons';
import { useTranslation } from 'react-i18next';
import { api } from '../../../api/client';
import type { components } from '../../../api/schema';
import { useMe } from '../../../hooks/use-me';
import { embedSnippets } from '../../../lib/embed';
import { data } from '../../../lib/request';
import { CopyButton } from '../../atoms/CopyButton';
import { Card } from '../../molecules/Card';

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
    <Card title={t('share.title')}>
      <div className="flex flex-col gap-1.5">
        <span className="text-sm font-medium text-secondary">{t('share.publicPage')}</span>
        <div className="flex items-center gap-2">
          <a
            href={pageUrl}
            target="_blank"
            rel="noopener"
            className="inline-flex min-w-0 items-center gap-1 text-sm font-semibold break-all text-brand-secondary hover:text-brand-secondary_hover"
          >
            {pageUrl}
            <LinkExternal01 aria-hidden="true" className="size-4 shrink-0" />
          </a>
          <CopyButton value={pageUrl} />
        </div>
      </div>
      {snippets ? (
        <>
          <Snippet label={t('share.iframe')} value={snippets.iframe} />
          <Snippet label={t('share.script')} value={snippets.script} />
        </>
      ) : (
        <p className="text-sm text-tertiary">{t('share.notIncluded')}</p>
      )}
    </Card>
  );
}

/** Untitled UI code snippet: label, copy button and the code on a muted surface. */
function Snippet({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium text-secondary">{label}</span>
        <CopyButton value={value} />
      </div>
      <pre className="overflow-x-auto rounded-lg bg-secondary p-3 ring-1 ring-secondary ring-inset">
        <code className="font-mono text-xs break-all whitespace-pre-wrap text-primary">
          {value}
        </code>
      </pre>
    </div>
  );
}
