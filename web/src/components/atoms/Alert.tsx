import type { FC, ReactNode } from 'react';
import { AlertCircle, AlertTriangle, CheckCircle, InfoCircle } from '@untitledui/icons';
import { FeaturedIcon } from '@/components/foundations/featured-icon/featured-icon';
import { cx } from '@/utils/cx';

type Color = 'error' | 'warning' | 'success' | 'gray';

const styles: Record<Color, { root: string; icon: FC<{ className?: string }> }> = {
  error: { root: 'border-error_subtle bg-error-primary', icon: AlertCircle },
  warning: { root: 'border-primary bg-warning-primary', icon: AlertTriangle },
  success: { root: 'border-primary bg-success-primary', icon: CheckCircle },
  gray: { root: 'border-primary bg-secondary', icon: InfoCircle },
};

/** Untitled UI's alert: featured icon, a bold line, then supporting content. */
export function Alert({
  color,
  title,
  children,
  role = color === 'error' ? 'alert' : 'status',
  className,
}: {
  color: Color;
  title: ReactNode;
  children?: ReactNode;
  role?: 'alert' | 'status';
  className?: string;
}) {
  const { root, icon } = styles[color];
  return (
    <div role={role} className={cx('flex gap-3 rounded-xl border p-4 shadow-xs', root, className)}>
      <FeaturedIcon icon={icon} color={color} theme="outline" size="sm" className="m-1.5" />
      <div className="flex min-w-0 flex-1 flex-col gap-1 text-sm">
        <p className="font-semibold text-primary">{title}</p>
        {children && <div className="text-secondary">{children}</div>}
      </div>
    </div>
  );
}
