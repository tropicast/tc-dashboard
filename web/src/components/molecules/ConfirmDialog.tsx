import type { ReactNode } from 'react';
import { AlertTriangle } from '@untitledui/icons';
import { Heading } from 'react-aria-components';
import { useTranslation } from 'react-i18next';
import { Dialog, Modal, ModalOverlay } from '@/components/application/modals/modal';
import { Button } from '@/components/base/buttons/button';
import { FeaturedIcon } from '@/components/foundations/featured-icon/featured-icon';

/** A destructive confirmation (Untitled UI modal). React Aria traps focus; Escape or the backdrop cancels. */
export function ConfirmDialog({
  title,
  confirmLabel,
  busy = false,
  onConfirm,
  onCancel,
  children,
}: {
  title: string;
  confirmLabel: string;
  busy?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
  children: ReactNode;
}) {
  const { t } = useTranslation();
  return (
    // Not dismissable while busy: the request's error shows inside the dialog.
    <ModalOverlay
      isOpen
      isDismissable={!busy}
      isKeyboardDismissDisabled={busy}
      onOpenChange={(open) => !open && onCancel()}
    >
      <Modal className="max-w-100">
        <Dialog>
          <div className="flex w-full flex-col gap-4 p-4 sm:p-6">
            <FeaturedIcon icon={AlertTriangle} color="error" theme="light" size="lg" />
            <div className="flex flex-col gap-1">
              <Heading slot="title" className="text-lg font-semibold text-primary">
                {title}
              </Heading>
              <div className="flex flex-col gap-3 text-sm text-tertiary">{children}</div>
            </div>
            <div className="flex flex-col-reverse gap-3 pt-4 sm:grid sm:grid-cols-2">
              <Button color="secondary" size="lg" isDisabled={busy} onPress={onCancel}>
                {t('common.cancel')}
              </Button>
              <Button
                color="primary-destructive"
                size="lg"
                isLoading={busy}
                showTextWhileLoading
                onPress={onConfirm}
              >
                {confirmLabel}
              </Button>
            </div>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
