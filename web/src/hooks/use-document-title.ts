import { useEffect } from 'react';

/**
 * Sets the tab title to the page first, then "Tropicast" (WCAG 2.4.2): "Radio One · Stations · Tropicast".
 * Screen readers announce it, and tabs and history entries can be told apart.
 */
export function useDocumentTitle(...parts: (string | undefined | null | false)[]) {
  const title = [...parts.filter(Boolean), 'Tropicast'].join(' · ');
  useEffect(() => {
    document.title = title;
  }, [title]);
}
