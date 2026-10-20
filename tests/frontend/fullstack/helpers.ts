import { randomBytes, randomUUID } from 'node:crypto';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { expect } from '@playwright/test';
import type { APIRequestContext, Browser, BrowserContext, Page, TestInfo } from '@playwright/test';
import { z } from 'zod';
import { fullStackState } from './state';

export const notes = 'Тестовий Власник завтра підготує міграцію. Тестовий Власник до п’ятниці перевірить API.';
export const noteDate = '2026-10-12';
export const SummarySchema = z.strictObject({
  migrationCount: z.number().int().nonnegative(), tasks: z.number().int().nonnegative(),
  notifications: z.number().int().nonnegative(), processingMarkers: z.number().int().nonnegative(),
  materializations: z.number().int().nonnegative(), commitReceipts: z.number().int().nonnegative(),
  outboxPublished: z.number().int().nonnegative(), outboxPending: z.number().int().nonnegative(),
  acknowledged: z.number().int().nonnegative(),
});
export type Summary = z.infer<typeof SummarySchema>;
const MailSchema = z.object({ confirmationUrl: z.string().nullable(), resetUrl: z.string().nullable() });
const UserSchema = z.object({ id: z.number().int().positive() });
export const DraftSchema = z.object({ id: z.string().uuid(), version: z.string().uuid(), referenceDate: z.string(),
  items: z.array(z.object({ draftItemId: z.string().uuid(), sourceQuote: z.string(), sourceStart: z.number().int(),
    sourceLength: z.number().int(), proposedDueAtUtc: z.string().nullable().optional(),
    proposedAssigneeId: z.number().int().positive().nullable().optional(), materializedTaskId: z.number().int().positive().nullable().optional() })) });
export interface Actor { email: string; password: string; firstName: string; lastName: string; userName: string }
export function actor(firstName = 'Тестовий', lastName = 'Власник'): Actor {
  const id = randomUUID().replaceAll('-', '');
  return { email: `fullstack-${id}@example.test`, userName: `fullstack${id}`,
    password: `Aa9!${randomBytes(24).toString('hex')}`, firstName, lastName };
}

export async function control<T>(path: '/summary' | '/mail' | '/ai/mode' | '/clock/advance' | '/dispatch',
  schema: z.ZodType<T>, body?: unknown, query?: URLSearchParams): Promise<T> {
  try {
    const state = fullStackState();
    const url = new URL(path, state.controlUrl);
    if (query) url.search = query.toString();
    const response = await fetch(url, {
      method: body === undefined ? 'GET' : 'POST', signal: AbortSignal.timeout(10_000), redirect: 'error',
      headers: { 'X-FullStack-Control': state.controlToken, 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (!response.ok) throw new Error();
    return schema.parse(await response.json());
  } catch {
    throw new Error('The private full-stack fixture control request failed.');
  }
}
export const summary = () => control('/summary', SummarySchema);
export const aiMode = (mode: 'normal' | 'unavailable' | 'invalid-output') => control('/ai/mode', z.unknown(), { mode });
export const dispatch = () => control('/dispatch', z.unknown(), {});
export const advanceClock = (minutes: number) => control('/clock/advance', z.unknown(), { minutes });

export async function privateLink(page: Page, value: string, allowedPath: '/confirm-email' | '/reset-password' | '/invite') {
  try {
    const url = new URL(value);
    if (url.origin !== fullStackState().baseUrl || url.pathname !== allowedPath || url.username || url.password || url.hash)
      throw new Error();
    await page.goto(url.href);
  } catch {
    // Playwright navigation errors include the full private link. Discard it.
    throw new Error('The private account link could not be opened on the test gateway.');
  }
}
export async function mail(email: string) {
  return control('/mail', MailSchema, undefined, new URLSearchParams({ email }));
}
export async function login(page: Page, person: Actor) {
  await page.goto('/login');
  await page.getByLabel('Email', { exact: true }).fill(person.email);
  await page.getByLabel('Пароль', { exact: true }).fill(person.password);
  await page.getByRole('button', { name: 'Увійти', exact: true }).click();
  await expect(page.getByRole('heading', { name: /Вітаємо/ })).toBeVisible();
}
export async function registerConfirmLogin(page: Page, person: Actor) {
  await page.goto('/register');
  await page.getByLabel('Ім’я користувача', { exact: true }).fill(person.userName);
  await page.getByLabel('Ім’я', { exact: true }).fill(person.firstName);
  await page.getByLabel('Прізвище', { exact: true }).fill(person.lastName);
  await page.getByLabel('Email', { exact: true }).fill(person.email);
  await page.getByLabel('Пароль', { exact: true }).fill(person.password);
  await page.getByLabel('Повторіть пароль', { exact: true }).fill(person.password);
  await page.getByRole('button', { name: 'Зареєструватися', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('Перевірте пошту');
  const received = await mail(person.email);
  if (!received.confirmationUrl) throw new Error('The real Identity confirmation message was not captured.');
  await privateLink(page, received.confirmationUrl, '/confirm-email');
  await page.getByRole('button', { name: 'Підтвердити email', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('Email підтверджено');
  await login(page, person);
  return productJson(page.request, '/api/auth/me', UserSchema);
}
export async function newContext(browser: Browser, source: Page): Promise<BrowserContext> {
  return browser.newContext({ baseURL: fullStackState().baseUrl, ignoreHTTPSErrors: true,
    viewport: source.viewportSize(), hasTouch: (source.viewportSize()?.width ?? 1440) < 600,
    locale: 'uk-UA', timezoneId: 'Europe/Kyiv', reducedMotion: 'reduce', serviceWorkers: 'block' });
}
export async function productJson<T>(request: APIRequestContext, path: string, schema: z.ZodType<T>): Promise<T> {
  const response = await request.get(path);
  if (!response.ok()) throw new Error(`The real product read failed with status ${response.status()}.`);
  const parsed = schema.safeParse(await response.json());
  if (!parsed.success) throw new Error('The real product response did not match its expected contract.');
  return parsed.data;
}
export async function productWrite(request: APIRequestContext, path: string, method: 'PUT' | 'POST' | 'DELETE', body?: unknown) {
  const csrf = await productJson(request, '/api/auth/csrf', z.object({ requestToken: z.string().min(1) }));
  return request.fetch(path, { method, data: body,
    headers: { Origin: fullStackState().baseUrl, 'X-CSRF-TOKEN': csrf.requestToken } });
}
export async function createWorkspace(page: Page, label: string) {
  // Team names are globally unique in the real domain. Each viewport/story
  // owns a fresh workspace even when both share the same disposable database.
  const suffix = randomUUID().replaceAll('-', '').slice(0, 16);
  const teamName = `Тестова команда ${label} ${suffix}`;
  const projectName = `Перевірка міграції ${label} ${suffix}`;
  await page.goto('/teams');
  await page.getByLabel('Назва команди', { exact: true }).fill(teamName);
  await page.getByRole('button', { name: 'Створити команду', exact: true }).click();
  await expect(page.getByRole('heading', { name: teamName, exact: true })).toBeVisible();
  const teamId = Number(new URL(page.url()).pathname.split('/').at(-1));
  expect(Number.isSafeInteger(teamId) && teamId > 0).toBe(true);
  await page.goto('/projects/new');
  await page.getByLabel('Назва', { exact: true }).fill(projectName);
  await page.getByLabel('Опис', { exact: true }).fill('Синтетичний проєкт у disposable SQL database; реальні product endpoints.');
  // Wrapping label text includes option text; its combobox still has the
  // accessible label prefix. Select the real returned team ID explicitly.
  await page.getByRole('combobox', { name: /^Команда/ }).selectOption(String(teamId));
  await page.getByLabel('Строк завершення', { exact: true }).fill('2030-12-01T18:00');
  await page.getByRole('button', { name: 'Створити проєкт', exact: true }).click();
  await expect(page.getByRole('heading', { name: projectName, exact: true })).toBeVisible();
  const projectId = Number(new URL(page.url()).pathname.split('/').at(-1));
  expect(Number.isSafeInteger(projectId) && projectId > 0).toBe(true);
  return { teamId, projectId };
}
export async function fillNotes(page: Page, projectId: number) {
  await page.goto('/ai-notes');
  await page.getByLabel('Проєкт', { exact: true }).selectOption(String(projectId));
  await page.getByLabel('Нотатки', { exact: true }).fill(notes);
  await page.getByLabel('Дата нотаток (необов’язково)', { exact: true }).fill(noteDate);
  await page.getByLabel('Часовий пояс', { exact: true }).fill('Europe/Kyiv');
}
export async function extract(page: Page, projectId: number) {
  await fillNotes(page, projectId);
  await page.getByRole('button', { name: 'Запропонувати завдання', exact: true }).click();
  await expect(page.getByLabel('Вибрати пропозицію 1')).toBeVisible();
  await expect(page.getByLabel('Вибрати пропозицію 2')).toBeVisible();
  const draftId = new URL(page.url()).searchParams.get('draftId');
  if (!draftId || !z.string().uuid().safeParse(draftId).success) throw new Error('A real persisted extraction draft is required.');
  return productJson(page.request, `/api/ai/task-extractions/${draftId}`, DraftSchema);
}
export async function createManualTask(page: Page, projectId: number, title: string) {
  await page.goto(`/tasks/new?projectId=${projectId}`);
  await page.getByLabel('Назва', { exact: true }).fill(title);
  await page.getByLabel('Опис', { exact: true }).fill('Завдання створено через реальну форму без AI.');
  await page.getByRole('button', { name: 'Створити завдання', exact: true }).click();
  await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();
  const id = Number(new URL(page.url()).pathname.split('/').at(-1));
  expect(Number.isSafeInteger(id) && id > 0).toBe(true);
  return id;
}
export function liveHints(page: Page) {
  let count = 0;
  page.on('websocket', socket => {
    if (new URL(socket.url()).pathname !== '/chathub') return;
    socket.on('framereceived', frame => {
      if (typeof frame.payload !== 'string') return;
      for (const chunk of frame.payload.split('\u001e')) {
        try {
          const message: unknown = JSON.parse(chunk);
          const parsed = z.object({ target: z.literal('InboxChanged'), arguments: z.array(z.object({ notificationId: z.string().uuid() })).length(1) }).safeParse(message);
          if (parsed.success) count++;
        } catch { /* handshake and non-JSON frames are not application hints */ }
      }
    });
  });
  return () => count;
}
export async function layoutAndCapture(page: Page, info: TestInfo, name: 'ai-review' | 'durable-inbox' | 'manual-task') {
  const widths = await page.evaluate(() => ({ viewport: document.documentElement.clientWidth, document: document.documentElement.scrollWidth }));
  expect(widths.document, 'The real workspace must fit its viewport.').toBeLessThanOrEqual(widths.viewport + 1);
  const directory = resolve('test-results/fullstack-screenshots');
  await mkdir(directory, { recursive: true });
  // Called only on synthetic work surfaces after all private account links have
  // been left. No auth inputs, clipboard content or URL bar is captured.
  if (!['/ai-notes', '/notifications'].includes(new URL(page.url()).pathname) && !/^\/tasks\/\d+$/.test(new URL(page.url()).pathname))
    throw new Error('Evidence capture is restricted to synthetic work surfaces.');
  await page.screenshot({ path: resolve(directory, `${info.project.name}-${name}.png`), fullPage: true });
}
