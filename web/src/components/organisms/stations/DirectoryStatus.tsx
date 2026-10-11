import { useQuery } from '@tanstack/react-query';
import { api } from '../../../api/client';
import type { components } from '../../../api/schema';
import { data } from '../../../lib/request';

type Directory = components['schemas']['DirectoryStatusResponse'];

const labels: Record<Directory['state'], string> = {
  Off: 'Not listed.',
  NotIncluded: 'Not included in this plan.',
  NeedsAction: 'Action needed.',
  WaitingForLive: 'Waiting for the station to go live.',
  Retrying: 'RadioBrowser did not accept it yet; retrying.',
  Listed: 'Listed on RadioBrowser.',
  Delisting:
    'Opted out: at its next check of the stream, RadioBrowser marks the entry broken, which hides it from most apps.',
};

/** Where the station's RadioBrowser listing stands, next to the "List on RadioBrowser" setting. */
export function DirectoryStatus({ stationId }: { stationId: string }) {
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
      {labels[state]} {message}
      {listingUrl && (
        <>
          {' '}
          <a href={listingUrl} target="_blank" rel="noopener">
            View the listing
          </a>
        </>
      )}
      {state === 'Retrying' && nextAttemptAt && (
        <> Next attempt: {new Date(nextAttemptAt).toLocaleString()}.</>
      )}
    </p>
  );
}
