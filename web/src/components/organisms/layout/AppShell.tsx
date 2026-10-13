import { useMutation } from '@tanstack/react-query';
import { useEffect, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, NavLink, useNavigate } from 'react-router-dom';
import { api } from '../../../api/client';
import { useMe } from '../../../hooks/use-me';
import { queryClient } from '../../../lib/query-client';
import { data } from '../../../lib/request';

export function AppShell({ children }: { children: ReactNode }) {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const me = useMe();
  const [dark, setDark] = useState(localStorage.getItem('theme') === 'dark');
  const [tenantId, setTenantId] = useState(localStorage.getItem('tenant-id') ?? '');
  const activeTenantId = me.data?.memberships.some((membership) => membership.tenantId === tenantId)
    ? tenantId
    : (me.data?.memberships[0]?.tenantId ?? '');
  useEffect(() => {
    document.documentElement.dataset.theme = dark ? 'dark' : 'light';
  }, [dark]);
  const logout = useMutation({
    mutationFn: () => data<void>(api.POST('/api/v1/auth/logout')),
    onSuccess: () => {
      void queryClient.clear();
      navigate('/login');
    },
  });
  return (
    <div className="shell">
      <header>
        <Link className="brand" to="/">
          <img src="/tropicast-logo.svg" alt="Tropicast" />
          <span>{t('nav.dashboard')}</span>
        </Link>
        <nav aria-label={t('nav.mainNavigation')}>
          <NavLink to="/stations">{t('nav.stations')}</NavLink>
          <NavLink to="/account">{t('nav.account')}</NavLink>
        </nav>
        {me.data && me.data.memberships.length > 1 && (
          <label>
            {t('nav.tenant')}
            <select
              aria-label={t('nav.tenant')}
              value={activeTenantId}
              onChange={(event) => {
                localStorage.setItem('tenant-id', event.target.value);
                setTenantId(event.target.value);
                void queryClient.invalidateQueries();
              }}
            >
              {me.data.memberships.map((membership) => (
                <option key={membership.tenantId} value={membership.tenantId}>
                  {membership.tenantName}
                </option>
              ))}
            </select>
          </label>
        )}
        <label>
          {t('nav.language')}
          <select
            aria-label={t('nav.language')}
            value={i18n.resolvedLanguage}
            onChange={(event) => {
              localStorage.setItem('language', event.target.value);
              void i18n.changeLanguage(event.target.value);
            }}
          >
            <option value="en">{t('nav.english')}</option>
            <option value="fr">{t('nav.french')}</option>
          </select>
        </label>
        <button
          className="quiet"
          onClick={() => {
            const next = !dark;
            setDark(next);
            localStorage.setItem('theme', next ? 'dark' : 'light');
          }}
        >
          {dark ? t('nav.lightTheme') : t('nav.darkTheme')}
        </button>
        <button className="quiet" onClick={() => logout.mutate()}>
          {t('nav.signOut')}
        </button>
      </header>
      <main>{children}</main>
    </div>
  );
}
