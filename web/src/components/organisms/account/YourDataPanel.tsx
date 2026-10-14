import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { Download01, Trash01 } from '@untitledui/icons';
import { api } from '../../../api/client';
import { Button } from '@/components/base/buttons/button';
import { TextField } from '../../atoms/FormFields';
import { Notice } from '../../atoms/Notice';
import { Card } from '../../molecules/Card';
import { ConfirmDialog } from '../../molecules/ConfirmDialog';
import { queryClient } from '../../../lib/query-client';
import { data } from '../../../lib/request';

/** GDPR rights on the Account page (#94): download everything stored about the user, or delete the account. */
export function YourDataPanel() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [confirming, setConfirming] = useState(false);
  const form = useForm({ defaultValues: { password: '' } });
  const remove = useMutation({
    mutationFn: (password: string) =>
      data<void>(api.DELETE('/api/v1/auth/account', { body: { password } })),
    onSuccess: () => {
      localStorage.removeItem('tenant-id');
      queryClient.clear();
      navigate('/login', { replace: true });
    },
  });
  return (
    <Card title={t('account.yourData')} description={t('account.yourDataHint')}>
      <div className="flex flex-wrap gap-3">
        {/* A plain download: the browser saves the API's attachment (file name, no-store) itself. */}
        <Button
          color="secondary"
          size="md"
          iconLeading={Download01}
          href="/api/v1/auth/account/export"
          download
        >
          {t('account.exportData')}
        </Button>
        <Button
          color="secondary-destructive"
          size="md"
          iconLeading={Trash01}
          onPress={() => setConfirming(true)}
        >
          {t('account.deleteAccount')}
        </Button>
      </div>
      {confirming && (
        <ConfirmDialog
          title={t('account.deleteTitle')}
          confirmLabel={t('account.deleteAccount')}
          busy={remove.isPending}
          onConfirm={form.handleSubmit(({ password }) => remove.mutate(password))}
          onCancel={() => {
            setConfirming(false);
            remove.reset();
            form.reset();
          }}
        >
          <p>{t('account.deleteBody')}</p>
          <TextField
            control={form.control}
            name="password"
            rules={{ required: true }}
            type="password"
            label={t('account.currentPassword')}
            autoComplete="current-password"
            passwordToggleLabel={t('auth.showCharacters')}
          />
          <Notice error={remove.error} />
        </ConfirmDialog>
      )}
    </Card>
  );
}
