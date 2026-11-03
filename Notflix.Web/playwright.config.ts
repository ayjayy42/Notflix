import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './e2e', timeout: 60000, expect: { timeout: 10000 }, fullyParallel: false, workers: 1,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: { baseURL: 'http://127.0.0.1:4200', browserName: 'chromium', headless: true,
    screenshot: 'only-on-failure', trace: 'retain-on-failure' },
  webServer: { command: 'npm start -- --port 4200', url: 'http://127.0.0.1:4200', timeout: 120000, reuseExistingServer: false }
});
