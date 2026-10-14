import type { ReactNode } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { Trans, useTranslation } from 'react-i18next';
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { z } from 'zod';
import { api, refreshAntiforgery } from '../../../api/client';
import { Button } from '@/components/base/buttons/button';
import { CheckboxField, TextField } from '../../atoms/FormFields';
import { Notice } from '../../atoms/Notice';
import { FormPage } from '../../molecules/FormPage';
import { queryClient } from '../../../lib/query-client';
import { data } from '../../../lib/request';
import i18n from '../../../lib/i18n';

const loginSchema = z.object({ email: z.email(), password: z.string().min(12) });
const signupSchema = loginSchema.extend({
  acceptTerms: z.boolean().refine(Boolean, { error: () => i18n.t('legal.mustAccept') }),
});

/** The in-app page the guard sent the visitor from; never another site. */
function returnPath(state: unknown) {
  const from = (state as { from?: unknown } | null)?.from;
  return typeof from === 'string' && from.startsWith('/') && !from.startsWith('//') ? from : '/';
}

export function Login() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const form = useForm({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '' },
  });
  const mutation = useMutation({
    mutationFn: (body: z.infer<typeof loginSchema>) =>
      data<void>(api.POST('/api/v1/auth/login', { body })),
    onSuccess: async () => {
      await refreshAntiforgery();
      await queryClient.invalidateQueries({ queryKey: ['me'] });
      navigate(returnPath(location.state));
    },
  });
  return (
    <FormPage
      title={t('auth.signIn')}
      footer={
        <>
          <TextLink to="/signup">{t('auth.createAccount')}</TextLink>
          <TextLink to="/forgot-password">{t('auth.forgotPassword')}</TextLink>
        </>
      }
    >
      <form
        className="flex flex-col gap-5"
        onSubmit={form.handleSubmit((value) => !mutation.isPending && mutation.mutate(value))}
      >
        <TextField
          control={form.control}
          name="email"
          label={t('common.email')}
          autoComplete="email"
        />
        <TextField
          control={form.control}
          name="password"
          type="password"
          label={t('auth.password')}
          autoComplete="current-password"
          passwordToggleLabel={t('auth.showCharacters')}
        />
        <Notice error={mutation.error} />
        <SubmitButton busy={mutation.isPending}>{t('auth.signIn')}</SubmitButton>
      </form>
    </FormPage>
  );
}

export function Signup() {
  const { t } = useTranslation();
  const form = useForm({
    resolver: zodResolver(signupSchema),
    defaultValues: { email: '', password: '', acceptTerms: false },
  });
  const mutation = useMutation({
    mutationFn: (body: z.infer<typeof signupSchema>) =>
      data<void>(api.POST('/api/v1/auth/register', { body })),
  });
  return (
    <FormPage
      title={t('auth.createAccount')}
      footer={<TextLink to="/login">{t('auth.signIn')}</TextLink>}
    >
      <form
        className="flex flex-col gap-5"
        onSubmit={form.handleSubmit((value) => !mutation.isPending && mutation.mutate(value))}
      >
        <TextField
          control={form.control}
          name="email"
          label={t('common.email')}
          autoComplete="email"
        />
        <TextField
          control={form.control}
          name="password"
          type="password"
          label={t('auth.password')}
          autoComplete="new-password"
          passwordToggleLabel={t('auth.showCharacters')}
        />
        <div className="flex flex-col gap-1.5">
          <CheckboxField control={form.control} name="acceptTerms" label={t('legal.acceptLabel')} />
          <p className="pl-6 text-sm text-tertiary">
            <Trans
              i18nKey="legal.readBoth"
              components={{
                terms: <TextLink to="/terms" />,
                privacy: <TextLink to="/privacy" />,
              }}
            />
          </p>
        </div>
        <Notice error={mutation.error} />
        <SubmitButton busy={mutation.isPending}>
          {mutation.isSuccess ? t('auth.checkEmail') : t('auth.createAccount')}
        </SubmitButton>
      </form>
    </FormPage>
  );
}

export function EmailAction({ kind }: { kind: 'forgot' | 'confirm' }) {
  const { t } = useTranslation();
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const form = useForm({
    defaultValues: {
      email: '',
      code: params.get('code') ?? '',
      userId: params.get('userId') ?? '',
    },
  });
  const mutation = useMutation({
    mutationFn: (value: { email: string; code: string; userId: string }) =>
      kind === 'forgot'
        ? data<void>(api.POST('/api/v1/auth/forgot-password', { body: { email: value.email } }))
        : data<void>(
            api.POST('/api/v1/auth/confirm-email', {
              body: { code: value.code, userId: value.userId },
            }),
          ),
    // A confirmed address goes home (#60); the guard sends a signed-out visitor to sign in.
    onSuccess: () => {
      if (kind === 'confirm') navigate('/', { replace: true });
    },
  });
  return (
    <FormPage title={kind === 'forgot' ? t('auth.resetTitle') : t('auth.confirmTitle')}>
      <form
        className="flex flex-col gap-5"
        onSubmit={form.handleSubmit((value) => !mutation.isPending && mutation.mutate(value))}
      >
        {kind === 'forgot' ? (
          <TextField
            control={form.control}
            name="email"
            rules={{ required: true }}
            label={t('common.email')}
            autoComplete="email"
          />
        ) : (
          <>
            <TextField
              control={form.control}
              name="userId"
              rules={{ required: true }}
              label={t('auth.userId')}
            />
            <TextField
              control={form.control}
              name="code"
              rules={{ required: true }}
              label={t('auth.confirmationCode')}
            />
          </>
        )}
        <Notice error={mutation.error} />
        <SubmitButton busy={mutation.isPending}>
          {mutation.isSuccess ? t('auth.emailSent') : t('common.continue')}
        </SubmitButton>
      </form>
    </FormPage>
  );
}

export function Reset() {
  const { t } = useTranslation();
  const [params] = useSearchParams();
  const form = useForm({
    resolver: zodResolver(
      z.object({ email: z.email(), code: z.string().min(1), newPassword: z.string().min(12) }),
    ),
    defaultValues: {
      email: params.get('email') ?? '',
      code: params.get('code') ?? '',
      newPassword: '',
    },
  });
  const mutation = useMutation({
    mutationFn: (body: { email: string; code: string; newPassword: string }) =>
      data<void>(api.POST('/api/v1/auth/reset-password', { body })),
  });
  return (
    <FormPage title={t('auth.newPasswordTitle')}>
      <form
        className="flex flex-col gap-5"
        onSubmit={form.handleSubmit((value) => !mutation.isPending && mutation.mutate(value))}
      >
        <TextField
          control={form.control}
          name="email"
          label={t('common.email')}
          autoComplete="email"
        />
        <TextField control={form.control} name="code" label={t('auth.resetCode')} />
        <TextField
          control={form.control}
          name="newPassword"
          type="password"
          label={t('auth.newPassword')}
          autoComplete="new-password"
          passwordToggleLabel={t('auth.showCharacters')}
        />
        <Notice error={mutation.error} />
        <SubmitButton busy={mutation.isPending}>
          {mutation.isSuccess ? t('auth.passwordUpdated') : t('auth.updatePassword')}
        </SubmitButton>
      </form>
    </FormPage>
  );
}

export function AcceptInvite() {
  const { t } = useTranslation();
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const form = useForm({ defaultValues: { token: params.get('token') ?? '' } });
  const mutation = useMutation({
    mutationFn: (body: { token: string }) =>
      data<void>(api.POST('/api/v1/invitations/accept', { body })),
    onSuccess: () => navigate('/'),
  });
  return (
    <FormPage title={t('auth.joinTeam')}>
      <form
        className="flex flex-col gap-5"
        onSubmit={form.handleSubmit((value) => !mutation.isPending && mutation.mutate(value))}
      >
        <TextField
          control={form.control}
          name="token"
          rules={{ required: true }}
          label={t('auth.invitationToken')}
        />
        <Notice error={mutation.error} />
        <SubmitButton busy={mutation.isPending}>{t('auth.acceptInvitation')}</SubmitButton>
      </form>
    </FormPage>
  );
}

/**
 * While pending, React Aria renders the button as type="button", so Enter in a one-field form
 * would submit again: each form's onSubmit also ignores a pending mutation.
 */
function SubmitButton({ busy = false, children }: { busy?: boolean; children: string }) {
  return (
    <Button type="submit" size="lg" className="w-full" isLoading={busy} showTextWhileLoading>
      {children}
    </Button>
  );
}

/** Children are optional: <Trans> fills them in. */
function TextLink({ to, children }: { to: string; children?: ReactNode }) {
  return (
    <Link
      to={to}
      className="rounded-xs font-semibold text-brand-secondary outline-focus-ring hover:text-brand-secondary_hover focus-visible:outline-2 focus-visible:outline-offset-2"
    >
      {children}
    </Link>
  );
}
