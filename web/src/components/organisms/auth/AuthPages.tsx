import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { z } from 'zod';
import { api, refreshAntiforgery } from '../../../api/client';
import { Field } from '../../atoms/Field';
import { Notice } from '../../atoms/Notice';
import { FormPage } from '../../molecules/FormPage';
import { queryClient } from '../../../lib/query-client';
import { data } from '../../../lib/request';

const loginSchema = z.object({ email: z.email(), password: z.string().min(12) });

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
    <FormPage title={t('auth.signIn')}>
      <form onSubmit={form.handleSubmit((value) => mutation.mutate(value))}>
        <Field label={t('common.email')} error={form.formState.errors.email?.message}>
          <input autoComplete="email" {...form.register('email')} />
        </Field>
        <Field label={t('auth.password')} error={form.formState.errors.password?.message}>
          <input type="password" autoComplete="current-password" {...form.register('password')} />
        </Field>
        <Notice error={mutation.error} />
        <button disabled={mutation.isPending}>{t('auth.signIn')}</button>
      </form>
      <p>
        <Link to="/signup">{t('auth.createAccount')}</Link> ·{' '}
        <Link to="/forgot-password">{t('auth.forgotPassword')}</Link>
      </p>
    </FormPage>
  );
}

export function Signup() {
  const { t } = useTranslation();
  const form = useForm({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '' },
  });
  const mutation = useMutation({
    mutationFn: (body: z.infer<typeof loginSchema>) =>
      data<void>(api.POST('/api/v1/auth/register', { body })),
  });
  return (
    <FormPage title={t('auth.createAccount')}>
      <form onSubmit={form.handleSubmit((value) => mutation.mutate(value))}>
        <Field label={t('common.email')} error={form.formState.errors.email?.message}>
          <input autoComplete="email" {...form.register('email')} />
        </Field>
        <Field label={t('auth.password')} error={form.formState.errors.password?.message}>
          <input type="password" autoComplete="new-password" {...form.register('password')} />
        </Field>
        <Notice error={mutation.error} />
        <button disabled={mutation.isPending}>
          {mutation.isSuccess ? t('auth.checkEmail') : t('auth.createAccount')}
        </button>
      </form>
      <p>
        <Link to="/login">{t('auth.signIn')}</Link>
      </p>
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
      <form onSubmit={form.handleSubmit((value) => mutation.mutate(value))}>
        {kind === 'forgot' ? (
          <Field label={t('common.email')}>
            <input autoComplete="email" {...form.register('email', { required: true })} />
          </Field>
        ) : (
          <>
            <Field label={t('auth.userId')}>
              <input {...form.register('userId', { required: true })} />
            </Field>
            <Field label={t('auth.confirmationCode')}>
              <input {...form.register('code', { required: true })} />
            </Field>
          </>
        )}
        <Notice error={mutation.error} />
        <button disabled={mutation.isPending}>
          {mutation.isSuccess ? t('auth.emailSent') : t('common.continue')}
        </button>
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
      <form onSubmit={form.handleSubmit((value) => mutation.mutate(value))}>
        <Field label={t('common.email')}>
          <input {...form.register('email')} />
        </Field>
        <Field label={t('auth.resetCode')}>
          <input {...form.register('code')} />
        </Field>
        <Field label={t('auth.newPassword')}>
          <input type="password" {...form.register('newPassword')} />
        </Field>
        <Notice error={mutation.error} />
        <button>{mutation.isSuccess ? t('auth.passwordUpdated') : t('auth.updatePassword')}</button>
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
      <form onSubmit={form.handleSubmit((value) => mutation.mutate(value))}>
        <Field label={t('auth.invitationToken')}>
          <input {...form.register('token', { required: true })} />
        </Field>
        <Notice error={mutation.error} />
        <button>{t('auth.acceptInvitation')}</button>
      </form>
    </FormPage>
  );
}
