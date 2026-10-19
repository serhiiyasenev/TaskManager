import { expect, test } from '@playwright/test';
import type { APIRequestContext, APIResponse } from '@playwright/test';
import { z } from 'zod';
import { TaskSchema, TeamSchema } from '../src/lib/contracts';
import type { TaskRecord } from '../src/lib/contracts';
import { actor, createWorkspace, newContext, privateLink, productJson, productWrite, registerConfirmLogin } from './helpers';
import { fullStackState } from './state';

const title = 'Статус завдання після виходу виконавця';
const description = 'Синтетичний опис: статус не повинен змінювати поля завдання.';

function persistedFields(task: TaskRecord) {
  // Deliberately omit the nested performer, which may contain a private email.
  return { id: task.id, projectId: task.projectId, performerId: task.performerId, name: task.name,
    description: task.description, createdAt: task.createdAt, finishedAt: task.finishedAt, dueDate: task.dueDate,
    reminderEnabled: task.reminderEnabled, reminderOffsetMinutes: task.reminderOffsetMinutes,
    escalationEnabled: task.escalationEnabled, escalationDelayMinutes: task.escalationDelayMinutes,
    reminderSentAt: task.reminderSentAt, escalationSentAt: task.escalationSentAt };
}
function unchanged(actual: TaskRecord, expected: TaskRecord, includeStateAndVersion = true) {
  return JSON.stringify(persistedFields(actual)) === JSON.stringify(persistedFields(expected))
    && (!includeStateAndVersion || actual.state === expected.state && actual.version === expected.version);
}
function fullWrite(task: TaskRecord) {
  return { projectId: task.projectId, performerId: task.performerId, name: task.name, description: task.description,
    state: 'InProgress', dueDate: task.dueDate, reminderEnabled: task.reminderEnabled,
    reminderOffsetMinutes: task.reminderOffsetMinutes, escalationEnabled: task.escalationEnabled,
    escalationDelayMinutes: task.escalationDelayMinutes, expectedVersion: task.version };
}
async function patchState(request: APIRequestContext, taskId: number, state: string, expectedVersion: string) {
  // The existing helper supports full writes only. Keep this narrow operation
  // on the actual cookie/CSRF boundary without changing shared helper behavior.
  const csrf = await productJson(request, '/api/auth/csrf', z.object({ requestToken: z.string().min(1) }));
  return request.patch(`/api/tasks/${taskId}/state`, { data: { state, expectedVersion },
    headers: { Origin: fullStackState().baseUrl, 'X-CSRF-TOKEN': csrf.requestToken } });
}
async function expectProblem(response: APIResponse, status: number, code: string) {
  expect(response.status()).toBe(status);
  let body: unknown;
  try { body = await response.json(); }
  catch { throw new Error('The native task failure must return JSON ProblemDetails.'); }
  const parsed = z.object({ status: z.literal(status), code: z.literal(code) }).safeParse(body);
  expect(parsed.success, 'The native task failure must have the expected safe ProblemDetails code.').toBe(true);
}

test('departed assignee Board status uses a narrow versioned write and preserves task data', async ({ page, browser }) => {
  // Two real Identity registrations and owner invitation/removal are included.
  test.setTimeout(90_000);
  await registerConfirmLogin(page, actor());
  const workspace = await createWorkspace(page, 'дошка');
  const memberContext = await newContext(browser, page);
  try {
    const member = await memberContext.newPage();
    const person = actor('Тестовий', 'Учасник');
    const memberUser = await registerConfirmLogin(member, person);
    await page.goto(`/teams/${workspace.teamId}`);
    await page.getByLabel('Email запрошеного учасника', { exact: true }).fill(person.email);
    await page.getByRole('button', { name: 'Створити запрошення', exact: true }).click();
    const invitation = await page.getByLabel('Посилання для запрошеної людини', { exact: true }).inputValue();
    await privateLink(member, invitation, '/invite');
    await member.getByRole('button', { name: 'Приєднатися до команди', exact: true }).click();
    await expect(member.getByRole('status')).toContainText('Ви приєдналися до команди');

    // Create through the owner's actual form with nontrivial unrelated fields.
    await page.goto(`/tasks/new?projectId=${workspace.projectId}`);
    await page.getByLabel('Назва', { exact: true }).fill(title);
    await page.getByLabel('Опис', { exact: true }).fill(description);
    await page.getByRole('combobox', { name: /^Виконавець/ }).selectOption(String(memberUser.id));
    await page.getByLabel('Строк виконання', { exact: true }).fill('2030-11-12T10:30');
    await page.getByLabel('Нагадати до строку', { exact: true }).check();
    await page.getByLabel('За скільки хвилин', { exact: true }).fill('45');
    await page.getByLabel('Нагадати про прострочення', { exact: true }).check();
    await page.getByLabel('Через скільки хвилин', { exact: true }).fill('90');
    await page.getByRole('button', { name: 'Створити завдання', exact: true }).click();
    await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();
    const taskId = Number(new URL(page.url()).pathname.split('/').at(-1));
    expect(Number.isSafeInteger(taskId) && taskId > 0).toBe(true);
    const before = await productJson(page.request, `/api/tasks/${taskId}`, TaskSchema);
    expect(before.performerId).toBe(memberUser.id);
    expect(before.state).toBe('ToDo');
    expect(before.reminderEnabled && before.reminderOffsetMinutes === 45
      && before.escalationEnabled && before.escalationDelayMinutes === 90 && Boolean(before.dueDate)).toBe(true);
    expect((await member.request.get(`/api/tasks/${taskId}`)).status()).toBe(200);

    await page.goto(`/teams/${workspace.teamId}`);
    await page.getByRole('button', { name: 'Вилучити Тестовий Учасник', exact: true }).click();
    await page.getByRole('button', { name: 'Підтвердити вилучення', exact: true }).click();
    await expect(page.getByRole('status')).toContainText('Учасника вилучено');
    const membership = await productJson(page.request, `/api/teams/${workspace.teamId}`, TeamSchema);
    expect(membership.users.some(user => user.id === memberUser.id)).toBe(false);
    const departed = await productJson(page.request, `/api/tasks/${taskId}`, TaskSchema);
    expect(unchanged(departed, before), 'Removing membership must preserve the task and its historical assignment.').toBe(true);

    // Preserve the current version: this is an assignee validation rejection,
    // rather than an unrelated optimistic concurrency failure.
    const fullPut = await productWrite(page.request, `/api/tasks/${taskId}`, 'PUT', fullWrite(departed));
    await expectProblem(fullPut, 400, 'Error.Validation.PerformerId');
    expect(unchanged(await productJson(page.request, `/api/tasks/${taskId}`, TaskSchema), departed),
      'Rejected full writes must leave every persisted task field unchanged.').toBe(true);

    await page.goto(`/tasks?projectId=${workspace.projectId}&view=board`);
    const board = page.getByRole('region', { name: 'Дошка завдань поточної сторінки', exact: true });
    const todo = board.locator('.board-column').filter({ has: page.getByRole('heading', { name: 'До виконання', exact: true }) });
    const progress = board.locator('.board-column').filter({ has: page.getByRole('heading', { name: 'У роботі', exact: true }) });
    await expect(todo.getByRole('link', { name: title, exact: true })).toBeVisible();
    const stateResponse = page.waitForResponse(response => response.request().method() === 'PATCH'
      && new URL(response.url()).pathname === `/api/tasks/${taskId}/state`);
    await board.getByLabel(`Статус: ${title}`, { exact: true }).selectOption('InProgress');
    const changedResponse = await stateResponse;
    expect(changedResponse.status()).toBe(200);
    const posted = z.strictObject({ state: z.literal('InProgress'), expectedVersion: z.string().uuid() })
      .safeParse(changedResponse.request().postDataJSON());
    expect(posted.success && posted.data.expectedVersion === departed.version,
      'The actual board must send only state and the loaded task version.').toBe(true);
    await expect(progress.getByRole('link', { name: title, exact: true })).toBeVisible();
    await expect(todo.getByRole('link', { name: title, exact: true })).toHaveCount(0);
    await expect(board.getByLabel(`Статус: ${title}`, { exact: true })).toHaveValue('InProgress');
    const changed = await productJson(page.request, `/api/tasks/${taskId}`, TaskSchema);
    expect(changed.state).toBe('InProgress');
    expect(changed.version !== departed.version).toBe(true);
    expect(unchanged(changed, departed, false), 'State PATCH must preserve assignment, content, dates and reminder settings.').toBe(true);

    await expectProblem(await patchState(page.request, taskId, 'Done', departed.version), 409, 'Error.Conflict');
    expect(unchanged(await productJson(page.request, `/api/tasks/${taskId}`, TaskSchema), changed),
      'A stale PATCH must leave the successfully moved card unchanged.').toBe(true);
    expect((await member.request.get(`/api/tasks/${taskId}`)).status()).toBe(404);
    expect((await patchState(member.request, taskId, 'Done', changed.version)).status()).toBe(404);
    expect((await productWrite(member.request, `/api/tasks/${taskId}`, 'PUT', fullWrite(changed))).status()).toBe(404);
    expect(unchanged(await productJson(page.request, `/api/tasks/${taskId}`, TaskSchema), changed),
      'A departed member must not mutate the task through either write route.').toBe(true);
    await member.goto(`/tasks/${taskId}`);
    await expect(member.getByRole('alert')).toContainText('Об’єкт недоступний');
    await expect(member.getByRole('heading', { name: title, exact: true })).toHaveCount(0);
  } finally { await memberContext.close(); }
});
