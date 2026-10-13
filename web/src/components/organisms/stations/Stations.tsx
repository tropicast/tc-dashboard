import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { api } from '../../../api/client';
import { Notice } from '../../atoms/Notice';
import { data } from '../../../lib/request';
import type { components } from '../../../api/schema';

export function Stations() {
  const { t } = useTranslation();
  const stations = useQuery({
    queryKey: ['stations', localStorage.getItem('tenant-id')],
    queryFn: () =>
      data<components['schemas']['PagedListOfStationResponse']>(
        api.GET('/api/v1/stations', { params: { query: { page: 1, pageSize: 50 } } }),
      ),
  });
  return (
    <section>
      <div className="heading">
        <div>
          <h1>{t('stations.title')}</h1>
          <p>{t('stations.intro')}</p>
        </div>
      </div>
      <Notice error={stations.error} />
      {stations.data?.items.length ? (
        <div className="cards">
          {stations.data.items.map((station) => (
            <article className="card" key={station.id}>
              <h2>
                <Link to={`/stations/${station.id}`}>{station.name}</Link>
              </h2>
              <p>
                {station.genre || t('stations.noGenre')} ·{' '}
                {station.country || t('stations.worldwide')}
              </p>
              <a href={station.listenerUrls.mp3}>{station.listenerUrls.mp3}</a>
            </article>
          ))}
        </div>
      ) : (
        <p>{t('stations.empty')}</p>
      )}
    </section>
  );
}
