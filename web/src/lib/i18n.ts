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

export default i18n;
