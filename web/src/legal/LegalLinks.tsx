import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';

const link =
  'rounded-xs text-tertiary underline-offset-2 outline-focus-ring hover:text-secondary hover:underline focus-visible:outline-2 focus-visible:outline-offset-2';

/** Footer links to the legal pages, on every SPA page (#94). */
export function LegalLinks({ className = '' }: { className?: string }) {
  const { t } = useTranslation();
  return (
    <nav
      aria-label={t('legal.links')}
      className={`flex flex-wrap justify-center gap-x-4 gap-y-1 text-sm ${className}`}
    >
      <Link to="/privacy" className={link}>
        {t('legal.privacy')}
      </Link>
      <Link to="/terms" className={link}>
        {t('legal.terms')}
      </Link>
      <Link to="/privacy#cookies" className={link}>
        {t('legal.cookies')}
      </Link>
    </nav>
  );
}
