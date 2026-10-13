import type { ReactNode } from 'react';
import { cx } from '@/utils/cx';

/** Untitled UI card: header (title, supporting text, actions) over a padded body. */
export function Card({
  title,
  description,
  actions,
  children,
  className,
  bodyClassName,
}: {
  title?: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  children?: ReactNode;
  className?: string;
  bodyClassName?: string;
}) {
  return (
    <div className={cx('rounded-xl bg-primary shadow-xs ring-1 ring-secondary', className)}>
      {title && (
        <div className="flex flex-col gap-4 border-b border-secondary px-4 py-5 sm:flex-row sm:items-start md:px-6">
          <div className="flex min-w-0 flex-1 flex-col gap-1">
            <h2 className="text-lg font-semibold text-primary">{title}</h2>
            {description && <p className="text-sm text-tertiary">{description}</p>}
          </div>
          {actions && <div className="flex shrink-0 items-center gap-3">{actions}</div>}
        </div>
      )}
      {children && (
        <div className={cx('flex flex-col gap-4 px-4 py-5 md:px-6', bodyClassName)}>{children}</div>
      )}
    </div>
  );
}
