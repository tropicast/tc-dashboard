import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';

export type Theme = 'light' | 'dark' | 'system';

/** Same key and values as before the Untitled UI refactor, so a saved choice survives it. */
const storageKey = 'theme';
const darkModeClass = 'dark-mode';

const ThemeContext = createContext<{ theme: Theme; setTheme: (theme: Theme) => void } | undefined>(
  undefined,
);

// eslint-disable-next-line react-refresh/only-export-components
export function useTheme() {
  const context = useContext(ThemeContext);
  if (!context) throw new Error('useTheme must be used within a ThemeProvider');
  return context;
}

function storedTheme(): Theme {
  const saved = localStorage.getItem(storageKey);
  return saved === 'light' || saved === 'dark' ? saved : 'system';
}

function systemDark() {
  return typeof window.matchMedia === 'function'
    ? window.matchMedia('(prefers-color-scheme: dark)')
    : undefined;
}

/**
 * Untitled UI's theme provider, adapted: light, dark or system, with the `dark-mode` class on <html>.
 * index.html applies the stored choice before the first paint.
 */
export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setTheme] = useState<Theme>(storedTheme);

  useEffect(() => {
    const media = systemDark();
    const apply = () => {
      const dark = theme === 'dark' || (theme === 'system' && Boolean(media?.matches));
      document.documentElement.classList.toggle(darkModeClass, dark);
    };
    if (theme === 'system') localStorage.removeItem(storageKey);
    else localStorage.setItem(storageKey, theme);
    apply();
    media?.addEventListener('change', apply);
    return () => media?.removeEventListener('change', apply);
  }, [theme]);

  return <ThemeContext.Provider value={{ theme, setTheme }}>{children}</ThemeContext.Provider>;
}
