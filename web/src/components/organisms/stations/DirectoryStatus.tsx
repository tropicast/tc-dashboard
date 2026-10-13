import { useQuery } from '@tanstack/react-query';
import { LinkExternal01 } from '@untitledui/icons';
import { useTranslation } from 'react-i18next';
import { api } from '../../../api/client';
import type { components } from '../../../api/schema';
import { formatDateTime } from '../../../lib/i18n';
import { data } from '../../../lib/request';

type Directory = components['schemas']['DirectoryStatusResponse'];

/** Where the station's RadioBrowser listing stands, next to the "List on RadioBrowser" setting. */
export function DirectoryStatus({ stationId }: { stationId: string }) {
  const { t } = useTranslation();
  const tenantId = localStorage.getItem('tenant-id');
  const directory = useQuery({
    queryKey: ['directory', tenantId, stationId],
    queryFn: () =>
      data<Directory>(
        api.GET('/api/v1/stations/{id}/directory', { params: { path: { id: stationId } } }),
      ),
    refetchInterval: 60_000,
  });
  if (!directory.data) return null;
  const { state, message, listingUrl, nextAttemptAt } = directory.data;
  return (
    <p className="pl-6 text-sm text-tertiary" role="status">
      {t(`directory.${state}`)} {message}
      {listingUrl && (
        <>
          {' '}
          <a
            href={listingUrl}
            target="_blank"
            rel="noopener"
            className="inline-flex items-center gap-1 font-semibold text-brand-secondary hover:text-brand-secondary_hover"
          >
            {t('directory.viewListing')}
            <LinkExternal01 aria-hidden="true" className="size-4" />
          </a>
        </>
      )}
      {state === 'Retrying' && nextAttemptAt && (
        <> {t('directory.nextAttempt', { date: formatDateTime(nextAttemptAt) })}</>
      )}
    </p>
  );
}
