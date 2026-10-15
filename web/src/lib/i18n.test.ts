import { afterEach, expect, test } from 'vitest';
import { en } from '../locales/en';
import { fr } from '../locales/fr';
import i18n, { formatBytes, formatListeningTime, formatStorage, formatTrackDuration } from './i18n';

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

test('the page language follows the interface language (#68)', async () => {
  await i18n.changeLanguage('fr');
  expect(document.documentElement.lang).toBe('fr');
  await i18n.changeLanguage('en');
  expect(document.documentElement.lang).toBe('en');
});

test.each([
  [0, '0 B', '0 o'],
  [950, '950 B', '950 o'],
  [123_456, '123.5 kB', '123,5 ko'],
  [1_534_000_000, '1.5 GB', '1,5 Go'],
  [1_534_270_000_000, '1.5 TB', '1,5 To'],
])('formatBytes(%d) is "%s" in English, "%s" in French (#75)', async (bytes, english, french) => {
  expect(formatBytes(bytes)).toBe(english);
  await i18n.changeLanguage('fr');
  expect(formatBytes(bytes).replace(/\s/g, ' ')).toBe(french);
});

test.each([
  [0.25, '15 min', '15 min'],
  [3.4166, '3 h 25 min', '3 h 25 min'],
  [2, '2 h', '2 h'],
  [1250.6, '1,251 h', '1 251 h'],
])(
  'formatListeningTime(%d) is "%s" in English, "%s" in French (#75)',
  async (hours, english, french) => {
    expect(formatListeningTime(hours)).toBe(english);
    await i18n.changeLanguage('fr');
    expect(formatListeningTime(hours).replace(/\s/g, ' ')).toBe(french);
  },
);

test('stored audio and track lengths read naturally', () => {
  expect(formatStorage(37 * 1024)).toBe('37 kB');
  expect(formatStorage(512 * 1024 ** 2)).toBe('512 MB');
  expect(formatStorage(1024 ** 3)).toBe('1 GB');
  expect(formatTrackDuration(185_000)).toBe('3:05');
  expect(formatTrackDuration(3_729_000)).toBe('1:02:09');
});
