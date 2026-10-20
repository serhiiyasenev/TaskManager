import { defineConfig } from '@playwright/test';
import { fileURLToPath } from 'node:url';

const repository = fileURLToPath(new URL('../../', import.meta.url));

// Branded channels use disposable Playwright contexts, never a person's browser
// profile. The channel can be overridden by CI after installing its own browser.
const channel = process.env.TASKMANAGER_UI_BROWSER ?? 'chrome';
export default defineConfig({
  testDir: './e2e', testMatch: 'ui*.spec.ts', timeout: 30_000, expect: { timeout: 8_000 },
  fullyParallel: false, workers: 1, retries: 0, forbidOnly: Boolean(process.env.CI),
  outputDir: './test-results/ui',
  reporter: [['list'], ['json', { outputFile: './test-results/ui-summary.json' }]],
  use: { baseURL: 'http://127.0.0.1:4173', browserName: 'chromium', channel, headless: true,
    locale: 'uk-UA', timezoneId: 'Europe/Kyiv', reducedMotion: 'reduce',
    trace: 'retain-on-failure', screenshot: 'only-on-failure', serviceWorkers: 'block' },
  projects: [
    { name: 'desktop-1440x900', use: { viewport: { width: 1440, height: 900 } } },
    { name: 'mobile-390x844', use: { viewport: { width: 390, height: 844 }, hasTouch: true } },
  ],
  webServer: {
    command: 'npm run preview --workspace taskmanager-web -- --host 127.0.0.1 --port 4173 --strictPort',
    cwd: repository,
    url: 'http://127.0.0.1:4173', timeout: 30_000, reuseExistingServer: false,
  },
});
