import type { ReactNode } from 'react';
import { RouterProvider } from 'react-aria-components';
import { useHref, useNavigate, type NavigateOptions } from 'react-router-dom';

declare module 'react-aria-components' {
  interface RouterConfig {
    routerOptions: NavigateOptions;
  }
}

/** Lets Untitled UI (React Aria) links and buttons with `href` navigate through React Router. */
export function RouteProvider({ children }: { children: ReactNode }) {
  const navigate = useNavigate();
  return (
    <RouterProvider navigate={navigate} useHref={useHref}>
      {children}
    </RouterProvider>
  );
}
