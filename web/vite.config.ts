/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// The API serves the built SPA from its wwwroot; in development Vite proxies API calls to it.
const apiUrl = process.env.API_URL ?? 'http://localhost:5080';

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: { '/api': apiUrl, '/health': apiUrl, '/openapi': apiUrl },
  },
  build: {
    outDir: '../src/Tropicast.Dashboard.Api/wwwroot',
    emptyOutDir: true,
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test-setup.ts'],
  },
});
