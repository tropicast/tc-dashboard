import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { z } from 'zod';
import { api } from '../../../api/client';
import { Field } from '../../atoms/Field';
import { Notice } from '../../atoms/Notice';
import { FormPage } from '../../molecules/FormPage';
import { queryClient } from '../../../lib/query-client';
import { data } from '../../../lib/request';

const loginSchema = z.object({ email: z.email(), password: z.string().min(8) });

export function Login() {
  const navigate = useNavigate();
  const form = useForm({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '' },
  });
  const mutation = useMutation({
    mutationFn: (body: z.infer<typeof loginSchema>) =>
      data<void>(api.POST('/api/v1/auth/login', { body })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['me'] });
      navigate('/');
    },
  });
  return (
    <FormPage title="Sign in">
      <form onSubmit={form.handleSubmit((value) => mutation.mutate(value))}>
        <Field label="Email" error={form.formState.errors.email?.message}>
          <input autoComplete="email" {...form.register('email')} />
        </Field>
        <Field label="Password" error={form.formState.errors.password?.message}>
          <input type="password" autoComplete="current-password" {...form.register('password')} />
        </Field>
        <Notice error={mutation.error} />
        <button disabled={mutation.isPending}>Sign in</button>
      </form>
      <p>
        <Link to="/signup">Create account</Link> ·{' '}
        <Link to="/forgot-password">Forgot password?</Link>
      </p>
    </FormPage>
  );
}

export function Signup() {
  const form = useForm({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '' },
  });
  const mutation = useMutation({
    mutationFn: (body: z.infer<typeof loginSchema>) =>
      data<void>(api.POST('/api/v1/auth/register', { body })),
  });
  return (
    <FormPage title="Create account">
      <form onSubmit={form.handleSubmit((value) => mutation.mutate(value))}>
        <Field label="Email" error={form.formState.errors.email?.message}>
          <input autoComplete="email" {...form.register('email')} />
        </Field>
        <Field
          label="Password (8 or more characters)"
          error={form.formState.errors.password?.message}
        >
          <input type="password" autoComplete="new-password" {...form.register('password')} />
        </Field>
        <Notice error={mutation.error} />
        <button disabled={mutation.isPending}>
          {mutation.isSuccess ? 'Check your email' : 'Create account'}
        </button>
      </form>
      <p>
        <Link to="/login">Sign in</Link>
      </p>
    </FormPage>
  );
}

export function EmailAction({ kind }: { kind: 'forgot' | 'confirm' }) {
  const [params] = useSearchParams();
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
  });
  return (
    <FormPage title={kind === 'forgot' ? 'Reset your password' : 'Confirm your email'}>
      <form onSubmit={form.handleSubmit((value) => mutation.mutate(value))}>
        {kind === 'forgot' ? (
          <Field label="Email">
            <input autoComplete="email" {...form.register('email', { required: true })} />
          </Field>
        ) : (
          <>
            <Field label="User ID">
              <input {...form.register('userId', { required: true })} />
            </Field>
            <Field label="Confirmation code">
              <input {...form.register('code', { required: true })} />
            </Field>
          </>
        )}
        <Notice error={mutation.error} />
        <button disabled={mutation.isPending}>
          {mutation.isSuccess ? 'Email sent' : 'Continue'}
        </button>
      </form>
    </FormPage>
  );
}

export function Reset() {
  const [params] = useSearchParams();
  const form = useForm({
    resolver: zodResolver(
      z.object({ email: z.email(), code: z.string().min(1), newPassword: z.string().min(8) }),
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
    <FormPage title="Set new password">
      <form onSubmit={form.handleSubmit((value) => mutation.mutate(value))}>
        <Field label="Email">
          <input {...form.register('email')} />
        </Field>
        <Field label="Reset code">
          <input {...form.register('code')} />
        </Field>
        <Field label="New password">
          <input type="password" {...form.register('newPassword')} />
        </Field>
        <Notice error={mutation.error} />
        <button>{mutation.isSuccess ? 'Password updated' : 'Update password'}</button>
      </form>
    </FormPage>
  );
}

export function AcceptInvite() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const form = useForm({ defaultValues: { token: params.get('token') ?? '' } });
  const mutation = useMutation({
    mutationFn: (body: { token: string }) =>
      data<void>(api.POST('/api/v1/invitations/accept', { body })),
    onSuccess: () => navigate('/'),
  });
  return (
    <FormPage title="Join team">
      <form onSubmit={form.handleSubmit((value) => mutation.mutate(value))}>
        <Field label="Invitation token">
          <input {...form.register('token', { required: true })} />
        </Field>
        <Notice error={mutation.error} />
        <button>Accept invitation</button>
      </form>
    </FormPage>
  );
}
