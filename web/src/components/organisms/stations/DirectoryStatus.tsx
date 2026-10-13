import { useQuery } from '@tanstack/react-query';
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
    <p className="hint" role="status">
      {t(`directory.${state}`)} {message}
      {listingUrl && (
        <>
          {' '}
          <a href={listingUrl} target="_blank" rel="noopener">
            {t('directory.viewListing')}
          </a>
        </>
      )}
      {state === 'Retrying' && nextAttemptAt && (
        <> {t('directory.nextAttempt', { date: formatDateTime(nextAttemptAt) })}</>
      )}
    </p>
  );
}
