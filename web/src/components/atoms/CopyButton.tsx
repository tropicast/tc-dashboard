import { useState } from 'react';
import { useTranslation } from 'react-i18next';

export function CopyButton({ value }: { value: string }) {
  const { t } = useTranslation();
  const [copied, setCopied] = useState(false);
  return (
    <button
      className="quiet"
      onClick={() => void navigator.clipboard.writeText(value).then(() => setCopied(true))}
    >
      {copied ? t('common.copied') : t('common.copy')}
    </button>
  );
}
