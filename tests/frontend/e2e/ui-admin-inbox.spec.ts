import { expect, test } from '@playwright/test';
import type { Page } from '@playwright/test';
import { NotificationAdminInboxItemSchema, NotificationAdminInboxPageSchema } from '../../../src/frontend/src/lib/generated/notifications';
import { inboxFixtureItem, syntheticInboxApi } from './syntheticInboxApi';
import { syntheticApi, syntheticUser } from './syntheticApi';

// Browser-only invented actors. Real database authorization is tested separately.
async function adminFixture(page: Page) {
  await syntheticApi(page); const personal = await syntheticInboxApi(page, { count: 2 });
  const state = { roles: ['Admin'], failure: 0, total: 25, identityReads: 0 };
  const captured: Array<{ path: string; method: string }> = [];
  await page.route('**/api/auth/me', route => {
    state.identityReads++;
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ ...syntheticUser, roles: state.roles }) });
  });
  const rows = Array.from({ length: 25 }, (_, index) => NotificationAdminInboxItemSchema.parse({
    ...inboxFixtureItem(index + 101), taskName: `Адміністративне нагадування ${index + 1}`,
    recipientId: index === 0 ? 42 : 43, recipientName: index === 0 ? 'System Admin' : 'Катерина Довгопрізвищенко — інша команда',
    readAtUtc: index === 1 ? '2026-10-20T11:00:00Z' : null,
  }));
  await page.route('**/api/notifications/all?**', async route => {
    const request = route.request(); const url = new URL(request.url()); captured.push({ path: url.pathname + url.search, method: request.method() });
    if (state.failure) return route.fulfill({ status: state.failure, contentType: 'application/problem+json',
      body: JSON.stringify({ status: state.failure, title: 'Synthetic admin access outcome', traceId: 'synthetic-admin-inbox' }) });
    const number = Number(url.searchParams.get('page')); const size = Number(url.searchParams.get('pageSize'));
    const all = rows.slice(0, state.total); const unread = all.filter(item => !item.readAtUtc);
    const visible = url.searchParams.get('unreadOnly') === 'true' ? unread : all;
    return route.fulfill({ status: 200, contentType: 'application/json', headers: { 'Cache-Control': 'no-store' },
      body: JSON.stringify(NotificationAdminInboxPageSchema.parse({ items: visible.slice((number - 1) * size, number * size),
        page: number, pageSize: size, totalCount: all.length, unreadCount: unread.length })) });
  });
  return { state, captured, personal };
}
async function openAll(page: Page) {
  await page.goto('/notifications');
  await expect(page.getByText('Зв’язок наживо встановлено.', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Усі сповіщення', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Адміністративне нагадування 1', exact: true })).toBeVisible();
}

test('ordinary users retain their personal inbox without a global mode or admin request', async ({ page }) => {
  await syntheticApi(page); const inbox = await syntheticInboxApi(page, { count: 2 });
  await page.goto('/notifications');
  await expect(page.getByText('Зв’язок наживо встановлено.', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Усі сповіщення' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /Позначити прочитаним:/ })).toHaveCount(2);
  expect(inbox.captured.some(call => call.path.startsWith('/api/notifications/all'))).toBe(false);
});

test('administrator overview preserves personal counts, shows recipients read-only and reflows in both languages', async ({ page }) => {
  const api = await adminFixture(page); await openAll(page);
  await expect(page.getByRole('button', { name: 'Усі сповіщення', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByText('System Admin', { exact: true })).toBeVisible();
  await expect(page.getByText('Катерина Довгопрізвищенко — інша команда', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('Прочитано', { exact: true })).toBeVisible();
  await expect(page.getByText('Усього доступних: 25 · Непрочитаних отримувачами: 24', { exact: true })).toBeVisible();
  await expect(page.getByText(/Лише перегляд\. Статус прочитання належить отримувачу/)).toBeVisible();
  await expect(page.getByRole('button', { name: /Позначити прочитаним/ })).toHaveCount(0);
  await page.getByLabel('Лише непрочитані', { exact: true }).check();
  await expect(page.getByText('Прочитано', { exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Далі', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Адміністративне нагадування 22', exact: true })).toBeVisible();
  api.state.total = 20;
  await page.getByRole('button', { name: 'Оновити сповіщення' }).click();
  await expect(page.getByText(/Сторінка 1 з 1/)).toBeVisible();
  await page.getByLabel('Мова інтерфейсу', { exact: true }).selectOption('en');
  await expect(page.getByRole('heading', { name: 'All notifications', exact: true })).toBeVisible();
  await expect(page.getByText('Read only. Read status belongs to the recipient. Refresh notifications to see new events.', { exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Адміністративне нагадування 1', exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
  await page.getByRole('button', { name: 'My notifications', exact: true }).click();
  await expect(page.getByText('Currently accessible: 2 · Unread: 2', { exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Адміністративне нагадування 1', exact: true })).toHaveCount(0);
  expect(api.captured.every(call => call.method === 'GET')).toBe(true);
  expect(api.personal.captured.filter(call => call.method !== 'GET')).toHaveLength(0);
  expect(api.personal.stats().handshakes).toBe(1);
});

test('administrator outage is explicitly cached, then revoked rights clear global rows and remove the mode', async ({ page }) => {
  const api = await adminFixture(page); await openAll(page);
  api.state.failure = 503;
  await page.getByRole('button', { name: 'Оновити сповіщення' }).click();
  await expect(page.getByRole('alert')).toContainText('synthetic-admin-inbox');
  await expect(page.getByText(/Показано раніше завантажені сповіщення/)).toBeVisible();
  await expect(page.getByRole('link', { name: 'Адміністративне нагадування 1', exact: true })).toBeVisible();
  await expect(page.getByText('Усього доступних: — · Непрочитаних отримувачами: —', { exact: true })).toBeVisible();
  const reads = api.state.identityReads; api.state.roles = []; api.state.failure = 403;
  await page.getByRole('button', { name: 'Повторити' }).click();
  await expect.poll(() => api.state.identityReads).toBeGreaterThan(reads);
  await expect(page.getByRole('button', { name: 'Усі сповіщення' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Адміністративне нагадування 1', exact: true })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Сповіщення', exact: true })).toBeVisible();
  expect(api.personal.captured.filter(call => call.method !== 'GET')).toHaveLength(0);
});
