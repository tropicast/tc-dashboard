import { z } from 'zod';

const country = z.union([
  z.literal(''),
  z.string().regex(/^[A-Za-z]{2}$/, 'Use a two-letter country code, e.g. MG.'),
]);
const language = z.union([
  z.literal(''),
  z
    .string()
    .max(35)
    .regex(/^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$/, 'Use a language tag, e.g. mg or fr-CA.'),
]);
const website = z.union([
  z.literal(''),
  z.url().refine((value) => /^https?:\/\//i.test(value), 'Use an absolute http(s) address.'),
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
