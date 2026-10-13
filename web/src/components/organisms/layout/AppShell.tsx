import { useMutation } from '@tanstack/react-query';
import { useEffect, useState, type FC, type ReactNode } from 'react';
import {
  LogOut01,
  Menu02,
  Monitor01,
  Moon01,
  Signal01,
  Sun,
  Users01,
  XClose,
} from '@untitledui/icons';
import {
  Button as AriaButton,
  Dialog as AriaDialog,
  Modal as AriaModal,
  ModalOverlay as AriaModalOverlay,
} from 'react-aria-components';
import { useTranslation } from 'react-i18next';
import { Link, NavLink, useNavigate } from 'react-router-dom';
import { Button } from '@/components/base/buttons/button';
import { Dropdown } from '@/components/base/dropdown/dropdown';
import { NativeSelect } from '@/components/base/select/select-native';
import { useTheme, type Theme } from '@/providers/theme-provider';
import { cx } from '@/utils/cx';
import { api } from '../../../api/client';
import { useMe } from '../../../hooks/use-me';
import { queryClient } from '../../../lib/query-client';
import { data } from '../../../lib/request';

const themeIcons: Record<Theme, FC<{ className?: string }>> = {
  light: Sun,
  dark: Moon01,
  system: Monitor01,
};

/** Untitled UI header navigation: logo, primary nav and actions; a slide-out menu below `lg`. */
export function AppShell({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [menuOpen, setMenuOpen] = useState(false);
  // One workspace choice for the header and the drawer (both selects show the same value).
  const [tenantId, setTenantId] = useState(localStorage.getItem('tenant-id') ?? '');
  const chooseTenant = (id: string) => {
    localStorage.setItem('tenant-id', id);
    setTenantId(id);
    void queryClient.invalidateQueries();
  };
  // The drawer exists only below `lg`: close it when the window grows past that (tablet rotation),
  // or it would keep the page inert and scroll-locked with nothing visible to close it.
  useEffect(() => {
    if (typeof window.matchMedia !== 'function') return;
    const wide = window.matchMedia('(min-width: 1024px)');
    const close = () => wide.matches && setMenuOpen(false);
    wide.addEventListener('change', close);
    return () => wide.removeEventListener('change', close);
  }, []);
  const closeMenu = () => setMenuOpen(false);
  const logout = useMutation({
    mutationFn: () => data<void>(api.POST('/api/v1/auth/logout')),
    onSuccess: () => {
      void queryClient.clear();
      navigate('/login');
    },
  });
  const signOut = (
    <Button
      color="tertiary"
      size="sm"
      iconLeading={LogOut01}
      isLoading={logout.isPending}
      showTextWhileLoading
      onPress={() => logout.mutate()}
    >
      {t('nav.signOut')}
    </Button>
  );

  return (
    <div className="flex min-h-dvh flex-col bg-primary">
      <header className="sticky top-0 z-40 border-b border-secondary bg-primary">
        <div className="mx-auto flex h-16 w-full max-w-container items-center gap-4 px-4 md:px-8">
          <Brand />
          <nav aria-label={t('nav.mainNavigation')} className="max-lg:hidden">
            <NavItems />
          </nav>
          <div className="ml-auto flex items-center gap-2 max-lg:hidden">
            <TenantSelect value={tenantId} onChange={chooseTenant} />
            <LanguageSelect />
            <ThemeMenu />
            {signOut}
          </div>
          <AriaButton
            aria-label={t('nav.openMenu')}
            onPress={() => setMenuOpen(true)}
            className="ml-auto flex items-center justify-center rounded-lg p-2 text-fg-secondary outline-focus-ring hover:bg-primary_hover hover:text-fg-secondary_hover focus-visible:outline-2 focus-visible:outline-offset-2 lg:hidden"
          >
            <Menu02 aria-hidden="true" className="size-6" />
          </AriaButton>
        </div>
      </header>

      <AriaModalOverlay
        isDismissable
        isOpen={menuOpen}
        onOpenChange={setMenuOpen}
        className={({ isEntering, isExiting }) =>
          cx(
            'fixed inset-0 z-50 bg-overlay/70 pr-16 backdrop-blur-md lg:hidden',
            isEntering && 'duration-300 ease-in-out animate-in fade-in',
            isExiting && 'duration-200 ease-in-out animate-out fade-out',
          )
        }
      >
        <AriaModal className="h-dvh w-full max-w-80">
          <AriaDialog
            aria-label={t('nav.mainNavigation')}
            className="flex h-full flex-col gap-6 overflow-y-auto bg-primary px-4 pt-4 pb-6 outline-hidden"
          >
            <div className="flex items-center justify-between">
              <Brand onNavigate={closeMenu} />
              <AriaButton
                aria-label={t('nav.closeMenu')}
                onPress={closeMenu}
                className="rounded-lg p-2 text-fg-quaternary outline-focus-ring hover:bg-primary_hover hover:text-fg-quaternary_hover focus-visible:outline-2"
              >
                <XClose aria-hidden="true" className="size-6" />
              </AriaButton>
            </div>
            <nav aria-label={t('nav.mainNavigation')}>
              <NavItems vertical onNavigate={closeMenu} />
            </nav>
            <div className="mt-auto flex flex-col gap-4 border-t border-secondary pt-6">
              <TenantSelect value={tenantId} onChange={chooseTenant} withLabel />
              <LanguageSelect withLabel />
              <div className="flex items-center justify-between gap-2">
                <ThemeMenu />
                {signOut}
              </div>
            </div>
          </AriaDialog>
        </AriaModal>
      </AriaModalOverlay>

      <main className="mx-auto w-full max-w-container flex-1 px-4 py-8 md:px-8 md:py-12">
        {children}
      </main>
    </div>
  );
}

function Brand({ onNavigate }: { onNavigate?: () => void }) {
  const { t } = useTranslation();
  return (
    <Link
      to="/"
      onClick={onNavigate}
      className="flex shrink-0 items-center gap-2.5 rounded-xs outline-focus-ring focus-visible:outline-2 focus-visible:outline-offset-2"
    >
      <img src="/tropicast-logo.svg" alt="Tropicast" className="h-8 w-auto" />
      <span className="text-md font-semibold text-primary">{t('nav.dashboard')}</span>
    </Link>
  );
}

function NavItems({
  vertical = false,
  onNavigate,
}: {
  vertical?: boolean;
  onNavigate?: () => void;
}) {
  const { t } = useTranslation();
  const items = [
    { to: '/stations', label: t('nav.stations'), icon: Signal01 },
    { to: '/account', label: t('nav.account'), icon: Users01 },
  ];
  return (
    <ul className={cx('flex gap-0.5', vertical ? 'flex-col' : 'items-center')}>
      {items.map(({ to, label, icon: Icon }) => (
        <li key={to}>
          <NavLink
            to={to}
            onClick={onNavigate}
            className={({ isActive }) =>
              cx(
                'group flex items-center gap-2 rounded-md px-3 py-2 text-sm font-semibold text-secondary outline-focus-ring transition duration-100 ease-linear hover:bg-primary_hover hover:text-secondary_hover focus-visible:outline-2 focus-visible:outline-offset-2',
                isActive && 'bg-secondary text-secondary_hover hover:bg-secondary_hover',
              )
            }
          >
            <Icon
              aria-hidden="true"
              className="size-5 text-fg-quaternary group-hover:text-fg-quaternary_hover"
            />
            {label}
          </NavLink>
        </li>
      ))}
    </ul>
  );
}

function TenantSelect({
  value,
  onChange,
  withLabel = false,
}: {
  value: string;
  onChange: (tenantId: string) => void;
  withLabel?: boolean;
}) {
  const { t } = useTranslation();
  const me = useMe();
  const memberships = me.data?.memberships ?? [];
  if (memberships.length < 2) return null;
  const active = memberships.some((membership) => membership.tenantId === value)
    ? value
    : memberships[0].tenantId;
  return (
    <NativeSelect
      size="sm"
      label={withLabel ? t('nav.tenant') : undefined}
      aria-label={withLabel ? undefined : t('nav.tenant')}
      className={withLabel ? undefined : 'w-44'}
      value={active}
      onChange={(event) => onChange(event.target.value)}
      options={memberships.map((membership) => ({
        value: membership.tenantId,
        label: membership.tenantName,
      }))}
    />
  );
}

function LanguageSelect({ withLabel = false }: { withLabel?: boolean }) {
  const { t, i18n } = useTranslation();
  return (
    <NativeSelect
      size="sm"
      label={withLabel ? t('nav.language') : undefined}
      aria-label={withLabel ? undefined : t('nav.language')}
      className={withLabel ? undefined : 'w-28'}
      value={i18n.resolvedLanguage}
      onChange={(event) => {
        localStorage.setItem('language', event.target.value);
        void i18n.changeLanguage(event.target.value);
      }}
      options={[
        { value: 'en', label: t('nav.english') },
        { value: 'fr', label: t('nav.french') },
      ]}
    />
  );
}

function ThemeMenu() {
  const { t } = useTranslation();
  const { theme, setTheme } = useTheme();
  const labels: Record<Theme, string> = {
    light: t('nav.lightTheme'),
    dark: t('nav.darkTheme'),
    system: t('nav.systemTheme'),
  };
  return (
    <Dropdown.Root>
      <Button
        color="tertiary"
        size="sm"
        iconLeading={themeIcons[theme]}
        aria-label={`${t('nav.theme')}: ${labels[theme]}`}
      />
      <Dropdown.Popover className="w-48">
        <Dropdown.Menu
          aria-label={t('nav.theme')}
          selectionMode="single"
          selectedKeys={[theme]}
          onAction={(key) => setTheme(key as Theme)}
        >
          {(['light', 'dark', 'system'] as const).map((value) => (
            <Dropdown.Item key={value} id={value} icon={themeIcons[value]} label={labels[value]} />
          ))}
        </Dropdown.Menu>
      </Dropdown.Popover>
    </Dropdown.Root>
  );
}
