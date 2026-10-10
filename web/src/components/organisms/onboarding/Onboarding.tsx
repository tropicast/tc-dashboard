import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { Link } from 'react-router-dom';
import { z } from 'zod';
import { api } from '../../../api/client';
import { Field } from '../../atoms/Field';
import { Notice } from '../../atoms/Notice';
import { data } from '../../../lib/request';
import type { Station, Tenant } from '../../../lib/types';

const tenantSchema = z.object({ name: z.string().min(2), slug: z.string().regex(/^[a-z0-9-]+$/) });
const stationSchema = z.object({
  name: z.string().min(2),
  slug: z.string().regex(/^[a-z0-9-]+$/),
  description: z.string().optional(),
  genre: z.string().optional(),
  country: z.string().max(2).optional(),
  language: z.string().optional(),
  website: z.union([z.literal(''), z.url()]).optional(),
  listInDirectory: z.boolean(),
});

export function Onboarding() {
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
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['stations'] }),
  });
  if (!createTenant.isSuccess)
    return (
      <section className="onboarding">
        <h1>Create your workspace</h1>
        <p>Start with the organization that owns your stations.</p>
        <form onSubmit={tenant.handleSubmit((value) => createTenant.mutate(value))}>
          <Field label="Organization name" error={tenant.formState.errors.name?.message}>
            <input {...tenant.register('name')} />
          </Field>
          <Field label="Workspace slug" error={tenant.formState.errors.slug?.message}>
            <input {...tenant.register('slug')} />
          </Field>
          <Notice error={createTenant.error} />
          <button>Create workspace</button>
        </form>
      </section>
    );
  if (!createStation.isSuccess)
    return (
      <section className="onboarding">
        <h1>Create first station</h1>
        <form onSubmit={station.handleSubmit((value) => createStation.mutate(value))}>
          <Field label="Station name">
            <input {...station.register('name')} />
          </Field>
          <Field label="Station slug">
            <input {...station.register('slug')} />
          </Field>
          <Field label="Description">
            <textarea {...station.register('description')} />
          </Field>
          <Field label="Genre">
            <input {...station.register('genre')} />
          </Field>
          <Field label="Country">
            <input {...station.register('country')} />
          </Field>
          <Field label="Language">
            <input {...station.register('language')} />
          </Field>
          <Field label="Website">
            <input {...station.register('website')} />
          </Field>
          <label className="checkbox">
            <input type="checkbox" {...station.register('listInDirectory')} /> List in directory
          </label>
          <Notice error={createStation.error} />
          <button>Create station</button>
        </form>
      </section>
    );
  return (
    <section className="onboarding">
      <h1>Station ready</h1>
      <p>
        Install Tropicast Desktop, then add a broadcast device under your station. It will receive a
        one-time secret for linking.
      </p>
      <Link className="button" to="/stations">
        Open stations
      </Link>
    </section>
  );
}
