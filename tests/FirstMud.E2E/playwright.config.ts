import { defineConfig, devices } from '@playwright/test';

// Base URL points at the Vite dev container. When running inside the e2e
// docker service on the compose network, the host `client` resolves to the
// container; when running on the host, set BASE_URL=http://localhost:5173.
const baseURL = process.env.BASE_URL ?? 'http://client:5173';

export default defineConfig({
  testDir: './tests',
  timeout: 30_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  retries: 0,
  workers: 1,
  reporter: [['list']],
  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
