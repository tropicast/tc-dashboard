import { useQuery } from '@tanstack/react-query';
import { Headphones01, Signal01 } from '@untitledui/icons';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { FeaturedIcon } from '@/components/foundations/featured-icon/featured-icon';
import { api } from '../../../api/client';
import { CopyButton } from '../../atoms/CopyButton';
import { Notice } from '../../atoms/Notice';
import { Loading } from '../../molecules/Loading';
import { PageHeader } from '../../molecules/PageHeader';
import { data } from '../../../lib/request';
import type { components } from '../../../api/schema';
import { useDocumentTitle } from '@/hooks/use-document-title';

export function Stations() {
  const { t } = useTranslation();
  useDocumentTitle(t('stations.title'));
  const stations = useQuery({
    queryKey: ['stations', localStorage.getItem('tenant-id')],
    queryFn: () =>
      data<components['schemas']['PagedListOfStationResponse']>(
        api.GET('/api/v1/stations', { params: { query: { page: 1, pageSize: 50 } } }),
      ),
  });
  return (
    <section>
      <PageHeader title={t('stations.title')} description={t('stations.intro')} />
      <Notice error={stations.error} />
      {stations.isPending ? (
        <Loading label={t('guard.loading')} />
      ) : stations.data?.items.length ? (
        <ul className="grid gap-6 md:grid-cols-2 xl:grid-cols-3">
          {stations.data.items.map((station) => (
            <li
              key={station.id}
              className="relative flex flex-col gap-5 rounded-xl bg-primary p-5 shadow-xs ring-1 ring-secondary transition duration-100 ease-linear focus-within:ring-2 focus-within:ring-brand hover:ring-primary"
            >
              <div className="flex items-start gap-3">
                <FeaturedIcon icon={Signal01} color="brand" theme="light" size="md" />
                <div className="flex min-w-0 flex-col gap-0.5">
                  <h2 className="text-md font-semibold text-primary">
                    {/* The whole card is the link's hit area; the copy button sits above it. */}
                    <Link
                      to={`/stations/${station.id}`}
                      className="outline-hidden after:absolute after:inset-0 after:rounded-xl"
                    >
                      {station.name}
                    </Link>
                  </h2>
                  <p className="text-sm text-tertiary">
                    {station.genre || t('stations.noGenre')} ·{' '}
                    {station.country || t('stations.worldwide')}
                  </p>
                </div>
              </div>
              <div className="relative z-1 flex items-center gap-2 rounded-lg bg-secondary py-1 pr-1 pl-3">
                <Headphones01 aria-hidden="true" className="size-4 shrink-0 text-fg-quaternary" />
                <a
                  href={station.listenerUrls.mp3}
                  className="min-w-0 flex-1 truncate font-mono text-xs text-secondary hover:underline"
                >
                  {station.listenerUrls.mp3}
                </a>
                <CopyButton value={station.listenerUrls.mp3} />
              </div>
            </li>
          ))}
        </ul>
      ) : (
        stations.isSuccess && (
          <div className="flex flex-col items-center gap-4 rounded-xl px-4 py-12 text-center ring-1 ring-secondary">
            <FeaturedIcon icon={Signal01} color="gray" theme="modern" size="lg" />
            <p className="text-md font-semibold text-primary">{t('stations.empty')}</p>
          </div>
        )
      )}
    </section>
  );
}
