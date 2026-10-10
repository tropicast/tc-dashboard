import { defineConfig, devices } from '@playwright/test';

const realE2E = process.env.REAL_E2E === '1';

export default defineConfig({
  testDir: './e2e',
  retries: 1,
  use: {
    baseURL: realE2E
      ? (process.env.REAL_E2E_BASE_URL ?? 'http://localhost:8080')
      : 'http://localhost:5173',
    headless: true,
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: realE2E
    ? undefined
    : {
        command: 'npm run dev -- --host localhost',
        url: 'http://localhost:5173',
        reuseExistingServer: !process.env.CI,
      },
});
