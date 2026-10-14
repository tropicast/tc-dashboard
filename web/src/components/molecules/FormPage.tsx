import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { useDocumentTitle } from '@/hooks/use-document-title';
import { LegalLinks } from '../../legal/LegalLinks';

/** Untitled UI log in / sign up layout: logo, heading and supporting text over a narrow form. */
export function FormPage({
  title,
  description,
  children,
  footer,
}: {
  title: string;
  description?: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
}) {
  useDocumentTitle(title);
  return (
    <main className="flex min-h-dvh flex-col items-center bg-primary px-4 py-12 sm:py-24">
      <div className="flex w-full max-w-90 flex-col gap-8">
        <div className="flex flex-col items-center gap-6 text-center">
          <Link
            to="/"
            className="rounded-xs outline-focus-ring focus-visible:outline-2 focus-visible:outline-offset-2"
          >
            <img src="/tropicast-logo.svg" alt="Tropicast" className="h-12 w-auto" />
          </Link>
          <div className="flex flex-col gap-2 sm:gap-3">
            <h1 className="text-display-xs font-semibold text-primary sm:text-display-sm">
              {title}
            </h1>
            {description && <p className="text-md text-tertiary">{description}</p>}
          </div>
        </div>
        <div className="flex flex-col gap-6">{children}</div>
        {footer && (
          <div className="flex flex-wrap justify-center gap-x-3 gap-y-1 text-sm text-tertiary">
            {footer}
          </div>
        )}
        <LegalLinks />
      </div>
    </main>
  );
}
