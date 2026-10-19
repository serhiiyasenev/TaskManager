import { expect, test } from '@playwright/test';
import type { Page, Response } from '@playwright/test';
import { z } from 'zod';
import { ProjectSchema, TaskSchema } from '../src/lib/contracts';
import type { TaskRecord } from '../src/lib/contracts';
import { parseUtc } from '../src/lib/dates';
import { actor, createManualTask, createWorkspace, productJson, productWrite, registerConfirmLogin } from './helpers';

function taskSnapshot(task: TaskRecord) {
  // Authentication/user-directory fields never enter diagnostics or snapshots.
  return { id: task.id, projectId: task.projectId, performerId: task.performerId, name: task.name,
    description: task.description, state: task.state, version: task.version, dueDate: task.dueDate,
    reminderEnabled: task.reminderEnabled, reminderOffsetMinutes: task.reminderOffsetMinutes,
    escalationEnabled: task.escalationEnabled, escalationDelayMinutes: task.escalationDelayMinutes };
}
function sameTask(a: TaskRecord, b: TaskRecord) { return JSON.stringify(taskSnapshot(a)) === JSON.stringify(taskSnapshot(b)); }
async function problem(response: Response, status: number, code: string) {
  expect(response.status()).toBe(status);
  let value: unknown;
  try { value = await response.json(); } catch { throw new Error('The product failure must return JSON ProblemDetails.'); }
  expect(z.object({ status: z.literal(status), code: z.literal(code) }).safeParse(value).success,
    'Only the expected native status/code contract is accepted.').toBe(true);
}
function mutation(page: Page, method: string, path: string) {
  return page.waitForResponse(response => response.request().method() === method && new URL(response.url()).pathname === path);
}

test('two real tabs preserve bilingual edits and recover version conflicts without lost updates', async ({ page }) => {
  test.setTimeout(90_000);
  await registerConfirmLogin(page, actor());
  const workspace = await createWorkspace(page, 'дві вкладки');
  const originalTitle = 'QA вкладки — початкове завдання';
  const taskId = await createManualTask(page, workspace.projectId, originalTitle);
  const initial = await productJson(page.request, `/api/tasks/${taskId}`, TaskSchema);
  await page.goto(`/tasks/${taskId}/edit`);
  const unsavedTitle = 'Мої українські незбережені правки';
  const unsavedDescription = 'Правки не повинні зникнути через мову чи конфлікт.';
  await page.getByLabel('Назва', { exact: true }).fill(unsavedTitle);
  // Playwright label text includes a wrapped textarea's initial text. Match
  // the explicit label prefix while still selecting the actual labelled field.
  await page.getByLabel(/^Опис/).fill(unsavedDescription);
  await page.getByLabel('Нагадати до строку', { exact: true }).check();
  await page.getByLabel('Мова інтерфейсу', { exact: true }).selectOption('en');
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.getByText('A deadline is required for reminders.', { exact: true })).toBeVisible();
  expect(sameTask(await productJson(page.request, `/api/tasks/${taskId}`, TaskSchema), initial),
    'Client validation must make no persisted task changes.').toBe(true);
  await page.getByLabel('Interface language', { exact: true }).selectOption('uk');
  await expect(page.getByText('Для нагадування потрібен строк виконання.', { exact: true })).toBeVisible();
  await expect(page.getByLabel('Назва', { exact: true })).toHaveValue(unsavedTitle);
  await expect(page.getByLabel(/^Опис/)).toHaveValue(unsavedDescription);
  await page.getByLabel(/^Строк виконання/).fill('2030-10-12T14:30');
  await page.getByLabel('За скільки хвилин', { exact: true }).fill('45');

  const boardPage = await page.context().newPage();
  try {
    await boardPage.goto(`/tasks?projectId=${workspace.projectId}&view=board`);
    const boardWrite = mutation(boardPage, 'PATCH', `/api/tasks/${taskId}/state`);
    await boardPage.getByLabel(`Статус: ${originalTitle}`, { exact: true }).selectOption('InProgress');
    expect((await boardWrite).status()).toBe(200);
    const moved = await productJson(page.request, `/api/tasks/${taskId}`, TaskSchema);
    expect(moved.state).toBe('InProgress'); expect(moved.version !== initial.version).toBe(true);

    const staleEdit = mutation(page, 'PUT', `/api/tasks/${taskId}`);
    await page.getByRole('button', { name: 'Зберегти зміни', exact: true }).click();
    await problem(await staleEdit, 409, 'Error.Conflict');
    await expect(page.getByLabel('Назва', { exact: true })).toHaveValue(unsavedTitle);
    expect(sameTask(await productJson(page.request, `/api/tasks/${taskId}`, TaskSchema), moved),
      'A stale full edit must not overwrite the other tab.').toBe(true);
    await page.getByLabel('Мова інтерфейсу', { exact: true }).selectOption('en');
    await expect(page.getByRole('alert')).toContainText('The data or permissions changed.');
    await expect(page.getByLabel(/^Description/)).toHaveValue(unsavedDescription);
    // The browser storage event must update the other mounted tab's copy too.
    await expect(boardPage.getByLabel(`Status: ${originalTitle}`, { exact: true })).toHaveValue('InProgress');
    await page.getByRole('button', { name: 'Load current version', exact: true }).click();
    const current = page.getByRole('region', { name: 'Current task fields', exact: true });
    await expect(current.getByText('In progress', { exact: true })).toBeVisible();
    await expect(page.getByLabel('Name', { exact: true })).toHaveValue(unsavedTitle);
    await page.getByRole('combobox', { name: /^Status(?!:)/ }).selectOption('InProgress');
    const save = mutation(page, 'PUT', `/api/tasks/${taskId}`);
    await page.getByRole('button', { name: 'Save changes', exact: true }).click();
    const savedResponse = await save;
    expect(savedResponse.status()).toBe(200);
    const sent = z.object({ expectedVersion: z.string().uuid() }).safeParse(savedResponse.request().postDataJSON());
    expect(sent.success && sent.data.expectedVersion === moved.version,
      'An explicit retry must use the version reviewed from the server.').toBe(true);
    await expect(page.getByRole('heading', { name: unsavedTitle, exact: true })).toBeVisible();
    const saved = await productJson(page.request, `/api/tasks/${taskId}`, TaskSchema);
    expect(saved.name === unsavedTitle && saved.description === unsavedDescription && saved.state === 'InProgress'
      && saved.reminderEnabled && saved.reminderOffsetMinutes === 45).toBe(true);
    expect(parseUtc(saved.dueDate!).toISOString()).toBe('2030-10-12T11:30:00.000Z');

    const staleBoard = mutation(boardPage, 'PATCH', `/api/tasks/${taskId}/state`);
    await boardPage.getByLabel(`Status: ${originalTitle}`, { exact: true }).selectOption('Done');
    await problem(await staleBoard, 409, 'Error.Conflict');
    await expect(boardPage.getByRole('alert')).toContainText('The data or permissions changed.');
    expect(sameTask(await productJson(page.request, `/api/tasks/${taskId}`, TaskSchema), saved),
      'The stale board must not undo the reviewed full edit.').toBe(true);
    await boardPage.getByRole('button', { name: 'Retry', exact: true }).click();
    await expect(boardPage.getByLabel(`Status: ${unsavedTitle}`, { exact: true })).toHaveValue('InProgress');
    await expect(boardPage.getByRole('alert')).toHaveCount(0);
  } finally { await boardPage.close(); }
});

test('real task filters paginate CRUD and block nonempty project deletion without data loss', async ({ page }) => {
  test.setTimeout(90_000);
  const owner = await registerConfirmLogin(page, actor());
  const workspace = await createWorkspace(page, 'CRUD обмеження');
  const firstTitle = 'QA 01 — перше завдання'; const secondTitle = 'QA 02 — друге завдання';
  const firstId = await createManualTask(page, workspace.projectId, firstTitle);
  const first = await productJson(page.request, `/api/tasks/${firstId}`, TaskSchema);
  // This is a normal product HTTP write, not SQL seeding or a test-only route.
  const secondResponse = await productWrite(page.request, '/api/tasks', 'POST', { projectId: workspace.projectId,
    performerId: owner.id, name: secondTitle, description: 'Друге реальне завдання для межі сторінки.', state: 'ToDo',
    dueDate: null, reminderEnabled: false, escalationEnabled: false });
  expect(secondResponse.status()).toBe(200);
  const secondId = z.object({ id: z.number().int().positive() }).safeParse(await secondResponse.json());
  if (!secondId.success) throw new Error('The product create response must identify a persisted task.');
  const second = await productJson(page.request, `/api/tasks/${secondId.data.id}`, TaskSchema);
  await page.goto(`/tasks?projectId=${workspace.projectId}&pageSize=1&sort=name&direction=asc`);
  const list = page.getByRole('region', { name: 'Список завдань', exact: true });
  await expect(list.getByRole('link', { name: firstTitle, exact: true })).toBeVisible();
  await expect(list.getByRole('link', { name: secondTitle, exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Далі', exact: true }).click();
  await expect(list.getByRole('link', { name: secondTitle, exact: true })).toBeVisible();
  expect(new URL(page.url()).searchParams.get('page')).toBe('2');
  await page.getByLabel('Пошук', { exact: true }).fill('QA 01');
  await page.getByRole('button', { name: 'Показати', exact: true }).click();
  await expect(list.getByRole('link', { name: firstTitle, exact: true })).toBeVisible();
  expect(new URL(page.url()).searchParams.get('page')).toBe('1');
  expect(new URL(page.url()).searchParams.get('projectId')).toBe(String(workspace.projectId));
  expect(new URL(page.url()).searchParams.get('pageSize')).toBe('1');
  await expect(page.getByRole('button', { name: 'Далі', exact: true })).toBeDisabled();

  await page.goto(`/projects/${workspace.projectId}`);
  await expect(page.getByRole('heading', { name: 'Завдання проєкту', exact: true })).toBeVisible();
  const nonemptyDelete = mutation(page, 'DELETE', `/api/projects/${workspace.projectId}`);
  page.once('dialog', dialog => dialog.accept());
  await page.getByRole('button', { name: 'Видалити', exact: true }).click();
  await problem(await nonemptyDelete, 409, 'Error.Conflict');
  await expect(page.getByRole('alert')).toContainText('Дані або права змінилися.');
  const retained = await productJson(page.request, `/api/projects/${workspace.projectId}`, ProjectSchema);
  expect(retained.tasks?.some(task => task.id === first.id) && retained.tasks?.some(task => task.id === second.id),
    'The project constraint must preserve both persisted tasks.').toBe(true);
  expect(sameTask(await productJson(page.request, `/api/tasks/${first.id}`, TaskSchema), first)
    && sameTask(await productJson(page.request, `/api/tasks/${second.id}`, TaskSchema), second)).toBe(true);

  for (const task of [first, second]) {
    await page.goto(`/tasks/${task.id}`);
    const deleted = mutation(page, 'DELETE', `/api/tasks/${task.id}`);
    page.once('dialog', dialog => dialog.accept());
    await page.getByRole('button', { name: 'Видалити', exact: true }).click();
    const response = await deleted;
    expect(response.status()).toBe(200);
    expect(response.request().headers()['if-match'] === `"${task.version}"`, 'The actual delete UI must send a strong current version.').toBe(true);
    expect((await page.request.get(`/api/tasks/${task.id}`)).status()).toBe(404);
  }
  await page.goto(`/projects/${workspace.projectId}`);
  await expect(page.getByText('Завдань поки немає.', { exact: true })).toBeVisible();
  const emptyDelete = mutation(page, 'DELETE', `/api/projects/${workspace.projectId}`);
  page.once('dialog', dialog => dialog.accept());
  await page.getByRole('button', { name: 'Видалити', exact: true }).click();
  expect((await emptyDelete).status()).toBe(200);
  await expect(page.getByRole('heading', { name: 'Проєкти', exact: true })).toBeVisible();
  expect((await page.request.get(`/api/projects/${workspace.projectId}`)).status()).toBe(404);
});
