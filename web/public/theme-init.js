// Apply the saved theme before the first paint (no flash); ThemeProvider takes over after.
// A same-origin file, not an inline script: the production CSP is `script-src 'self'`.
try {
    const theme = localStorage.getItem('theme');
    const dark =
        theme === 'dark' ||
        (theme !== 'light' && window.matchMedia('(prefers-color-scheme: dark)').matches);
    document.documentElement.classList.toggle('dark-mode', dark);
} catch {
    /* storage blocked: stay light until React mounts */
}
