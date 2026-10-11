import { useEffect, type ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useMe } from '../../../hooks/use-me';
import { selectTenant } from '../../../lib/tenant';

export function AuthGuard({ children }: { children: ReactNode }) {
  const me = useMe();
  const location = useLocation();
  useEffect(() => {
    if (me.data) selectTenant(me.data.memberships);
  }, [me.data]);
  if (me.isPending) return <main className="loading">Loading your dashboard…</main>;
  // Come back to the same page after signing in (e.g. /device?code=… opened by the desktop app).
  if (me.isError) {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  }
  return <>{children}</>;
}
