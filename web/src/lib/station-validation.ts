import { z } from 'zod';
import i18n from './i18n';

// Messages are functions: they are read when a form validates, in the current language.

const country = z.union([
  z.literal(''),
  z.string().regex(/^[A-Za-z]{2}$/, { error: () => i18n.t('validation.country') }),
]);
const language = z.union([
  z.literal(''),
  z
    .string()
    .max(35)
    .regex(/^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$/, { error: () => i18n.t('validation.language') }),
]);
const website = z.union([
  z.literal(''),
  z
    .url()
    .refine((value) => /^https?:\/\//i.test(value), { error: () => i18n.t('validation.website') }),
]);

export const stationSchema = z.object({
  name: z.string().min(2),
  slug: z.string().regex(/^[a-z0-9-]+$/),
  description: z.string().optional(),
  genre: z.string().optional(),
  country,
  language,
  website,
  listInDirectory: z.boolean(),
});
