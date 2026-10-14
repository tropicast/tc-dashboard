import { useMutation } from '@tanstack/react-query';
import { Trans, useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import { Button } from '@/components/base/buttons/button';
import { Notice } from '../components/atoms/Notice';
import { FormPage } from '../components/molecules/FormPage';
import { queryClient } from '../lib/query-client';
import { data } from '../lib/request';
import legal from './legal.json';

/** Shown instead of the dashboard when the user has not accepted the current terms version (#94). */
export function TermsPrompt() {
  const { t } = useTranslation();
  const mutation = useMutation({
    mutationFn: () =>
      data<void>(api.POST('/api/v1/auth/terms', { body: { version: legal.termsVersion } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['me'] }),
  });
  return (
    <FormPage title={t('legal.updatedTitle')} description={t('legal.updatedBody')}>
      <p className="text-center text-sm text-tertiary">
        <Trans
          i18nKey="legal.readBoth"
          components={{
            terms: <Link to="/terms" className="font-semibold text-brand-secondary underline" />,
            privacy: (
              <Link to="/privacy" className="font-semibold text-brand-secondary underline" />
            ),
          }}
        />
      </p>
      <Notice error={mutation.error} />
      <Button size="lg" isLoading={mutation.isPending} onPress={() => mutation.mutate()}>
        {t('legal.accept')}
      </Button>
    </FormPage>
  );
}
