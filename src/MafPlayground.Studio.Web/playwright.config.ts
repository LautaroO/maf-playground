import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  timeout: 60000,
  webServer: {
    command: 'npm start',
    url: 'http://127.0.0.1:4200/studio/',
    env: { STUDIO_BACKEND_URL: 'http://127.0.0.1:5053' },
    reuseExistingServer: false,
  },
  use: {
    baseURL: process.env['STUDIO_E2E_URL'] || 'http://127.0.0.1:4200/studio/',
    trace: 'off',
    actionTimeout: 10000,
  },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
});
