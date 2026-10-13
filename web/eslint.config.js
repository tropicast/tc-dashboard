import js from '@eslint/js';
import prettier from 'eslint-config-prettier';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import globals from 'globals';
import tseslint from 'typescript-eslint';

export default tseslint.config(
    { ignores: ['dist', 'playwright-report', 'src/api/schema.d.ts', 'test-results'] },
    {
        files: ['**/*.{ts,tsx}'],
        extends: [js.configs.recommended, ...tseslint.configs.recommended],
        languageOptions: { ecmaVersion: 2023, globals: globals.browser },
        plugins: { 'react-hooks': reactHooks, 'react-refresh': reactRefresh },
        rules: {
            ...reactHooks.configs.recommended.rules,
            'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
        },
    },
    {
        // Untitled UI components, copied from the library (npx untitledui add): keep them close to upstream.
        files: [
            'src/components/{base,application,foundations}/**',
            'src/utils/**',
            'src/hooks/use-clipboard.ts',
        ],
        rules: {
            'react-refresh/only-export-components': 'off',
            '@typescript-eslint/no-empty-object-type': 'off',
        },
    },
    prettier,
);
