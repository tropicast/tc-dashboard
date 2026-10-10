import { expect, test } from 'vitest';
import { stationSchema } from './station-validation';

const validStation = {
  name: 'Radio Mada',
  slug: 'radio-mada',
  country: 'MG',
  language: 'fr-CA',
  website: 'https://radiomada.example',
  listInDirectory: false,
};

test('accepts station metadata matching server validation', () => {
  expect(stationSchema.safeParse(validStation).success).toBe(true);
});

test.each([
  { country: 'MGA' },
  { country: 'M1' },
  { language: 'f' },
  { language: 'fr--CA' },
  { website: 'ftp://radiomada.example' },
  { website: '/relative' },
])('rejects invalid station metadata: %o', (invalid) => {
  expect(stationSchema.safeParse({ ...validStation, ...invalid }).success).toBe(false);
});
