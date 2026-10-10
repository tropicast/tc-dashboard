import { Navigate } from 'react-router-dom';
import { useMe } from '../../hooks/use-me';
import { Onboarding } from './onboarding/Onboarding';

export function Home() {
  const me = useMe();
  return me.data?.memberships.length ? <Navigate to="/stations" replace /> : <Onboarding />;
}
