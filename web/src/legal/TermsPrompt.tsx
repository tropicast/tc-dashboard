import { useMutation } from '@tanstack/react-query';
import { Trans, useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { Button } from '@/components/base/buttons/button';
import { Notice } from '../components/atoms/Notice';
import { FormPage } from '../components/molecules/FormPage';
import { queryClient } from '../lib/query-client';
import { data } from '../lib/request';
import legal from './legal.json';

const link = 'font-semibold text-brand-secondary underline';

/**
 * Shown instead of the dashboard when the user has not accepted the current terms version (#94). They can accept,
 * go to the Account page (download their data, delete the account) or sign out.
 */
export function TermsPrompt({ version }: { version: string }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  // This page shows the texts bundled with the app: an older bundle than the API's version must reload first.
  const outdated = version !== legal.termsVersion;
  const accept = useMutation({
    mutationFn: () => data<void>(api.POST('/api/v1/auth/terms', { body: { version } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['me'] }),
  });
  const logout = useMutation({
    mutationFn: () => data<void>(api.POST('/api/v1/auth/logout')),
    onSuccess: () => {
      queryClient.clear();
      navigate('/login');
    },
  });
  return (
    <FormPage title={t('legal.updatedTitle')} description={t('legal.updatedBody')}>
      <p className="text-center text-sm text-tertiary">
        <Trans
          i18nKey="legal.readBoth"
          components={{
            terms: <Link to="/terms" className={link} />,
            privacy: <Link to="/privacy" className={link} />,
          }}
        />
      </p>
      <Notice error={accept.error ?? logout.error} />
      {outdated ? (
        <Button size="lg" onPress={() => window.location.reload()}>
          {t('legal.reload')}
        </Button>
      ) : (
        <Button size="lg" isLoading={accept.isPending} onPress={() => accept.mutate()}>
          {t('legal.accept')}
        </Button>
      )}
      <div className="flex flex-wrap justify-center gap-x-4 gap-y-1 text-sm">
        <Link to="/account" className={link}>
          {t('legal.manageAccount')}
        </Link>
        <Button
          color="link-gray"
          size="sm"
          isLoading={logout.isPending}
          onPress={() => logout.mutate()}
        >
          {t('nav.signOut')}
        </Button>
      </div>
    </FormPage>
  );
}
