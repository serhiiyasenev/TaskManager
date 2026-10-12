import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { expect, test } from '@playwright/test';
import type { Page, TestInfo } from '@playwright/test';
import { syntheticApi, longTaskName } from './syntheticApi';
import { inboxFixtureItem, syntheticInboxApi } from './syntheticInboxApi';

const evidenceDirectory = resolve(process.cwd(), '../docs/modernization/ui-evidence/inbox-screenshots');
async function capture(page: Page, info: TestInfo, state: string) {
  await mkdir(evidenceDirectory, { recursive: true }); await page.evaluate(() => window.scrollTo(0, 0));
  const path = resolve(evidenceDirectory, `${info.project.name}-inbox-${state}.png`);
  await page.screenshot({ path, fullPage: true }); await info.attach(`synthetic inbox UI ${state}`, { path, contentType: 'image/png' });
}
async function layout(page: Page) {
  const result = await page.evaluate(() => ({ viewport: document.documentElement.clientWidth, document: document.documentElement.scrollWidth,
    missingLabels: [...document.querySelectorAll<HTMLInputElement>('input:not([type=hidden]),select,textarea')].filter(field => !field.labels?.length && !field.getAttribute('aria-label') && !field.getAttribute('aria-labelledby')).length,
    smallTargets: [...document.querySelectorAll('button,input,select,textarea')].filter(element => {
      const rect = element.getBoundingClientRect(); if (!rect.width || !rect.height) return false;
      const target = element instanceof HTMLInputElement && element.type === 'checkbox' ? element.labels?.[0]?.getBoundingClientRect() ?? rect : rect;
      return target.width < 24 || target.height < 24;
    }).length,
    reducedMotion: matchMedia('(prefers-reduced-motion: reduce)').matches }));
  expect(result.document).toBeLessThanOrEqual(result.viewport + 1); expect(result.missingLabels).toBe(0); expect(result.smallTargets).toBe(0); expect(result.reducedMotion).toBe(true);
  const contrast = await page.locator('.badge,.notification-connection,.alert').evaluateAll(elements => {
    const luminance = (color: string) => (color.match(/[\d.]+/g) ?? []).slice(0, 3).map(Number).map(value => {
      const scaled = value / 255; return scaled <= .04045 ? scaled / 12.92 : ((scaled + .055) / 1.055) ** 2.4;
    }).reduce((sum, value, index) => sum + value * [.2126, .7152, .0722][index], 0);
    return elements.map(element => {
      let ancestor: Element | null = element; let background = 'rgb(255,255,255)';
      while (ancestor) { const color = getComputedStyle(ancestor).backgroundColor; if (color !== 'rgba(0, 0, 0, 0)' && color !== 'transparent') { background = color; break; } ancestor = ancestor.parentElement; }
      const foreground = luminance(getComputedStyle(element).color); const behind = luminance(background);
      return { text: element.textContent?.trim(), ratio: (Math.max(foreground, behind) + .05) / (Math.min(foreground, behind) + .05) };
    });
  });
  expect(contrast.filter(value => value.ratio < 4.5)).toEqual([]);
}
async function connected(page: Page) {
  await expect(page.getByText('Зв’язок наживо встановлено.', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Оновити сповіщення' })).toBeEnabled();
}

test('inbox renders canonical kinds, long content, server counts and an idempotent read action', async ({ page }, info) => {
  await syntheticApi(page, { longNames: true }); const api = await syntheticInboxApi(page, { count: 25, longNames: true, holdRead: true });
  await page.goto('/notifications'); await connected(page);
  await expect(page.getByRole('link', { name: longTaskName, exact: true })).toBeVisible();
  await expect(page.getByText('Усього доступних: 25 · Непрочитаних: 25', { exact: true })).toBeVisible();
  for (const text of ['Завдання виконано', 'Нагадування про строк', 'Завдання прострочено', 'Нове завдання']) await expect(page.getByText(text, { exact: true }).first()).toBeVisible();
  await layout(page); await capture(page, info, 'long-list');
  await page.getByLabel('Лише непрочитані').focus(); await page.keyboard.press('Shift+Tab');
  const refresh = page.getByRole('button', { name: 'Оновити сповіщення' }); await expect(refresh).toBeFocused();
  const outline = await refresh.evaluate(element => ({ width: getComputedStyle(element).outlineWidth, style: getComputedStyle(element).outlineStyle }));
  expect(outline.style).toBe('solid'); expect(Number.parseFloat(outline.width)).toBeGreaterThanOrEqual(2);
  await capture(page, info, 'keyboard-focus'); await page.keyboard.press('Enter'); await connected(page);
  await page.getByRole('button', { name: `Позначити прочитаним: ${longTaskName}`, exact: true }).dblclick();
  await expect.poll(() => api.captured.filter(call => call.method === 'PUT').length).toBe(1);
  const request = api.captured.find(call => call.method === 'PUT')!; expect(request.body).toBeUndefined(); expect(request.csrf).toBeTruthy();
  api.releaseRead(); await expect(page.getByText('Прочитано', { exact: true })).toBeVisible();
  await expect(page.getByText('Усього доступних: 25 · Непрочитаних: 24', { exact: true })).toBeVisible();
  await page.getByLabel('Лише непрочитані').check();
  await expect(page.getByRole('link', { name: longTaskName, exact: true })).toHaveCount(0);
  await expect(page.getByText(/24 результатів · Сторінка 1 з 2/)).toBeVisible();
  await page.getByRole('button', { name: 'Далі', exact: true }).click();
  await expect(page.getByText(/Сторінка 2 з 2/)).toBeVisible();
  await capture(page, info, 'read-filtered'); await layout(page);
});

test('real client SDK handles typed hints, ignores duplicates and restores missed notifications on reconnect', async ({ page }, info) => {
  await syntheticApi(page); const api = await syntheticInboxApi(page, { count: 1 }); await page.goto('/notifications'); await connected(page);
  const before = api.captured.filter(call => call.method === 'GET').length;
  api.invalidHint(); await expect(page.getByText('Untrusted', { exact: true })).toHaveCount(0);
  const extra = { ...inboxFixtureItem(2), taskName: 'Нове доступне нагадування' }; api.add(extra); api.hint(extra.id); api.hint(extra.id);
  await expect(page.getByRole('link', { name: extra.taskName, exact: true })).toBeVisible();
  await expect.poll(() => api.captured.filter(call => call.method === 'GET').length).toBe(before + 1);
  await connected(page); const count = api.captured.filter(call => call.method === 'GET').length;
  const missed = { ...inboxFixtureItem(3), taskName: 'Відновлене після перерви завдання' }; api.add(missed);
  await api.disconnect(); await expect.poll(() => api.stats().handshakes).toBeGreaterThan(1); await connected(page);
  await expect(page.getByRole('link', { name: missed.taskName, exact: true })).toBeVisible();
  await expect.poll(() => api.captured.filter(call => call.method === 'GET').length).toBeGreaterThan(count);
  await capture(page, info, 'reconnected'); expect(await page.evaluate(() => ({ local: localStorage.length, session: sessionStorage.length }))).toEqual({ local: 0, session: 0 });
});

test('loading resolves to honest empty content without conflating it with connection loss', async ({ page }, info) => {
  await syntheticApi(page); const api = await syntheticInboxApi(page, { count: 0, holdLoad: true }); await page.goto('/notifications');
  await expect(page.getByText('Завантаження…', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Сповіщень поки немає' })).toHaveCount(0); await capture(page, info, 'loading');
  api.releaseLoad(); await connected(page); await expect(page.getByRole('heading', { name: 'Сповіщень поки немає' })).toBeVisible();
  await capture(page, info, 'empty'); await layout(page);
});

test('HTTP outage retains successful cached rows with safe error and current counts unavailable', async ({ page }, info) => {
  await syntheticApi(page); const api = await syntheticInboxApi(page, { count: 1 }); await page.goto('/notifications'); await connected(page);
  api.settings.inboxFailure = 503; await page.getByRole('button', { name: 'Оновити сповіщення' }).click();
  await expect(page.getByRole('alert')).toContainText('synthetic-inbox-trace');
  await expect(page.getByRole('link', { name: 'Перевірка сповіщення 1', exact: true })).toBeVisible();
  await expect(page.getByText('Усього доступних: — · Непрочитаних: —', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Сповіщень поки немає' })).toHaveCount(0); await capture(page, info, 'partial-error');
  api.settings.inboxFailure = undefined; await page.getByRole('button', { name: 'Повторити' }).click(); await connected(page);
  await expect(page.getByRole('alert')).toHaveCount(0);
});

test('revoked access clears private rows and counts, stops the socket and refreshes identity', async ({ page }, info) => {
  const base = await syntheticApi(page); const api = await syntheticInboxApi(page, { count: 1 }); await page.goto('/notifications'); await connected(page);
  const identityReads = base.captured.filter(call => call.path === '/api/auth/me').length;
  api.settings.inboxFailure = 403; await page.getByRole('button', { name: 'Оновити сповіщення' }).click();
  await expect(page.getByRole('alert')).toContainText('synthetic-inbox-trace');
  await expect(page.getByRole('link', { name: 'Перевірка сповіщення 1', exact: true })).toHaveCount(0);
  await expect.poll(() => base.captured.filter(call => call.path === '/api/auth/me').length).toBeGreaterThan(identityReads);
  await expect.poll(() => api.stats().open).toBe(0); await capture(page, info, 'permission');
  const deniedReads = api.captured.filter(call => call.method === 'GET').length;
  await page.getByRole('button', { name: 'Відновити з’єднання' }).click();
  await expect.poll(() => api.captured.filter(call => call.method === 'GET').length).toBe(deniedReads + 1);
  await expect(page.getByRole('alert')).toContainText('synthetic-inbox-trace'); expect(api.stats().handshakes).toBe(1);
  api.settings.inboxFailure = undefined; api.settings.holdLoad = true;
  await page.getByRole('button', { name: 'Відновити з’єднання' }).click();
  await expect(page.getByText('Завантаження…', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Відновити з’єднання' })).toBeDisabled();
  expect(api.stats().handshakes).toBe(1); await expect(page.getByRole('link', { name: 'Перевірка сповіщення 1', exact: true })).toHaveCount(0);
  const current = { ...inboxFixtureItem(2), taskName: 'Доступ відновлено після перевірки' }; api.add(current); api.releaseLoad();
  await connected(page); await expect.poll(() => api.stats().handshakes).toBe(2);
  await expect(page.getByRole('link', { name: current.taskName, exact: true })).toBeVisible(); await capture(page, info, 'permission-recovered');
});

test('logout clears the inbox and closes the live connection before showing sign-in', async ({ page }, info) => {
  await syntheticApi(page); const api = await syntheticInboxApi(page, { count: 1 }); await page.goto('/notifications'); await connected(page);
  await page.getByRole('button', { name: 'Вийти', exact: true }).click(); await expect(page).toHaveURL(/\/login\?returnTo=/);
  await expect(page.getByRole('link', { name: 'Перевірка сповіщення 1', exact: true })).toHaveCount(0);
  await expect.poll(() => api.stats().open).toBe(0); await capture(page, info, 'logout');
});

test('bounded initial socket failures leave HTTP inbox usable and allow explicit reconnect', async ({ page }, info) => {
  await page.clock.install(); await syntheticApi(page); const api = await syntheticInboxApi(page, { count: 1, negotiateFailure: 503 });
  await page.goto('/notifications'); await expect(page.getByRole('link', { name: 'Перевірка сповіщення 1', exact: true })).toBeVisible();
  await expect.poll(() => api.stats().negotiations).toBe(1);
  for (const [index, delay] of [2_000, 10_000, 30_000].entries()) {
    await page.clock.fastForward(delay + 100); await expect.poll(() => api.stats().negotiations).toBe(index + 2);
  }
  await expect(page.getByText(/Немає з’єднання наживо/)).toBeVisible(); await capture(page, info, 'offline');
  await page.clock.fastForward(60_000); expect(api.stats().negotiations).toBe(4);
  api.settings.negotiateFailure = undefined; await page.getByRole('button', { name: 'Відновити з’єднання' }).click(); await connected(page);
  await expect.poll(() => api.stats().handshakes).toBe(1); await layout(page);
});
