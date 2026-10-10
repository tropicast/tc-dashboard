import { useMutation } from '@tanstack/react-query';
import { useEffect, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, NavLink, useNavigate } from 'react-router-dom';
import { api } from '../../../api/client';
import { queryClient } from '../../../lib/query-client';
import { data } from '../../../lib/request';

export function AppShell({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [dark, setDark] = useState(localStorage.getItem('theme') === 'dark');
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
          <span>{t('dashboard')}</span>
        </Link>
        <nav aria-label="Main navigation">
          <NavLink to="/stations">{t('stations')}</NavLink>
          <NavLink to="/account">{t('account')}</NavLink>
        </nav>
        <button
          className="quiet"
          onClick={() => {
            const next = !dark;
            setDark(next);
            localStorage.setItem('theme', next ? 'dark' : 'light');
          }}
        >
          {dark ? 'Light theme' : 'Dark theme'}
        </button>
        <button className="quiet" onClick={() => logout.mutate()}>
          {t('signOut')}
        </button>
      </header>
      <main>{children}</main>
    </div>
  );
}
