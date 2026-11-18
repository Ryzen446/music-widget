import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  use: { baseURL: 'http://127.0.0.1:5189', headless: true, viewport: { width: 420, height: 600 } },
  webServer: {
    command: `"${process.execPath}" node_modules/vite/bin/vite.js --host 127.0.0.1 --port 5189 --strictPort`,
    url: 'http://127.0.0.1:5189', reuseExistingServer: true,
  },
});
