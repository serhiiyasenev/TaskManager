import { defineConfig } from '@playwright/test';
import { fullStackRunId, fullStackState } from './fullstack/state';

const state = fullStackState();
fullStackRunId();
// Native Playwright 1.64 index.js _takePageSnapshot reads this switch before
// capturing the failure DOM. Error-message attachments are separately purged
// by our reporter because this switch does not suppress buildErrorContext.
process.env.PLAYWRIGHT_NO_COPY_PROMPT = '1';
export default defineConfig({
  testDir: './fullstack', testMatch: ['journeys.spec.ts', 'board.spec.ts', 'thorough.spec.ts'], timeout: 30_000, expect: { timeout: 8_000 },
  fullyParallel: false, workers: 1, retries: 0, forbidOnly: Boolean(process.env.CI),
  outputDir: './test-results/fullstack-private',
  reporter: [['./fullstack/sanitizedReporter.ts']],
  use: {
    baseURL: state.baseUrl, browserName: 'chromium', channel: process.env.TASKMANAGER_UI_BROWSER ?? 'chrome',
    headless: true, ignoreHTTPSErrors: true, locale: 'uk-UA', timezoneId: 'Europe/Kyiv',
    reducedMotion: 'reduce', serviceWorkers: 'block', trace: 'off', video: 'off', screenshot: 'off',
  },
  projects: [
    { name: 'desktop-1440x900', use: { viewport: { width: 1440, height: 900 } } },
    { name: 'mobile-390x844', use: { viewport: { width: 390, height: 844 }, hasTouch: true } },
  ],
  // The separate test-only host owns real SQL and all three Kestrel Programs.
  // An already-running SPA preview or synthetic endpoint is insufficient here.
});
