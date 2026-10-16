import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { ArrowRight, Building07, CheckCircle, Signal01 } from '@untitledui/icons';
import type { FC, ReactNode } from 'react';
import { Button } from '@/components/base/buttons/button';
import { FeaturedIcon } from '@/components/foundations/featured-icon/featured-icon';
import { Card } from '../../molecules/Card';
import { DownloadApp } from '../downloads/DownloadApp';
import { z } from 'zod';
import { api } from '../../../api/client';
import { selectedTenantId } from '../../../api/client';
import { CheckboxField, TextAreaField, TextField } from '../../atoms/FormFields';
import { Notice } from '../../atoms/Notice';
import { data } from '../../../lib/request';
import { stationSchema } from '../../../lib/station-validation';
import type { Station, Tenant } from '../../../lib/types';
import { useDocumentTitle } from '@/hooks/use-document-title';

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
      <Step
        step={1}
        icon={Building07}
        title={t('onboarding.workspaceTitle')}
        description={t('onboarding.workspaceIntro')}
      >
        <form
          className="flex flex-col gap-5"
          onSubmit={tenant.handleSubmit(
            (value) => !createTenant.isPending && createTenant.mutate(value),
          )}
        >
          <TextField
            control={tenant.control}
            name="name"
            label={t('onboarding.organizationName')}
          />
          <TextField control={tenant.control} name="slug" label={t('onboarding.workspaceSlug')} />
          <Notice error={createTenant.error} />
          <Button type="submit" size="lg" isLoading={createTenant.isPending} showTextWhileLoading>
            {t('onboarding.createWorkspace')}
          </Button>
        </form>
      </Step>
    );
  if (!createStation.isSuccess)
    return (
      <Step step={2} icon={Signal01} title={t('onboarding.stationTitle')}>
        <form
          className="grid gap-5 sm:grid-cols-2"
          onSubmit={station.handleSubmit(
            (value) => !createStation.isPending && createStation.mutate(value),
          )}
        >
          <TextField control={station.control} name="name" label={t('onboarding.stationName')} />
          <TextField control={station.control} name="slug" label={t('onboarding.stationSlug')} />
          <div className="sm:col-span-2">
            <TextAreaField
              control={station.control}
              name="description"
              label={t('common.description')}
            />
          </div>
          <TextField control={station.control} name="genre" label={t('common.genre')} />
          <TextField control={station.control} name="country" label={t('common.country')} />
          <TextField control={station.control} name="language" label={t('common.language')} />
          <TextField control={station.control} name="website" label={t('common.website')} />
          <div className="sm:col-span-2">
            <CheckboxField
              control={station.control}
              name="listInDirectory"
              label={t('common.listInDirectory')}
            />
          </div>
          <div className="flex flex-col gap-5 sm:col-span-2">
            <Notice error={createStation.error} />
            <Button
              type="submit"
              size="lg"
              isLoading={createStation.isPending}
              showTextWhileLoading
            >
              {t('onboarding.createStation')}
            </Button>
          </div>
        </form>
      </Step>
    );
  return (
    <Step
      step={3}
      icon={CheckCircle}
      color="success"
      title={t('onboarding.readyTitle')}
      description={t('onboarding.readyText')}
    >
      <DownloadApp />
      <Button href="/stations" size="lg" color="secondary" iconTrailing={ArrowRight}>
        {t('onboarding.openStations')}
      </Button>
    </Step>
  );
}

/** One onboarding step: progress, featured icon, heading and the step's content. */
function Step({
  step,
  icon,
  color = 'brand',
  title,
  description,
  children,
}: {
  step: number;
  icon: FC<{ className?: string }>;
  color?: 'brand' | 'success';
  title: string;
  description?: string;
  children: ReactNode;
}) {
  const { t } = useTranslation();
  useDocumentTitle(title);
  return (
    <section className="mx-auto flex max-w-xl flex-col gap-8">
      <div className="flex flex-col gap-4">
        <ol aria-label={t('onboarding.step', { current: step, total: 3 })} className="flex gap-2">
          {[1, 2, 3].map((index) => (
            <li
              key={index}
              className={
                index <= step
                  ? 'h-1.5 flex-1 rounded-full bg-brand-solid'
                  : 'h-1.5 flex-1 rounded-full bg-quaternary'
              }
            />
          ))}
        </ol>
        <p className="text-sm font-medium text-tertiary">
          {t('onboarding.step', { current: step, total: 3 })}
        </p>
      </div>
      <div className="flex flex-col gap-4">
        <FeaturedIcon icon={icon} color={color} theme="modern" size="lg" />
        <div className="flex flex-col gap-1">
          <h1 className="text-display-xs font-semibold text-primary">{title}</h1>
          {description && <p className="text-md text-tertiary">{description}</p>}
        </div>
      </div>
      <Card>{children}</Card>
    </section>
  );
}
