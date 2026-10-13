import { afterEach, expect, test } from 'vitest';
import { en } from '../locales/en';
import { fr } from '../locales/fr';
import i18n from './i18n';

type Texts = { [key: string]: string | Texts };

function keys(texts: Texts, prefix = ''): string[] {
  return Object.entries(texts).flatMap(([key, value]) =>
    typeof value === 'string' ? [`${prefix}${key}`] : keys(value, `${prefix}${key}.`),
  );
}

function placeholders(text: string) {
  return [...text.matchAll(/{{(\w+)}}|<(\w+)>/g)].map((match) => match[1] ?? match[2]).sort();
}

afterEach(() => i18n.changeLanguage('en'));

test('French has exactly the English keys, none empty', () => {
  expect(keys(fr).sort()).toEqual(keys(en).sort());
  for (const key of keys(fr)) {
    expect(i18n.getFixedT('fr')(key as never), key).not.toBe('');
  }
});

test('every French text keeps the placeholders and tags of its English text', () => {
  const lookup = (texts: Texts, key: string) =>
    key.split('.').reduce<string | Texts>((node, part) => (node as Texts)[part], texts) as string;
  for (const key of keys(en)) {
    expect(placeholders(lookup(fr, key)), key).toEqual(placeholders(lookup(en, key)));
  }
});

test('French counts follow French plural rules, including large numbers', async () => {
  await i18n.changeLanguage('fr');
  expect(i18n.t('common.listeners', { count: 0 })).toBe('0 auditeur');
  expect(i18n.t('common.listeners', { count: 1 })).toBe('1 auditeur');
  expect(i18n.t('common.listeners', { count: 2 })).toBe('2 auditeurs');
  expect(i18n.t('common.listeners', { count: 1_000_000 })).toBe('1000000 auditeurs');
});
