import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import { z } from 'zod';
import { en } from '../locales/en';
import { fr } from '../locales/fr';

declare module 'i18next' {
  interface CustomTypeOptions {
    resources: { translation: typeof en };
  }
}

type Texts = { [key: string]: string | Texts };

/**
 * French has a "many" plural category (1,000,000 and up) that English lacks. Without a `_many` form, i18next shows the
 * raw key, so each `_other` form is copied to `_many`.
 */
function withManyForms(texts: Texts): Texts {
  const result: Texts = {};
  for (const [key, value] of Object.entries(texts)) {
    result[key] = typeof value === 'string' ? value : withManyForms(value);
    if (typeof value === 'string' && key.endsWith('_other'))
      result[key.replace(/_other$/, '_many')] = value;
  }
  return result;
}

/** Zod's own messages (e.g. "too short") follow the language. */
function syncValidationLanguage(language: string) {
  z.config(language.startsWith('fr') ? z.locales.fr() : z.locales.en());
}

void i18n.use(initReactI18next).init({
  resources: {
    en: { translation: en },
    fr: { translation: withManyForms(fr) },
  },
  lng: localStorage.getItem('language') ?? 'en',
  fallbackLng: 'en',
  debug: false,
  interpolation: { escapeValue: false },
});
/** Screen readers pick their pronunciation from `<html lang>` (WCAG 3.1.1). */
function syncDocumentLanguage(language: string) {
  document.documentElement.lang = language.startsWith('fr') ? 'fr' : 'en';
}

function onLanguage(language: string) {
  syncValidationLanguage(language);
  syncDocumentLanguage(language);
}
onLanguage(i18n.language);
i18n.on('languageChanged', onLanguage);

/** Formats a date and time in the interface language. */
export function formatDateTime(value: string | number | Date) {
  return new Date(value).toLocaleString(i18n.language);
}

/** Formats a date in the interface language. */
export function formatDate(value: string | number | Date) {
  return new Date(value).toLocaleDateString(i18n.language);
}

const byteUnits = ['kilobyte', 'megabyte', 'gigabyte', 'terabyte', 'petabyte'] as const;

/** Bytes in decimal units (as for bandwidth), at most one decimal: 123.5 kB, 1.5 GB (1,5 Go in French). */
export function formatBytes(bytes: number) {
  let value = Math.max(0, Math.round(bytes));
  // Intl's short "byte" is the word ("950 byte"): use the symbol (B, o) below 1 kB.
  if (value < 1000) return i18n.t('stats.bytes', { value: formatNumber(value) });
  let unit = -1;
  while (value >= 1000 && unit < byteUnits.length - 1) {
    value /= 1000;
    unit += 1;
  }
  return new Intl.NumberFormat(i18n.language, {
    style: 'unit',
    unit: byteUnits[unit],
    unitDisplay: 'short',
    maximumFractionDigits: 1,
  }).format(value);
}

/** Listening time from hours: 15 min, 3 h 25 min, then whole hours past one day (1,251 h). */
export function formatListeningTime(hours: number) {
  const number = (value: number) => new Intl.NumberFormat(i18n.language).format(value);
  const minutes = Math.round(Math.max(0, hours) * 60);
  if (minutes < 60) return i18n.t('stats.duration.minutes', { minutes: number(minutes) });
  if (minutes < 24 * 60) {
    const rest = minutes % 60;
    const whole = Math.floor(minutes / 60);
    return rest === 0
      ? i18n.t('stats.duration.hours', { hours: number(whole) })
      : i18n.t('stats.duration.hoursMinutes', { hours: number(whole), minutes: number(rest) });
  }
  return i18n.t('stats.duration.hours', { hours: number(Math.round(hours)) });
}

/** A plain number in the interface language, with its grouping and decimal separators. */
export function formatNumber(value: number, maximumFractionDigits = 0) {
  return new Intl.NumberFormat(i18n.language, { maximumFractionDigits }).format(value);
}

export default i18n;
