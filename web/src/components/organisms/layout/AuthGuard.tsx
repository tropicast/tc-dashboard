import { useEffect, type ReactNode } from 'react';
import { Navigate } from 'react-router-dom';
import { useMe } from '../../../hooks/use-me';

export function AuthGuard({ children }: { children: ReactNode }) {
  const me = useMe();
  useEffect(() => {
    const tenantId = me.data?.memberships[0]?.tenantId;
    if (tenantId) localStorage.setItem('tenant-id', tenantId);
  }, [me.data]);
  if (me.isPending) return <main className="loading">Loading your dashboard…</main>;
  if (me.isError) return <Navigate to="/login" replace />;
  return <>{children}</>;
}
