import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { expect, test } from '@playwright/test';
import type { Page, TestInfo } from '@playwright/test';
import { longTaskName, syntheticApi, versionTwo } from './syntheticApi';

const evidenceDirectory = resolve(process.cwd(), '../docs/modernization/ui-evidence/screenshots');
async function capture(page: Page, info: TestInfo, state: string) {
  await mkdir(evidenceDirectory, { recursive: true });
  // Full-page Chrome captures otherwise stitch an off-viewport fixed skip link
  // into a scrolled document. Normalize the document before evidence capture.
  await page.evaluate(() => window.scrollTo(0, 0));
  const file = `${info.project.name}-${state}.png`;
  await page.screenshot({ path: resolve(evidenceDirectory, file), fullPage: true });
  await info.attach(`synthetic UI ${state}`, { path: resolve(evidenceDirectory, file), contentType: 'image/png' });
}
async function noDocumentOverflow(page: Page) {
  const widths = await page.evaluate(() => ({ viewport: document.documentElement.clientWidth, document: document.documentElement.scrollWidth }));
  expect(widths.document, 'Only the named task table/board may scroll horizontally; the whole document must fit.').toBeLessThanOrEqual(widths.viewport + 1);
}
async function labelledFields(page: Page) {
  const unlabelled = await page.locator('input:not([type=hidden]), select, textarea').evaluateAll(fields => fields.filter(field => {
    const element = field as HTMLInputElement;
    return !element.labels?.length && !element.getAttribute('aria-label') && !element.getAttribute('aria-labelledby');
  }).map(field => field.outerHTML));
  expect(unlabelled, 'Every rendered form control needs an accessible label.').toEqual([]);
}
async function touchTargets(page: Page) {
  const small = await page.locator('button, input, select, textarea').evaluateAll(elements => elements.filter(element => {
    const rect = element.getBoundingClientRect(); if (!rect.width || !rect.height) return false;
    const actual = element instanceof HTMLInputElement && element.type === 'checkbox' ? element.labels?.[0]?.getBoundingClientRect() ?? rect : rect;
    return actual.width < 24 || actual.height < 24;
  }).map(element => ({ text: element.textContent, html: element.outerHTML })));
  expect(small, 'Interactive targets, including clickable checkbox labels, meet the 24px WCAG minimum.').toEqual([]);
}
async function statusContrast(page: Page) {
  const measurements = await page.locator('.badge, .due.overdue, .alert').evaluateAll(elements => {
    function rgb(value: string) { return (value.match(/[\d.]+/g) ?? []).slice(0, 3).map(Number); }
    function luminance(channels: number[]) { return channels.map(value => { const scaled = value / 255; return scaled <= .04045 ? scaled / 12.92 : ((scaled + .055) / 1.055) ** 2.4; }).reduce((sum, value, index) => sum + value * [.2126, .7152, .0722][index], 0); }
    return elements.map(element => {
      const foreground = getComputedStyle(element).color; let ancestor: Element | null = element; let background = 'rgb(255, 255, 255)';
      while (ancestor) { const color = getComputedStyle(ancestor).backgroundColor; if (color !== 'rgba(0, 0, 0, 0)' && color !== 'transparent') { background = color; break; } ancestor = ancestor.parentElement; }
      const a = luminance(rgb(foreground)); const b = luminance(rgb(background));
      return { text: element.textContent?.trim().slice(0, 60), ratio: (Math.max(a, b) + .05) / (Math.min(a, b) + .05) };
    });
  });
  expect(measurements.filter(value => value.ratio < 4.5), 'Measured status/overdue/error text has at least 4.5:1 contrast.').toEqual([]);
}

test('long-content dashboard, visible keyboard focus, responsive layout and reduced motion', async ({ page }, info) => {
  await syntheticApi(page, { longNames: true }); await page.goto('/');
  await expect(page.getByRole('heading', { name: /Вітаємо/ })).toBeVisible();
  await expect(page.getByRole('link', { name: longTaskName })).toBeVisible();
  await capture(page, info, 'dashboard-long');
  await page.keyboard.press('Tab');
  const focused = page.locator(':focus'); await expect(focused).toHaveText('Перейти до вмісту');
  const outline = await focused.evaluate(element => ({ width: getComputedStyle(element).outlineWidth, style: getComputedStyle(element).outlineStyle }));
  expect(outline.style).toBe('solid'); expect(Number.parseFloat(outline.width)).toBeGreaterThanOrEqual(2);
  await capture(page, info, 'keyboard-focus');
  await page.keyboard.press('Enter'); await expect(page.locator('#main-content')).toBeFocused();
  expect(await page.evaluate(() => matchMedia('(prefers-reduced-motion: reduce)').matches)).toBe(true);
  const motion = await page.evaluate(() => {
    const seconds = (value: string) => value.split(',').map(item => Number.parseFloat(item) / (item.trim().endsWith('ms') ? 1000 : 1));
    const durations = Array.from(document.querySelectorAll('*')).flatMap(element => {
      const style = getComputedStyle(element); return [...seconds(style.animationDuration), ...seconds(style.transitionDuration)];
    });
    return { maximumSeconds: Math.max(...durations), running: document.getAnimations().filter(animation => animation.playState === 'running').length };
  });
  expect(motion.maximumSeconds).toBeLessThanOrEqual(.01); expect(motion.running).toBe(0);
  await noDocumentOverflow(page); await statusContrast(page);
});

test('task list and board preserve filters and submit a current-version status change', async ({ page }, info) => {
  const api = await syntheticApi(page, { longNames: true }); await page.goto('/tasks');
  await expect(page.getByRole('region', { name: 'Список завдань' })).toBeVisible();
  await labelledFields(page); await capture(page, info, 'tasks-list-long'); await noDocumentOverflow(page);
  await page.getByRole('button', { name: 'Дошка', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Дошка', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('heading', { name: 'До виконання', exact: true })).toBeVisible();
  await page.getByLabel(`Статус: ${longTaskName}`).selectOption('InProgress');
  await expect.poll(() => api.captured.some(call => call.method === 'PUT' && call.path === '/api/tasks/101' && call.body?.state === 'InProgress' && typeof call.body?.expectedVersion === 'string' && !!call.csrf)).toBe(true);
  await page.getByLabel('Пошук', { exact: true }).fill('Перевірити запрошення'); await page.getByRole('button', { name: 'Показати', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Перевірити запрошення учасників' })).toBeVisible();
  await expect(page.getByRole('link', { name: longTaskName })).toHaveCount(0);
  await capture(page, info, 'tasks-board-filtered'); await noDocumentOverflow(page); await statusContrast(page);
});

test('real rendered task form creates a task and converts local deadline to UTC', async ({ page }, info) => {
  const api = await syntheticApi(page); await page.goto('/tasks/new');
  await expect(page.getByRole('heading', { name: 'Нове завдання' })).toBeVisible(); await labelledFields(page);
  await page.getByLabel('Назва', { exact: true }).fill('Синтетичне створення через браузер');
  await page.getByLabel('Опис', { exact: true }).fill('Це fixture, а не production API E2E.');
  await page.getByLabel('Строк виконання', { exact: true }).fill('2030-10-12T14:30');
  await page.getByLabel('Нагадати до строку').check();
  await capture(page, info, 'task-form'); await touchTargets(page); await noDocumentOverflow(page);
  await page.getByRole('button', { name: 'Створити завдання', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Синтетичне створення через браузер' })).toBeVisible();
  const create = api.captured.find(call => call.path === '/api/tasks' && call.method === 'POST');
  expect(create?.body?.dueDate).toBe('2030-10-12T11:30:00.000Z'); expect(create?.csrf).toBeTruthy();
  await capture(page, info, 'task-create-success');
});

test('stale edit preserves unsaved fields and requires current-record review before explicit retry', async ({ page }, info) => {
  const api = await syntheticApi(page, { conflictOnce: true }); await page.goto('/tasks/101/edit');
  await expect(page.getByRole('heading', { name: 'Редагувати завдання' })).toBeVisible();
  await page.getByLabel('Назва', { exact: true }).fill('Мій незбережений текст');
  await page.getByRole('button', { name: 'Зберегти зміни', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('synthetic-conflict-trace');
  await expect(page.getByLabel('Назва', { exact: true })).toHaveValue('Мій незбережений текст');
  await capture(page, info, 'conflict');
  await page.getByRole('button', { name: 'Завантажити поточну версію' }).click();
  await expect(page.getByText('Редагування колеги', { exact: true })).toBeVisible();
  await expect(page.getByLabel('Назва', { exact: true })).toHaveValue('Мій незбережений текст');
  await page.getByRole('button', { name: 'Зберегти зміни', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Мій незбережений текст' })).toBeVisible();
  expect(api.captured.filter(call => call.method === 'PUT').at(-1)?.body?.expectedVersion).toBe(versionTwo);
});

test('loading has visible progress and resolves to data', async ({ page }, info) => {
  const api = await syntheticApi(page, { holdTasks: true }); await page.goto('/tasks');
  await expect(page.getByRole('status')).toContainText('Завантаження'); await capture(page, info, 'loading');
  api.releaseTasks(); await expect(page.getByRole('region', { name: 'Список завдань' })).toBeVisible();
});

test('empty task results explain the state and offer filter recovery', async ({ page }, info) => {
  await syntheticApi(page, { emptyTasks: true }); await page.goto('/tasks?search=відсутнє');
  await expect(page.getByRole('heading', { name: 'Завдань не знайдено' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Очистити фільтри' })).toBeVisible();
  await capture(page, info, 'empty'); await noDocumentOverflow(page);
  await page.getByRole('button', { name: 'Очистити фільтри' }).click(); await expect(page.getByLabel('Пошук', { exact: true })).toHaveValue('');
});

test('API error shows a safe trace and can retry without invented task rows', async ({ page }, info) => {
  const api = await syntheticApi(page, { taskFailure: 503 }); await page.goto('/tasks');
  await expect(page.getByRole('alert')).toContainText('synthetic-task-trace');
  await expect(page.getByRole('region', { name: 'Список завдань' })).toHaveCount(0);
  await capture(page, info, 'error'); await statusContrast(page);
  api.settings.taskFailure = undefined; await page.getByRole('button', { name: 'Повторити' }).click();
  await expect(page.getByRole('region', { name: 'Список завдань' })).toBeVisible();
});

test('partial dashboard retains successful tasks while project section fails and retries', async ({ page }, info) => {
  const api = await syntheticApi(page, { projectFailure: 503 }); await page.goto('/');
  await expect(page.getByRole('link', { name: 'Підготувати перевірку доступу' })).toBeVisible();
  await expect(page.getByRole('alert')).toContainText('synthetic-project-trace'); await capture(page, info, 'partial');
  api.settings.projectFailure = undefined; await page.getByRole('button', { name: 'Повторити' }).click();
  await expect(page.getByRole('link', { name: 'Оновлення TaskManager' })).toBeVisible();
});

test('forbidden owner mutation hides cached controls and invitation directory', async ({ page }, info) => {
  await syntheticApi(page, { invitationForbidden: true }); await page.goto('/teams/17');
  await page.getByLabel('Email запрошеного учасника').fill('new@example.test');
  await page.getByRole('button', { name: 'Створити запрошення', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('synthetic-permission-trace');
  await expect(page.getByRole('button', { name: 'Створити запрошення', exact: true })).toHaveCount(0);
  await expect(page.getByText('new@example.test', { exact: true })).toHaveCount(0); await capture(page, info, 'permission');
});

test('expired business session returns to labelled sign-in with an explanation', async ({ page }, info) => {
  const api = await syntheticApi(page); await page.goto('/'); await expect(page.getByRole('heading', { name: /Вітаємо/ })).toBeVisible();
  api.settings.taskFailure = 401; await page.getByRole('link', { name: 'Завдання', exact: true }).click();
  await expect(page).toHaveURL(/\/login\?returnTo=.*expired=1/);
  await expect(page.getByText('Сесію завершено. Увійдіть знову.', { exact: true })).toBeVisible();
  await labelledFields(page); await capture(page, info, 'session-expired');
});

test('labelled keyboard login uses the actual transport and returns to the work surface', async ({ page }, info) => {
  const api = await syntheticApi(page, { anonymous: true }); await page.goto('/login');
  await labelledFields(page); await page.keyboard.press('Tab'); await expect(page.getByLabel('Email', { exact: true })).toBeFocused();
  await page.keyboard.type('olena@example.test'); await page.keyboard.press('Tab'); await expect(page.getByLabel('Пароль', { exact: true })).toBeFocused();
  await page.keyboard.type('SyntheticPassword123!'); await page.keyboard.press('Tab'); await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { name: /Вітаємо/ })).toBeVisible();
  expect(api.captured.some(call => call.path === '/api/auth/login' && !!call.csrf)).toBe(true);
  expect(await page.evaluate(() => ({ local: localStorage.length, session: sessionStorage.length }))).toEqual({ local: 0, session: 0 });
  await capture(page, info, 'login-success');
});

test('registration password mismatch is associated with the labelled repeat field', async ({ page }, info) => {
  const api = await syntheticApi(page, { anonymous: true }); await page.goto('/register');
  await page.getByLabel('Ім’я користувача').fill('olena'); await page.getByLabel('Ім’я', { exact: true }).fill('Олена');
  await page.getByLabel('Прізвище').fill('Коваль'); await page.getByLabel('Email', { exact: true }).fill('olena@example.test');
  await page.getByLabel('Пароль', { exact: true }).fill('SyntheticPassword123!'); await page.getByLabel('Повторіть пароль').fill('OtherPassword123!');
  await page.getByRole('button', { name: 'Зареєструватися', exact: true }).click();
  await expect(page.getByLabel('Повторіть пароль')).toHaveAttribute('aria-invalid', 'true');
  await expect(page.getByLabel('Повторіть пароль')).toHaveAccessibleDescription('Паролі не збігаються.');
  expect(api.captured.some(call => call.path === '/api/auth/register')).toBe(false);
  await capture(page, info, 'registration-validation'); await noDocumentOverflow(page); await touchTargets(page);
});
