import type { ReactNode } from 'react';
import { RouterProvider } from 'react-aria-components';
import { useHref, useNavigate, type NavigateOptions } from 'react-router-dom';

declare module 'react-aria-components' {
  interface RouterConfig {
    routerOptions: NavigateOptions;
  }
}

/**
 * App paths go through React Router; absolute URLs (another site, or a server route such as a download) stay as they
 * are: React Router would read "https://…" as a relative path.
 */
function useAppHref(href: string) {
  const routed = useHref(href);
  return /^[a-z][a-z\d+.-]*:/i.test(href) ? href : routed;
}

/** Lets Untitled UI (React Aria) links and buttons with `href` navigate through React Router. */
export function RouteProvider({ children }: { children: ReactNode }) {
  const navigate = useNavigate();
  return (
    <RouterProvider navigate={navigate} useHref={useAppHref}>
      {children}
    </RouterProvider>
  );
}
