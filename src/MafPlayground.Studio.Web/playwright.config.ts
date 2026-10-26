import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  timeout: 60000,
  use: {
    baseURL: 'http://127.0.0.1:5053/studio/',
    trace: 'off',
    actionTimeout: 10000,
  },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
});
