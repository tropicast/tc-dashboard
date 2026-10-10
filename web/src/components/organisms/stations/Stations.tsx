import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api } from '../../../api/client';
import { Notice } from '../../atoms/Notice';
import { data } from '../../../lib/request';
import type { components } from '../../../api/schema';

export function Stations() {
  const stations = useQuery({
    queryKey: ['stations'],
    queryFn: () =>
      data<components['schemas']['PagedListOfStationResponse']>(
        api.GET('/api/v1/stations', { params: { query: { page: 1, pageSize: 50 } } }),
      ),
  });
  return (
    <section>
      <div className="heading">
        <div>
          <h1>Stations</h1>
          <p>Your current tenant stations.</p>
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
                {station.genre || 'No genre'} · {station.country || 'Worldwide'}
              </p>
              <a href={station.listenerUrls.mp3}>{station.listenerUrls.mp3}</a>
            </article>
          ))}
        </div>
      ) : (
        <p>No stations yet.</p>
      )}
    </section>
  );
}
