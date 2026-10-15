import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { Alert } from './Alert';

/** The plan has no AutoDJ: says so, with a link to the plans. */
export function AutoDjUpgrade({ title }: { title: string }) {
  const { t } = useTranslation();
  return (
    <Alert color="gray" title={title}>
      <Link to="/account" className="font-semibold text-brand-secondary underline">
        {t('media.upgrade')}
      </Link>
    </Alert>
  );
}
