import { expect, test } from '@playwright/test';
import { z } from 'zod';
import { NotificationInboxPageSchema } from '../src/lib/generated/notifications';
import { actor, advanceClock, aiMode, createManualTask, createWorkspace, dispatch,
  extract, fillNotes, layoutAndCapture, liveHints, login, mail, newContext, noteDate, notes,
  privateLink, productJson, productWrite, registerConfirmLogin, summary } from './helpers';

const firstTitle = 'Підготувати міграцію — підтверджено людиною';
const secondTitle = 'Перевірити API — підтверджено людиною';
const TaskSchema = z.object({ id: z.number().int().positive(), name: z.string(), projectId: z.number().int().positive(),
  performerId: z.number().int().positive(), version: z.string().uuid(), dueDate: z.string().nullable().optional(), state: z.string() });

test.beforeEach(async () => { await aiMode('normal'); });
test.afterEach(async () => { await aiMode('normal'); });

test('real source review, two partial commits, private live inbox and durable relogin', async ({ page, browser }, info) => {
  // This journey includes two real Identity registrations, two SQL commits,
  // private WebSockets and relogin. Its flow limit is independent of the 30s default.
  test.setTimeout(90_000);
  const owner = actor(); const ownerHints = liveHints(page);
  const ownerUser = await registerConfirmLogin(page, owner);
  const owned = await createWorkspace(page, 'джерела');
  const foreignContext = await newContext(browser, page);
  try {
    const foreign = await foreignContext.newPage(); const foreignHints = liveHints(foreign);
    await registerConfirmLogin(foreign, actor('Сторонній', 'Користувач'));
    await createWorkspace(foreign, 'інша команда');
    await foreign.goto('/notifications');
    await expect(foreign.getByText('Зв’язок наживо встановлено.', { exact: true })).toBeVisible();
    await expect(foreign.getByRole('heading', { name: 'Сповіщень поки немає', exact: true })).toBeVisible();

    const before = await summary(); expect(before.migrationCount).toBe(8);
    const draft = await extract(page, owned.projectId);
    expect(draft.referenceDate).toBe(noteDate); expect(draft.items).toHaveLength(2);
    for (const item of draft.items) {
      expect(item.sourceQuote.length).toBeGreaterThan(0);
      expect(notes.slice(item.sourceStart, item.sourceStart + item.sourceLength)).toBe(item.sourceQuote);
      expect(item.proposedAssigneeId).toBe(ownerUser.id); expect(item.materializedTaskId ?? null).toBeNull();
    }
    expect(draft.items[1].sourceStart).toBeGreaterThan(draft.items[0].sourceStart);
    expect(new Date(draft.items[0].proposedDueAtUtc!).toISOString()).toBe('2026-10-13T20:59:00.000Z');
    expect(new Date(draft.items[1].proposedDueAtUtc!).toISOString()).toBe('2026-10-16T20:59:00.000Z');
    await expect(page.getByLabel('Вибрати пропозицію 1')).not.toBeChecked();
    await expect(page.getByLabel('Вибрати пропозицію 2')).not.toBeChecked();
    await expect(page.getByRole('button', { name: 'Зберегти вибрані завдання', exact: true })).toBeDisabled();
    await expect(page.locator('blockquote.ai-source')).toHaveCount(2);
    await expect(page.getByLabel('Строк пропозиції 1')).toHaveValue('2026-10-13T23:59');
    expect((await summary()).tasks).toBe(before.tasks);
    await layoutAndCapture(page, info, 'ai-review');

    await page.getByLabel('Вибрати пропозицію 1').check();
    await page.getByLabel('Назва пропозиції 1').fill(firstTitle);
    await page.getByRole('button', { name: 'Зберегти вибрані завдання', exact: true }).click();
    const created = page.getByRole('link', { name: 'Відкрити створене завдання', exact: true });
    await expect(created).toHaveCount(1);
    const firstPath = await created.first().getAttribute('href');
    if (!firstPath || !/^\/tasks\/\d+$/.test(firstPath)) throw new Error('The first real commit did not return a task link.');
    const firstTask = await productJson(page.request, `/api${firstPath}`, TaskSchema);
    expect(firstTask.name).toBe(firstTitle); expect(firstTask.performerId).toBe(ownerUser.id);
    await expect(page.getByLabel('Вибрати пропозицію 1')).toBeDisabled();
    await expect(page.getByLabel('Вибрати пропозицію 2')).toBeEnabled();
    await expect(page.getByLabel('Вибрати пропозицію 2')).not.toBeChecked();
    const partial = await summary();
    expect(partial.tasks).toBe(before.tasks + 1); expect(partial.materializations).toBe(before.materializations + 1);
    expect(partial.commitReceipts).toBe(before.commitReceipts + 1);

    // Keep the inbox mounted while the second page performs the next commit.
    // The new row must arrive from the real private hint, without a refresh click.
    const review = await page.context().newPage();
    try {
      await review.goto(`/ai-notes?draftId=${draft.id}`);
      await expect(review.getByLabel('Вибрати пропозицію 1')).toBeDisabled();
      await page.goto('/notifications');
      await expect(page.getByText('Зв’язок наживо встановлено.', { exact: true })).toBeVisible();
      await dispatch();
      await expect(page.getByRole('list', { name: 'Сповіщення', exact: true }).getByRole('link', { name: firstTitle, exact: true })).toBeVisible();
      const hintsBeforeSecond = ownerHints();
      await review.getByLabel('Вибрати пропозицію 2').check();
      await review.getByLabel('Назва пропозиції 2').fill(secondTitle);
      await review.getByRole('button', { name: 'Зберегти вибрані завдання', exact: true }).click();
      await expect(review.getByRole('link', { name: 'Відкрити створене завдання', exact: true })).toHaveCount(2);
      await dispatch();
      await expect.poll(ownerHints, { message: 'A real private InboxChanged WebSocket hint is required.' }).toBeGreaterThan(hintsBeforeSecond);
      await expect(page.getByRole('list', { name: 'Сповіщення', exact: true }).getByRole('link', { name: secondTitle, exact: true })).toBeVisible();
    } finally { await review.close(); }
    const all = await summary();
    expect(all.tasks).toBe(before.tasks + 2); expect(all.materializations).toBe(before.materializations + 2);
    expect(all.commitReceipts).toBe(before.commitReceipts + 2);
    await expect.poll(async () => (await summary()).processingMarkers).toBe(before.processingMarkers + 2);
    await expect.poll(async () => (await summary()).acknowledged).toBeGreaterThanOrEqual(before.acknowledged + 2);
    expect(foreignHints()).toBe(0);
    await expect(foreign.getByRole('heading', { name: 'Сповіщень поки немає', exact: true })).toBeVisible();

    const inbox = await productJson(page.request, '/api/notifications?page=1&pageSize=20&unreadOnly=false', NotificationInboxPageSchema);
    expect(inbox.totalCount).toBe(2); expect(inbox.items.map(item => item.taskName).sort()).toEqual([firstTitle, secondTitle].sort());
    for (const path of [`/api/tasks/${firstTask.id}`, `/api/projects/${owned.projectId}`, `/api/ai/task-extractions/${draft.id}`])
      expect((await foreign.request.get(path)).status()).toBe(404);
    expect((await productWrite(foreign.request, `/api/notifications/${inbox.items[0].id}/read`, 'PUT')).status()).toBe(404);
    await foreign.goto(`/tasks/${firstTask.id}`);
    await expect(foreign.getByRole('alert')).toContainText('Об’єкт недоступний');
    await expect(foreign.getByText(firstTitle, { exact: true })).toHaveCount(0);

    await page.getByRole('button', { name: `Позначити прочитаним: ${firstTitle}`, exact: true }).click();
    await expect(page.getByRole('button', { name: `Позначити прочитаним: ${firstTitle}`, exact: true })).toHaveCount(0);
    await page.reload();
    await expect(page.getByRole('list', { name: 'Сповіщення', exact: true }).getByRole('link', { name: firstTitle, exact: true })).toBeVisible();
    await layoutAndCapture(page, info, 'durable-inbox');
    await page.getByRole('button', { name: 'Вийти', exact: true }).click();
    await expect(page).toHaveURL(/\/login/);
    await login(page, owner);
    await page.goto('/notifications');
    await expect(page.getByRole('button', { name: `Позначити прочитаним: ${firstTitle}`, exact: true })).toHaveCount(0);
    const restored = await productJson(page.request, '/api/notifications?page=1&pageSize=20&unreadOnly=false', NotificationInboxPageSchema);
    expect(restored.totalCount).toBe(2); expect(restored.items.find(item => item.taskId === firstTask.id)?.readAtUtc).toBeTruthy();
    await page.goto(`/ai-notes?draftId=${draft.id}`);
    await expect(page.getByLabel('Вибрати пропозицію 1')).toBeDisabled();
    await expect(page.getByLabel('Вибрати пропозицію 2')).toBeDisabled();
    await page.goto('/tasks');
    await expect(page.getByRole('link', { name: firstTitle, exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: secondTitle, exact: true })).toBeVisible();
    expect(await page.evaluate(() => ({ local: localStorage.length, session: sessionStorage.length }))).toEqual({ local: 0, session: 0 });
    const persisted = await summary(); expect(persisted.tasks).toBe(before.tasks + 2);
    expect(persisted.outboxPending).toBe(0);
  } finally { await foreignContext.close(); }
});

test('owner removal revokes a real member draft and task; test clock expires the real cookie', async ({ page, browser }) => {
  test.setTimeout(90_000);
  const owner = actor(); await registerConfirmLogin(page, owner);
  const owned = await createWorkspace(page, 'відкликання');
  const memberContext = await newContext(browser, page);
  try {
    const member = await memberContext.newPage(); const person = actor('Тестовий', 'Учасник');
    await registerConfirmLogin(member, person);
    await page.goto(`/teams/${owned.teamId}`);
    await page.getByLabel('Email запрошеного учасника', { exact: true }).fill(person.email);
    await page.getByRole('button', { name: 'Створити запрошення', exact: true }).click();
    const link = await page.getByLabel('Посилання для запрошеної людини', { exact: true }).inputValue();
    // The real API omits null timestamps on a fresh invitation. A reload must
    // keep the owner list usable after the one-time private link is hidden.
    await page.reload();
    const invitations = page.getByRole('region', { name: 'Приватні запрошення', exact: true });
    await expect(invitations.getByText('Активне', { exact: true })).toBeVisible();
    await expect(invitations.getByRole('button', { name: `Відкликати запрошення ${person.email}`, exact: true })).toBeVisible();
    await expect(invitations.getByRole('alert')).toHaveCount(0);
    await privateLink(member, link, '/invite');
    await member.getByRole('button', { name: 'Приєднатися до команди', exact: true }).click();
    await expect(member.getByRole('status')).toContainText('Ви приєдналися до команди');
    await page.reload();
    await expect(invitations.getByText('Прийняте', { exact: true })).toBeVisible();
    await expect(invitations.getByRole('button', { name: `Відкликати запрошення ${person.email}`, exact: true })).toHaveCount(0);
    await expect(invitations.getByRole('alert')).toHaveCount(0);
    const taskId = await createManualTask(member, owned.projectId, 'Завдання учасника до вилучення');
    const draft = await extract(member, owned.projectId);
    await member.getByLabel('Вибрати пропозицію 1').check();
    await member.getByLabel('Назва пропозиції 1').fill('Мої незбережені правки');
    const before = await summary();
    await page.reload();
    await page.getByRole('button', { name: 'Вилучити Тестовий Учасник', exact: true }).click();
    await page.getByRole('button', { name: 'Підтвердити вилучення', exact: true }).click();
    await expect(page.getByRole('status')).toContainText('Учасника вилучено');
    await member.getByRole('button', { name: 'Зберегти вибрані завдання', exact: true }).click();
    // A refreshed real /me now has no team. Protected pages must discard the
    // former scope's private source/rows rather than keep a stale review form.
    await expect(member.getByLabel('Назва пропозиції 1')).toHaveCount(0);
    await expect(member.locator('.ai-source')).toHaveCount(0);
    await expect(member.getByRole('button', { name: 'Зберегти вибрані завдання', exact: true })).toHaveCount(0);
    await expect(member.getByRole('alert')).toContainText('Чернетка недоступна');
    expect((await member.request.get(`/api/tasks/${taskId}`)).status()).toBe(404);
    expect((await member.request.get(`/api/projects/${owned.projectId}`)).status()).toBe(404);
    expect((await member.request.get(`/api/ai/task-extractions/${draft.id}`)).status()).toBe(403);
    const after = await summary(); expect(after.tasks).toBe(before.tasks);
    expect(after.materializations).toBe(before.materializations); expect(after.commitReceipts).toBe(before.commitReceipts);
    await member.goto(`/tasks/${taskId}`);
    await expect(member.getByRole('alert')).toContainText('Об’єкт недоступний');
    await expect(member.getByRole('heading', { name: 'Завдання учасника до вилучення', exact: true })).toHaveCount(0);

    // Cookie issuance and expiry both use the same host TimeProvider. A normal
    // business navigation must receive the real 401 and clear the browser session.
    await advanceClock(61);
    await page.getByRole('link', { name: 'Завдання', exact: true }).first().click();
    await expect(page).toHaveURL(/\/login\?returnTo=.*expired=1/);
    await expect(page.getByText('Сесію завершено. Увійдіть знову.', { exact: true })).toBeVisible();
    expect((await page.request.get('/api/tasks/page?page=1&pageSize=20')).status()).toBe(401);
    await login(page, owner);
    await page.goto(`/tasks/${taskId}`);
    await expect(page.getByRole('heading', { name: 'Завдання учасника до вилучення', exact: true })).toBeVisible();
  } finally { await memberContext.close(); }
});

test('AI unavailable and invalid output create no tasks; manual work and real password recovery remain usable', async ({ page, browser }, info) => {
  test.setTimeout(90_000);
  const owner = actor(); await registerConfirmLogin(page, owner);
  const owned = await createWorkspace(page, 'відновлення');
  const before = await summary();
  await aiMode('unavailable');
  await fillNotes(page, owned.projectId);
  await expect(page.getByText(/AI наразі недоступний/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Запропонувати завдання', exact: true })).toBeDisabled();
  await expect(page.getByLabel('Нотатки', { exact: true })).toHaveValue(notes);
  await expect(page.getByLabel('Нотатки', { exact: true })).toBeEnabled();
  expect((await summary()).tasks).toBe(before.tasks);

  await aiMode('invalid-output');
  await fillNotes(page, owned.projectId);
  const rejectedExtraction = page.waitForResponse(response =>
    response.request().method() === 'POST' && new URL(response.url()).pathname === '/api/ai/task-extractions');
  await page.getByRole('button', { name: 'Запропонувати завдання', exact: true }).click();
  const failureResponse = await rejectedExtraction;
  expect(failureResponse.status()).toBe(503);
  let failureBody: unknown;
  try { failureBody = await failureResponse.json(); }
  catch { throw new Error('The native extraction failure must return JSON ProblemDetails.'); }
  const safeProblem = z.object({ status: z.literal(503), title: z.literal('Error.ServiceUnavailable'),
    code: z.literal('Error.ServiceUnavailable') }).safeParse(failureBody);
  // Assert only the safe contract verdict; never dump a response body or notes.
  expect(safeProblem.success, 'Unsafe AI output must return the native 503 ProblemDetails code.').toBe(true);
  await expect(page.getByRole('alert')).toContainText('Сервіс тимчасово недоступний. Спробуйте пізніше.');
  await expect(page.getByLabel('Нотатки', { exact: true })).toHaveValue(notes);
  await expect(page.getByRole('region', { name: 'Таблиця пропозицій', exact: true })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Створити завдання вручну', exact: true })).toBeVisible();
  const failed = await summary(); expect(failed.tasks).toBe(before.tasks);
  expect(failed.materializations).toBe(before.materializations); expect(failed.commitReceipts).toBe(before.commitReceipts);
  await aiMode('unavailable');
  const manualId = await createManualTask(page, owned.projectId, 'Ручне завдання під час недоступності AI');
  await layoutAndCapture(page, info, 'manual-task');
  expect((await summary()).tasks).toBe(before.tasks + 1);

  const staleContext = await newContext(browser, page);
  try {
    const stale = await staleContext.newPage(); await login(stale, owner);
    await page.getByRole('button', { name: 'Вийти', exact: true }).click();
    await page.goto('/forgot-password');
    await page.getByLabel('Email', { exact: true }).fill(owner.email);
    await page.getByRole('button', { name: 'Надіслати посилання', exact: true }).click();
    await expect(page.getByRole('status')).toContainText('лист для відновлення надіслано');
    const recovery = await mail(owner.email);
    if (!recovery.resetUrl) throw new Error('The real Identity password recovery message was not captured.');
    await privateLink(page, recovery.resetUrl, '/reset-password');
    const changed = actor().password;
    await page.getByLabel('Новий пароль', { exact: true }).fill(changed);
    await page.getByLabel('Повторіть новий пароль', { exact: true }).fill(changed);
    await page.getByRole('button', { name: 'Змінити пароль', exact: true }).click();
    await expect(page.getByRole('status')).toContainText('Пароль змінено');
    expect((await stale.request.get(`/api/tasks/${manualId}`)).status()).toBe(401);
    await stale.goto(`/tasks/${manualId}`); await expect(stale).toHaveURL(/\/login/);
    await login(page, { ...owner, password: changed });
    await page.goto(`/tasks/${manualId}`);
    await expect(page.getByRole('heading', { name: 'Ручне завдання під час недоступності AI', exact: true })).toBeVisible();
    expect((await summary()).tasks).toBe(before.tasks + 1);
  } finally { await staleContext.close(); }
});
