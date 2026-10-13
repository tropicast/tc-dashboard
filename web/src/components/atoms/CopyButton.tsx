import { Check, Copy01 } from '@untitledui/icons';
import { useTranslation } from 'react-i18next';
import { ButtonUtility } from '@/components/base/buttons/button-utility';
import { useClipboard } from '@/hooks/use-clipboard';

export function CopyButton({ value }: { value: string }) {
  const { t } = useTranslation();
  const { copied, copy } = useClipboard();
  return (
    <ButtonUtility
      size="sm"
      color="tertiary"
      icon={copied ? Check : Copy01}
      tooltip={copied ? t('common.copied') : t('common.copy')}
      onPress={() => void copy(value)}
    />
  );
}
