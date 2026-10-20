import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { expect, test } from '@playwright/test';
import type { Page, TestInfo } from '@playwright/test';
import { syntheticApi } from './syntheticApi';
import { syntheticAiApi, syntheticAiNotes, syntheticAiQuote, syntheticDraftId, syntheticItemOne, syntheticItemTwo, syntheticReceiptVersion } from './syntheticAiApi';

const evidenceDirectory = resolve(process.cwd(), 'test-results/ui-evidence/ai-screenshots');
async function capture(page: Page, info: TestInfo, state: string) {
  await mkdir(evidenceDirectory, { recursive: true });
  await page.evaluate(() => window.scrollTo(0, 0));
  const path = resolve(evidenceDirectory, `${info.project.name}-ai-${state}.png`);
  await page.screenshot({ path, fullPage: true });
  await info.attach(`synthetic AI UI ${state}`, { path, contentType: 'image/png' });
}
async function layoutAndLabels(page: Page) {
  const measurements = await page.evaluate(() => ({ viewport: document.documentElement.clientWidth, document: document.documentElement.scrollWidth,
    unlabelled: [...document.querySelectorAll<HTMLInputElement>('input:not([type=hidden]),select,textarea')].filter(field => !field.labels?.length && !field.getAttribute('aria-label') && !field.getAttribute('aria-labelledby')).length }));
  expect(measurements.document, 'The AI table may scroll within its named region; the page must fit the viewport.').toBeLessThanOrEqual(measurements.viewport + 1);
  expect(measurements.unlabelled).toBe(0);
}
async function fillNotes(page: Page) {
  await page.getByLabel('Проєкт', { exact: true }).selectOption('71');
  await page.getByLabel('Нотатки', { exact: true }).fill(syntheticAiNotes);
  await page.getByLabel('Часовий пояс', { exact: true }).fill('Europe/Kyiv');
}
async function extract(page: Page) {
  await fillNotes(page); await page.getByRole('button', { name: 'Запропонувати завдання', exact: true }).click();
  await expect(page.getByLabel('Вибрати пропозицію 1')).toBeVisible();
}

test('AI review requires explicit selection, preserves source and supports a later partial batch', async ({ page }, info) => {
  await syntheticApi(page, { longNames: true }); const api = await syntheticAiApi(page);
  await page.goto('/ai-notes'); await expect(page.getByRole('heading', { name: 'Завдання з нотаток' })).toBeVisible();
  await extract(page);
  await expect(page.getByLabel('Вибрати пропозицію 1')).not.toBeChecked(); await expect(page.getByLabel('Вибрати пропозицію 2')).not.toBeChecked();
  await expect(page.getByRole('button', { name: 'Зберегти вибрані завдання' })).toBeDisabled();
  await expect(page.getByText('Дату без часу прочитано як 23:59 у часовому поясі Europe/Kyiv. Перевірте, чи цей строк вам підходить.', { exact: true })).toBeVisible();
  await expect(page.getByText(/date_only_end_of_day|commitment_unresolved/)).toHaveCount(0);
  await layoutAndLabels(page); await capture(page, info, 'review');
  const region = page.getByRole('region', { name: 'Таблиця пропозицій', exact: true });
  await page.keyboard.press('Tab');
  await region.focus(); await expect(region).toBeFocused();
  const outline = await region.evaluate(element => ({ width: getComputedStyle(element).outlineWidth, style: getComputedStyle(element).outlineStyle }));
  expect(outline.style).toBe('solid'); expect(Number.parseFloat(outline.width)).toBeGreaterThanOrEqual(2);
  if (info.project.name.startsWith('mobile')) {
    await region.evaluate(element => { element.scrollLeft = element.scrollWidth; });
    await capture(page, info, 'review-sources');
    await region.evaluate(element => { element.scrollLeft = 0; });
  }
  await page.getByLabel('Вибрати пропозицію 1').check();
  await page.getByLabel('Назва пропозиції 1').fill('Перевірка, підтверджена людиною');
  await page.getByRole('button', { name: 'Зберегти вибрані завдання' }).click();
  // Pending also disables rows. Only a returned task link proves that the
  // materialization receipt has been applied to this batch.
  await expect(page.getByRole('link', { name: 'Відкрити створене завдання' }).first()).toHaveAttribute('href', '/tasks/501');
  await expect.poll(() => api.captured.filter(call => call.path.endsWith('/commit')).length).toBe(1);
  await expect(page.getByLabel('Вибрати пропозицію 1')).toBeDisabled();
  await expect(page.getByLabel('Вибрати пропозицію 2')).toBeEnabled(); await expect(page.getByLabel('Вибрати пропозицію 2')).not.toBeChecked();
  const first = api.captured.find(call => call.path.endsWith('/commit'))!;
  expect(first.body?.items).toEqual([expect.objectContaining({ draftItemId: syntheticItemOne, title: 'Перевірка, підтверджена людиною', reminderEnabled: false })]);
  expect(first.key).toMatch(/^[0-9a-f-]{36}$/i); expect(first.csrf).toBeTruthy();
  await expect(page.getByText(syntheticAiQuote, { exact: true })).toBeVisible();
  await capture(page, info, 'partial-saved'); await layoutAndLabels(page);
  await page.getByLabel('Вибрати пропозицію 2').check(); await page.getByLabel('Виконавець пропозиції 2').selectOption('43');
  await page.getByRole('button', { name: 'Зберегти вибрані завдання' }).click();
  await expect(page.getByRole('link', { name: 'Відкрити створене завдання' }).nth(1)).toHaveAttribute('href', '/tasks/502');
  await expect.poll(() => api.captured.filter(call => call.path.endsWith('/commit')).length).toBe(2);
  await expect(page.getByLabel('Вибрати пропозицію 2')).toBeDisabled();
  const commits = api.captured.filter(call => call.path.endsWith('/commit'));
  expect(commits).toHaveLength(2); expect(commits[1].body?.expectedVersion).toBe(syntheticReceiptVersion); expect(commits[1].key).not.toBe(first.key);
  expect(commits[1].body?.items).toEqual([expect.objectContaining({ draftItemId: syntheticItemTwo, assigneeId: 43 })]);
  expect(new URL(page.url()).searchParams.has('draftId')).toBe(true); expect(page.url()).not.toContain('перевірить');
  expect(await page.evaluate(() => ({ local: localStorage.length, session: sessionStorage.length }))).toEqual({ local: 0, session: 0 });
});

test('AI extraction shows progress before the controlled response arrives', async ({ page }, info) => {
  await syntheticApi(page); const api = await syntheticAiApi(page, { holdExtraction: true }); await page.goto('/ai-notes');
  await fillNotes(page); await page.getByRole('button', { name: 'Запропонувати завдання' }).click();
  await expect(page.getByRole('button', { name: 'Зачекайте…' })).toBeDisabled(); await expect(page.getByLabel('Нотатки', { exact: true })).toBeDisabled();
  await expect(page.getByRole('table')).toHaveCount(0); await capture(page, info, 'loading');
  api.releaseExtraction(); await expect(page.getByLabel('Вибрати пропозицію 1')).toBeVisible();
});

test('AI empty results explain the state and retain manual creation', async ({ page }, info) => {
  await syntheticApi(page); await syntheticAiApi(page, { empty: true }); await page.goto('/ai-notes');
  await fillNotes(page); await page.getByRole('button', { name: 'Запропонувати завдання' }).click();
  await expect(page.getByRole('heading', { name: 'Пропозицій не знайдено' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Зберегти вибрані завдання' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Створити завдання вручну' })).toHaveAttribute('href', '/tasks/new?projectId=71');
  await capture(page, info, 'empty'); await layoutAndLabels(page);
});

test('AI malformed output becomes a safe error without invented review rows', async ({ page }, info) => {
  await syntheticApi(page); await syntheticAiApi(page, { malformedDraft: true }); await page.goto('/ai-notes');
  await fillNotes(page); await page.getByRole('button', { name: 'Запропонувати завдання' }).click();
  await expect(page.getByRole('alert')).toContainText('Сервер повернув неочікувану відповідь');
  await expect(page.getByRole('table')).toHaveCount(0); await expect(page.getByLabel('Нотатки', { exact: true })).toHaveValue(syntheticAiNotes);
  await expect(page.getByRole('button', { name: 'Запропонувати завдання' })).toBeEnabled(); await capture(page, info, 'schema-error');
});

test('AI service failure preserves notes and revoked rights hide current directories while retaining edits', async ({ page }, info) => {
  const baseApi = await syntheticApi(page); const api = await syntheticAiApi(page, { extractionFailure: 503 }); await page.goto('/ai-notes');
  await fillNotes(page); await page.getByRole('button', { name: 'Запропонувати завдання' }).click();
  await expect(page.getByRole('alert')).toContainText('synthetic-ai-trace');
  await expect(page.getByLabel('Нотатки', { exact: true })).toHaveValue(syntheticAiNotes); await expect(page.getByRole('table')).toHaveCount(0);
  expect(api.captured.some(call => call.path.endsWith('/commit'))).toBe(false);
  await expect(page.getByRole('link', { name: 'Створити завдання вручну' })).toBeVisible(); await capture(page, info, 'service-error');
  api.settings.extractionFailure = undefined;
  await page.getByRole('button', { name: 'Запропонувати завдання' }).click();
  await expect(page.getByLabel('Вибрати пропозицію 1')).toBeVisible();
  await page.getByLabel('Вибрати пропозицію 1').check(); await page.getByLabel('Назва пропозиції 1').fill('Мої локальні правки збережено у формі');
  const sessionReads = baseApi.captured.filter(call => call.path === '/api/auth/me').length;
  api.settings.commitFailure = 403;
  await page.getByRole('button', { name: 'Зберегти вибрані завдання' }).click();
  await expect(page.getByText(/Доступ змінився. Вибір учасників приховано/)).toBeVisible();
  await expect(page.getByLabel('Назва пропозиції 1')).toHaveValue('Мої локальні правки збережено у формі');
  await expect(page.getByLabel('Виконавець пропозиції 1').getByRole('option')).toHaveCount(1);
  await expect(page.getByLabel('Проєкт', { exact: true }).getByRole('option')).toHaveCount(1);
  await expect(page.getByRole('button', { name: 'Зберегти вибрані завдання' })).toBeDisabled();
  await expect.poll(() => baseApi.captured.filter(call => call.path === '/api/auth/me').length).toBeGreaterThan(sessionReads);
  await capture(page, info, 'permission'); await layoutAndLabels(page);
});

test('AI unavailable status leaves private notes and manual task creation usable', async ({ page }, info) => {
  await syntheticApi(page); await syntheticAiApi(page, { unavailable: true }); await page.goto('/ai-notes');
  await expect(page.getByText(/AI наразі недоступний/)).toBeVisible(); await expect(page.getByRole('button', { name: 'Запропонувати завдання' })).toBeDisabled();
  await page.getByLabel('Нотатки', { exact: true }).fill(syntheticAiNotes); await expect(page.getByLabel('Нотатки', { exact: true })).toBeEnabled();
  await capture(page, info, 'unavailable'); await layoutAndLabels(page);
});

test('AI unknown save freezes human edits and recovers the identical receipt without duplication', async ({ page }, info) => {
  await syntheticApi(page); const api = await syntheticAiApi(page, { unknownCommitOnce: true }); await page.goto('/ai-notes'); await extract(page);
  await page.getByLabel('Вибрати пропозицію 1').check(); await page.getByLabel('Назва пропозиції 1').fill('Мої правки після перевірки');
  await page.getByRole('button', { name: 'Зберегти вибрані завдання' }).dblclick();
  await expect(page.getByText(/Результат збереження ще не підтверджено/)).toBeVisible();
  await expect(page.getByLabel('Назва пропозиції 1')).toBeDisabled(); await expect(page.getByLabel('Нотатки', { exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Видалити чернетку' })).toBeDisabled();
  const first = api.captured.find(call => call.path === `/api/ai/task-extractions/${syntheticDraftId}/commit`)!;
  expect(api.captured.filter(call => call.path.endsWith('/commit'))).toHaveLength(1); await capture(page, info, 'unknown-save');
  await page.getByRole('button', { name: 'Повторити той самий запит' }).click();
  await expect(page.getByLabel('Вибрати пропозицію 1')).toBeDisabled(); await expect(page.getByText('Вибрані завдання збережено.', { exact: true })).toBeVisible();
  const commits = api.captured.filter(call => call.path.endsWith('/commit')); expect(commits).toHaveLength(2);
  expect(commits[1].body).toEqual(first.body); expect(commits[1].key).toBe(first.key); expect(commits[1].csrf).not.toBe(first.csrf);
  await expect(page.getByRole('link', { name: 'Відкрити створене завдання' })).toHaveAttribute('href', '/tasks/501');
  await capture(page, info, 'recovered-save'); await layoutAndLabels(page);
});
