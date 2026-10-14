import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useLocation } from 'react-router-dom';
import { useDocumentTitle } from '@/hooks/use-document-title';
import { legalTexts, type LegalSection } from './content';
import legal from './legal.json';
import { LegalLinks } from './LegalLinks';

/** The Privacy Policy or the Terms of Service, public and in the interface language (#94). */
export function LegalPage({ document }: { document: 'privacy' | 'terms' }) {
  const { t, i18n } = useTranslation();
  const texts = legalTexts[i18n.language.startsWith('fr') ? 'fr' : 'en'];
  const title = t(document === 'privacy' ? 'legal.privacy' : 'legal.terms');
  useDocumentTitle(title);
  const { hash } = useLocation();
  useEffect(() => {
    if (hash) window.document.getElementById(hash.slice(1))?.scrollIntoView();
  }, [hash]);
  const date = new Date(`${legal.termsVersion}T00:00:00Z`).toLocaleDateString(i18n.language, {
    dateStyle: 'long',
    timeZone: 'UTC',
  });
  return (
    <main className="flex min-h-dvh flex-col items-center bg-primary px-4 py-12">
      <article className="flex w-full max-w-180 flex-col gap-8">
        <header className="flex flex-col gap-4">
          <Link
            to="/"
            className="self-start rounded-xs outline-focus-ring focus-visible:outline-2 focus-visible:outline-offset-2"
          >
            <img src="/tropicast-logo.svg" alt={t('legal.back')} className="h-10 w-auto" />
          </Link>
          <h1 className="text-display-xs font-semibold text-primary sm:text-display-sm">{title}</h1>
          <p className="text-sm text-tertiary">{t('legal.version', { date })}</p>
          {legal.draft && (
            <p
              role="note"
              className="rounded-lg bg-warning-primary p-3 text-sm text-warning-primary"
            >
              {t('legal.draft')}
            </p>
          )}
        </header>
        {texts[document].map((section) => (
          <Section key={section.heading} section={section}>
            {section.id === 'cookies' && <StorageTable />}
          </Section>
        ))}
        <LegalLinks className="border-t border-secondary pt-6" />
      </article>
    </main>
  );
}

function Section({ section, children }: { section: LegalSection; children?: React.ReactNode }) {
  return (
    <section id={section.id} className="flex scroll-mt-6 flex-col gap-3 text-md text-secondary">
      <h2 className="text-lg font-semibold text-primary">{section.heading}</h2>
      {section.paragraphs?.map((paragraph) => (
        <p key={paragraph}>{paragraph}</p>
      ))}
      {section.list && (
        <ul className="flex list-disc flex-col gap-2 pl-6">
          {section.list.map((item) => (
            <li key={item}>{item}</li>
          ))}
        </ul>
      )}
      {children}
    </section>
  );
}

/** Every cookie and local storage entry, from legal.json (an API test checks no other cookie is set). */
function StorageTable() {
  const { t, i18n } = useTranslation();
  const texts = legalTexts[i18n.language.startsWith('fr') ? 'fr' : 'en'];
  return (
    <div className="overflow-x-auto rounded-lg ring-1 ring-secondary">
      <table className="w-full text-left text-sm">
        <caption className="sr-only">{t('legal.storageTitle')}</caption>
        <thead className="bg-secondary text-xs font-semibold text-tertiary">
          <tr>
            <th scope="col" className="px-4 py-3">
              {t('legal.name')}
            </th>
            <th scope="col" className="px-4 py-3">
              {t('legal.type')}
            </th>
            <th scope="col" className="px-4 py-3">
              {t('legal.purpose')}
            </th>
            <th scope="col" className="px-4 py-3">
              {t('legal.duration')}
            </th>
          </tr>
        </thead>
        <tbody>
          {legal.cookies.map((entry) => (
            <tr key={entry.name} className="border-t border-secondary">
              <th scope="row" className="px-4 py-3 font-mono font-normal text-primary">
                {entry.name}
              </th>
              <td className="px-4 py-3">
                {t(entry.kind === 'cookie' ? 'legal.cookie' : 'legal.localStorage')}
              </td>
              <td className="px-4 py-3">{texts.storage[entry.name]}</td>
              <td className="px-4 py-3">{texts.lifetimes[entry.lifetime]}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
