import type { ReactNode } from 'react';

/** Untitled UI page header: optional breadcrumb, title, supporting text and actions. */
export function PageHeader({
  title,
  description,
  breadcrumb,
  badge,
  actions,
}: {
  title: ReactNode;
  description?: ReactNode;
  breadcrumb?: ReactNode;
  badge?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <div className="mb-8 flex flex-col gap-4 border-b border-secondary pb-5">
      {breadcrumb}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-start">
        <div className="flex min-w-0 flex-1 flex-col gap-1">
          <div className="flex flex-wrap items-center gap-3">
            <h1 className="text-display-xs font-semibold text-primary">{title}</h1>
            {badge}
          </div>
          {description && <p className="text-md text-tertiary">{description}</p>}
        </div>
        {actions && <div className="flex shrink-0 items-center gap-3">{actions}</div>}
      </div>
    </div>
  );
}
