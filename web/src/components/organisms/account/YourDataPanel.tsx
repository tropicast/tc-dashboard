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
  const download = useMutation({
    mutationFn: async () => {
      const exported = await data<unknown>(api.GET('/api/v1/auth/account/export'));
      const url = URL.createObjectURL(
        new Blob([JSON.stringify(exported, null, 2)], { type: 'application/json' }),
      );
      const link = document.createElement('a');
      link.href = url;
      link.download = `tropicast-account-${new Date().toISOString().slice(0, 10)}.json`;
      link.click();
      URL.revokeObjectURL(url);
    },
  });
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
        <Button
          color="secondary"
          size="md"
          iconLeading={Download01}
          isLoading={download.isPending}
          showTextWhileLoading
          onPress={() => download.mutate()}
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
      <Notice error={download.error} />
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
