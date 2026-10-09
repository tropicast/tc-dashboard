import { useEffect, useState } from 'react';
import { api } from './api/client';

export default function App() {
  const [version, setVersion] = useState<string>();

  useEffect(() => {
    let active = true;
    void api.GET('/api/v1/version').then(({ data }) => {
      if (active && data) setVersion(data.version);
    });
    return () => {
      active = false;
    };
  }, []);

  return (
    <main>
      <h1>Tropicast</h1>
      <p>{version ? `API ${version}` : 'Connecting to the API…'}</p>
    </main>
  );
}
