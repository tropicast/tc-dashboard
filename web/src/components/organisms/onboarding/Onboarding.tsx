import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { z } from 'zod';
import { api } from '../../../api/client';
import { selectedTenantId } from '../../../api/client';
import { Field } from '../../atoms/Field';
import { Notice } from '../../atoms/Notice';
import { data } from '../../../lib/request';
import { stationSchema } from '../../../lib/station-validation';
import type { Station, Tenant } from '../../../lib/types';

const tenantSchema = z.object({ name: z.string().min(2), slug: z.string().regex(/^[a-z0-9-]+$/) });
export function Onboarding() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const tenant = useForm({
    resolver: zodResolver(tenantSchema),
    defaultValues: { name: '', slug: '' },
  });
  const station = useForm({
    resolver: zodResolver(stationSchema),
    defaultValues: {
      name: '',
      slug: '',
      description: '',
      genre: '',
      country: '',
      language: '',
      website: '',
      listInDirectory: false,
    },
  });
  const createTenant = useMutation({
    mutationFn: (body: z.infer<typeof tenantSchema>) =>
      data<Tenant>(api.POST('/api/v1/tenants', { body })),
    onSuccess: (createdTenant) => localStorage.setItem('tenant-id', createdTenant.id),
  });
  const createStation = useMutation({
    mutationFn: (value: z.infer<typeof stationSchema>) =>
      data<Station>(
        api.POST('/api/v1/stations', {
          body: {
            ...value,
            description: value.description || null,
            genre: value.genre || null,
            country: value.country || null,
            language: value.language || null,
            website: value.website || null,
          },
        }),
      ),
    onSuccess: () =>
      void queryClient.invalidateQueries({ queryKey: ['stations', selectedTenantId()] }),
  });
  if (!createTenant.isSuccess)
    return (
      <section className="onboarding">
        <h1>{t('onboarding.workspaceTitle')}</h1>
        <p>{t('onboarding.workspaceIntro')}</p>
        <form onSubmit={tenant.handleSubmit((value) => createTenant.mutate(value))}>
          <Field
            label={t('onboarding.organizationName')}
            error={tenant.formState.errors.name?.message}
          >
            <input {...tenant.register('name')} />
          </Field>
          <Field
            label={t('onboarding.workspaceSlug')}
            error={tenant.formState.errors.slug?.message}
          >
            <input {...tenant.register('slug')} />
          </Field>
          <Notice error={createTenant.error} />
          <button>{t('onboarding.createWorkspace')}</button>
        </form>
      </section>
    );
  if (!createStation.isSuccess)
    return (
      <section className="onboarding">
        <h1>{t('onboarding.stationTitle')}</h1>
        <form onSubmit={station.handleSubmit((value) => createStation.mutate(value))}>
          <Field label={t('onboarding.stationName')}>
            <input {...station.register('name')} />
          </Field>
          <Field label={t('onboarding.stationSlug')}>
            <input {...station.register('slug')} />
          </Field>
          <Field label={t('common.description')}>
            <textarea {...station.register('description')} />
          </Field>
          <Field label={t('common.genre')}>
            <input {...station.register('genre')} />
          </Field>
          <Field label={t('common.country')}>
            <input {...station.register('country')} />
          </Field>
          <Field label={t('common.language')}>
            <input {...station.register('language')} />
          </Field>
          <Field label={t('common.website')}>
            <input {...station.register('website')} />
          </Field>
          <label className="checkbox">
            <input type="checkbox" {...station.register('listInDirectory')} />{' '}
            {t('common.listInDirectory')}
          </label>
          <Notice error={createStation.error} />
          <button>{t('onboarding.createStation')}</button>
        </form>
      </section>
    );
  return (
    <section className="onboarding">
      <h1>{t('onboarding.readyTitle')}</h1>
      <p>{t('onboarding.readyText')}</p>
      <Link className="button" to="/stations">
        {t('onboarding.openStations')}
      </Link>
    </section>
  );
}
