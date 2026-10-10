import { useEffect, type ReactNode } from 'react';
import { Navigate } from 'react-router-dom';
import { useMe } from '../../../hooks/use-me';
import { selectTenant } from '../../../lib/tenant';

export function AuthGuard({ children }: { children: ReactNode }) {
  const me = useMe();
  useEffect(() => {
    if (me.data) selectTenant(me.data.memberships);
  }, [me.data]);
  if (me.isPending) return <main className="loading">Loading your dashboard…</main>;
  if (me.isError) return <Navigate to="/login" replace />;
  return <>{children}</>;
}
