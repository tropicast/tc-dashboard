import { LoadingIndicator } from '@/components/application/loading-indicator/loading-indicator';
import { cx } from '@/utils/cx';

/** Untitled UI loading indicator with its label announced to screen readers. */
export function Loading({ label, className }: { label: string; className?: string }) {
  return (
    <div role="status" className={cx('flex justify-center py-12', className)}>
      <LoadingIndicator type="line-spinner" size="md" label={label} />
    </div>
  );
}
