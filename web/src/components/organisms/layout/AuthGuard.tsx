import { useEffect, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Navigate, useLocation } from 'react-router-dom';
import { useMe } from '../../../hooks/use-me';
import { selectTenant } from '../../../lib/tenant';
import { Loading } from '../../molecules/Loading';
import { TermsPrompt } from '../../../legal/TermsPrompt';

export function AuthGuard({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const me = useMe();
  const location = useLocation();
  useEffect(() => {
    if (me.data) selectTenant(me.data.memberships);
  }, [me.data]);
  if (me.isPending) {
    return (
      <main className="flex min-h-dvh items-center justify-center bg-primary">
        <Loading label={t('guard.loading')} />
      </main>
    );
  }
  // Come back to the same page after signing in (e.g. /device?code=… opened by the desktop app).
  if (me.isError) {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  }
  // A new version of the terms must be accepted before going on; the Account page stays open, so someone who
  // refuses can still download their data or delete their account.
  if (me.data.termsAccepted === false && location.pathname !== '/account') {
    return <TermsPrompt version={me.data.termsVersion} />;
  }
  return <>{children}</>;
}
