import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AINotesPage } from './AINotesPage';

const boundary = vi.hoisted(() => {
  class TestApiError extends Error {
    constructor(public status: number, public traceId?: string, public fieldErrors: Record<string, string[]> = {}) {
      super('Безпечна помилка API.');
    }
  }
  return { request: vi.fn(), commit: vi.fn(), refresh: vi.fn(), ApiError: TestApiError };
});
vi.mock('../../lib/api', () => ({ request: boundary.request, ApiError: boundary.ApiError }));
vi.mock('../../lib/session', () => ({ useSession: () => ({
  user: { id: 42, userName: 'olena', email: 'olena@example.test', firstName: 'Олена', lastName: 'Коваль', teamId: 17, emailConfirmed: true, roles: [] },
  status: 'authenticated', refresh: boundary.refresh,
}) }));

const notes = 'Олена до п’ятниці перевірить API оплати. За дизайн ще не домовилися.';
const quote = 'Олена до п’ятниці перевірить API оплати.';
const suggestionQuote = 'За дизайн ще не домовилися.';
const draftId = '7a4f8cd9-cd7f-4b36-b9e0-b473f5dff64b';
const itemOne = 'cb17b7a3-e6b3-4203-9830-8f8a84644348';
const itemTwo = 'b9dadff4-ef3e-4181-9b17-80f3f933088a';
const versionOne = '76d8c079-d82c-46c8-a184-58327b8ae6a0';
const versionTwo = 'c5d112b2-22b3-4a6f-8c96-0a98bf832909';
const user = { id: 42, userName: 'olena', email: 'olena@example.test', firstName: 'Олена', lastName: 'Коваль', teamId: 17 };
const team = { id: 17, name: 'Команда API', ownerId: 42, createdAt: '2026-10-01T12:00:00Z', users: [user,
  { ...user, id: 43, userName: 'other-olena', email: 'another.olena@example.test' }] };
const project = { id: 71, name: 'Оновлення API', description: '', authorId: 42, teamId: 17, createdAt: '2026-10-01T12:00:00Z', deadline: '2030-11-01T12:00:00Z' };
function makeDraft() {
  return { id: draftId, ownerId: 42, projectId: 71, version: versionOne,
    createdAtUtc: new Date().toISOString(), expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(), locale: 'uk', timeZoneId: 'Europe/Kyiv', referenceDate: '2026-10-12', items: [
      { draftItemId: itemOne, title: 'Перевірити API оплати', description: 'Перевірка зі стенограмою.', proposedAssigneeId: 42 as number | null,
        originalAssigneeText: 'Олена', proposedDueAtUtc: '2026-10-16T20:59:00Z' as string | null, originalDueText: 'до п’ятниці',
        sourceQuote: quote, sourceStart: 0, sourceLength: quote.length, isSuggestion: false, interpretations: ['date_only_end_of_day', 'deadline_text_resolution'], clarificationReasons: [] as string[], materializedTaskId: null as number | null },
      { draftItemId: itemTwo, title: 'Обговорити дизайн', description: '', proposedAssigneeId: null as number | null,
        originalAssigneeText: null as string | null, proposedDueAtUtc: null as string | null, originalDueText: null as string | null,
        sourceQuote: suggestionQuote, sourceStart: notes.indexOf(suggestionQuote), sourceLength: suggestionQuote.length, isSuggestion: true,
        interpretations: ['description_interpretation'], clarificationReasons: ['commitment_unresolved', 'due_date_missing'], materializedTaskId: null as number | null },
    ] };
}
const receipt = { draftId, draftVersion: versionTwo, items: [{ draftItemId: itemOne, taskId: 501,
  taskVersion: 'fb12a4c4-d45d-4085-8360-0eebd935bafc', outboxMessageId: 'c7f9c2cb-d941-40cf-ab90-3fb4079c31b5' }] };
let draft = makeDraft(); let currentDraft = makeDraft();
let currentTeam = team;
let status = { available: true, provider: 'azure', maxInputCharacters: 20_000, maxItems: 25 };
let extractionFailure: InstanceType<typeof boundary.ApiError> | undefined;

beforeEach(() => {
  draft = makeDraft(); currentDraft = makeDraft(); currentTeam = team; extractionFailure = undefined;
  status = { available: true, provider: 'azure', maxInputCharacters: 20_000, maxItems: 25 };
  boundary.request.mockReset(); boundary.commit.mockReset(); boundary.refresh.mockReset();
  boundary.commit.mockResolvedValue(receipt); boundary.refresh.mockResolvedValue(undefined);
  boundary.request.mockImplementation(async (path: string, _schema: unknown, options?: { method?: string }) => {
    if (path === '/api/ai/status') return status;
    if (path === '/api/projects') return [project];
    if (path === '/api/teams/17') return currentTeam;
    if (path === '/api/ai/task-extractions' && options?.method === 'POST') { if (extractionFailure) throw extractionFailure; return draft; }
    if (path === `/api/ai/task-extractions/${draftId}/commit`) return boundary.commit(options);
    if (path === `/api/ai/task-extractions/${draftId}` && options?.method === 'DELETE') return undefined;
    if (path === `/api/ai/task-extractions/${draftId}`) return currentDraft;
    throw new Error(`Unexpected synthetic test route ${path}`);
  });
});

function page(path = '/ai-notes') { return render(<MemoryRouter initialEntries={[path]}><AINotesPage /></MemoryRouter>); }
function commitCalls() { return boundary.request.mock.calls.filter(call => call[0] === `/api/ai/task-extractions/${draftId}/commit`); }
async function extract(text = notes) {
  const actor = userEvent.setup(); page();
  await actor.selectOptions(await screen.findByLabelText('Проєкт', { exact: true }), '71');
  await actor.click(screen.getByLabelText('Нотатки', { exact: true })); await actor.paste(text);
  const zone = screen.getByLabelText('Часовий пояс', { exact: true }); await actor.clear(zone); await actor.type(zone, 'Europe/Kyiv');
  await actor.click(screen.getByRole('button', { name: 'Запропонувати завдання' }));
  await screen.findByLabelText('Вибрати пропозицію 1'); return actor;
}

describe('AI notes, evidence review and explicit commit', () => {
  it('shows unavailable AI honestly and leaves manual task creation and note input usable', async () => {
    status = { ...status, available: false, provider: 'disabled' }; page();
    expect(await screen.findByText(/AI наразі недоступний/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Запропонувати завдання' })).toBeDisabled();
    expect(screen.getByLabelText('Нотатки', { exact: true })).toBeEnabled();
    expect(screen.getByRole('link', { name: 'Створити завдання вручну' })).toHaveAttribute('href', '/tasks/new');
    expect(boundary.request.mock.calls.some(call => call[0] === '/api/ai/task-extractions')).toBe(false);
  });

  it('posts exact private notes with an explicit project, note date, timezone and locale, creating no tasks', async () => {
    const actor = userEvent.setup(); page();
    await actor.selectOptions(await screen.findByLabelText('Проєкт', { exact: true }), '71');
    await actor.click(screen.getByLabelText('Нотатки', { exact: true })); await actor.paste(notes);
    await actor.type(screen.getByLabelText('Дата нотаток (необов’язково)'), '2026-10-12');
    const zone = screen.getByLabelText('Часовий пояс', { exact: true }); await actor.clear(zone); await actor.type(zone, 'Europe/Kyiv');
    await actor.click(screen.getByRole('button', { name: 'Запропонувати завдання' }));
    await screen.findByLabelText('Вибрати пропозицію 1');
    expect(boundary.request).toHaveBeenCalledWith('/api/ai/task-extractions', expect.anything(), expect.objectContaining({ method: 'POST', body: {
      notesText: notes, projectId: 71, noteDate: '2026-10-12', timeZoneId: 'Europe/Kyiv', locale: 'uk',
    } }));
    expect(boundary.request.mock.calls.some(call => call[0] === '/api/tasks')).toBe(false);
    expect(commitCalls()).toHaveLength(0);
  });

  it('leaves actionable rows and suggestions unchecked and shows original evidence and interpretations', async () => {
    draft.items[1].clarificationReasons.push('future_internal_marker');
    await extract();
    expect(screen.getByLabelText('Вибрати пропозицію 1')).not.toBeChecked();
    expect(screen.getByLabelText('Вибрати пропозицію 2')).not.toBeChecked();
    expect(screen.getByRole('region', { name: 'Таблиця пропозицій' })).toContainElement(screen.getByRole('table'));
    expect(screen.getByText(quote, { exact: true })).toBeInTheDocument();
    expect(screen.getByText('Дату без часу прочитано як 23:59 у часовому поясі Europe/Kyiv. Перевірте, чи цей строк вам підходить.', { exact: true })).toBeInTheDocument();
    expect(screen.getByText('У джерелі немає чіткого зобов’язання. Зберігайте цей рядок лише після явного підтвердження.', { exact: true })).toBeInTheDocument();
    expect(screen.getByText('Строк не вказано. Його можна залишити порожнім або задати вручну.', { exact: true })).toBeInTheDocument();
    expect(screen.getByText('Перевірте цю пропозицію за джерелом і уточніть потрібні поля.', { exact: true })).toBeInTheDocument();
    expect(screen.queryByText(/future_internal_marker|date_only_end_of_day|commitment_unresolved/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Зберегти вибрані завдання' })).toBeDisabled();
  });

  it('does not infer an unresolved duplicate name or deadline and requires an explicit current assignee', async () => {
    draft.items[0] = { ...draft.items[0], proposedAssigneeId: null, proposedDueAtUtc: null, interpretations: [], clarificationReasons: ['assignee_ambiguous', 'due_date_unresolved', 'assignee_membership_changed'] };
    const actor = await extract();
    expect(screen.getByText('Це ім’я мають кілька учасників. Оберіть потрібну людину зі списку.', { exact: true })).toBeInTheDocument();
    expect(screen.getByText('Склад команди змінився після аналізу нотаток. Перевірте поточного виконавця та оберіть його вручну.', { exact: true })).toBeInTheDocument();
    expect(screen.getByLabelText('Виконавець пропозиції 1')).toHaveValue('');
    expect(screen.getByLabelText('Строк пропозиції 1')).toHaveValue('');
    await actor.click(screen.getByLabelText('Вибрати пропозицію 1'));
    await actor.click(screen.getByRole('button', { name: 'Зберегти вибрані завдання' }));
    expect(commitCalls()).toHaveLength(0);
    expect(screen.getByLabelText('Виконавець пропозиції 1')).toHaveAttribute('aria-invalid', 'true');
  });

  it('renders untrusted source text as text without executing or inserting HTML', async () => {
    const maliciousQuote = '<img src=x onerror=alert(1)> Олена перевірить API.';
    draft.items[0] = { ...draft.items[0], sourceQuote: maliciousQuote, sourceLength: maliciousQuote.length };
    await extract(maliciousQuote);
    expect(within(screen.getByRole('table')).getByText(maliciousQuote, { exact: true })).toBeInTheDocument();
    expect(document.querySelector('img')).toBeNull();
  });

  it('commits only selected edited rows with current version and an idempotency header', async () => {
    const actor = await extract(); await actor.click(screen.getByLabelText('Вибрати пропозицію 1'));
    const title = screen.getByLabelText('Назва пропозиції 1'); await actor.clear(title); await actor.paste('Моя уточнена перевірка');
    const description = screen.getByLabelText('Опис пропозиції 1'); await actor.clear(description); await actor.paste('Підтверджено після перевірки джерела.');
    await actor.selectOptions(screen.getByLabelText('Виконавець пропозиції 1'), '43');
    await actor.clear(screen.getByLabelText('Строк пропозиції 1'));
    await actor.click(screen.getByRole('button', { name: 'Зберегти вибрані завдання' }));
    await waitFor(() => expect(commitCalls()).toHaveLength(1));
    const options = commitCalls()[0][2];
    expect(options.body).toEqual({ expectedVersion: versionOne, items: [{ draftItemId: itemOne, title: 'Моя уточнена перевірка',
      description: 'Підтверджено після перевірки джерела.', assigneeId: 43, dueAtUtc: null,
      reminderEnabled: false, reminderOffsetMinutes: null, escalationEnabled: false, escalationDelayMinutes: null }] });
    expect(new Headers(options.headers).get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/i);
    expect(screen.getByText(quote, { exact: true })).toBeInTheDocument();
    await waitFor(() => expect(screen.getAllByRole('link').some(link => link.getAttribute('href') === '/tasks/501')).toBe(true));
    expect(screen.getByLabelText('Вибрати пропозицію 1')).toBeDisabled();
    expect(screen.getByLabelText('Вибрати пропозицію 2')).not.toBeChecked();
    expect(screen.getByLabelText('Вибрати пропозицію 2')).toBeEnabled();
  });

  it('supports a later explicit batch using receipt version without resubmitting materialized rows', async () => {
    const thirdVersion = 'a080caed-d915-4460-9e32-ff41903cfb7b';
    boundary.commit.mockResolvedValueOnce(receipt).mockResolvedValueOnce({ draftId, draftVersion: thirdVersion,
      items: [{ ...receipt.items[0], draftItemId: itemTwo, taskId: 502 }] });
    const actor = await extract(); await actor.click(screen.getByLabelText('Вибрати пропозицію 1'));
    await actor.click(screen.getByRole('button', { name: 'Зберегти вибрані завдання' }));
    await waitFor(() => expect(screen.getByLabelText('Вибрати пропозицію 1')).toBeDisabled());
    await actor.click(screen.getByLabelText('Вибрати пропозицію 2'));
    await actor.selectOptions(screen.getByLabelText('Виконавець пропозиції 2'), '42');
    await actor.click(screen.getByRole('button', { name: 'Зберегти вибрані завдання' }));
    await waitFor(() => expect(commitCalls()).toHaveLength(2));
    const second = commitCalls()[1][2];
    expect(second.body.expectedVersion).toBe(versionTwo);
    expect(second.body.items.map((item: { draftItemId: string }) => item.draftItemId)).toEqual([itemTwo]);
    expect(new Headers(second.headers).get('Idempotency-Key')).not.toBe(new Headers(commitCalls()[0][2].headers).get('Idempotency-Key'));
    await waitFor(() => expect(screen.getAllByRole('link').some(link => link.getAttribute('href') === '/tasks/502')).toBe(true));
  });

  it('freezes exact payload after unknown network outcome and retries with the same idempotency key', async () => {
    boundary.commit.mockRejectedValueOnce(new boundary.ApiError(0)).mockResolvedValueOnce(receipt);
    const actor = await extract(); await actor.click(screen.getByLabelText('Вибрати пропозицію 1'));
    await actor.click(screen.getByRole('button', { name: 'Зберегти вибрані завдання' }));
    const retry = await screen.findByRole('button', { name: 'Повторити той самий запит' });
    expect(screen.getByLabelText('Назва пропозиції 1')).toBeDisabled();
    expect(screen.getByLabelText('Нотатки', { exact: true })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Видалити чернетку' })).toBeDisabled();
    const first = commitCalls()[0][2]; await actor.click(retry);
    await waitFor(() => expect(commitCalls()).toHaveLength(2));
    const second = commitCalls()[1][2];
    expect(second.body).toEqual(first.body);
    expect(new Headers(second.headers).get('Idempotency-Key')).toBe(new Headers(first.headers).get('Idempotency-Key'));
    await waitFor(() => expect(screen.getAllByRole('link').some(link => link.getAttribute('href') === '/tasks/501')).toBe(true));
  });

  it('also retains the exact retry after a server failure instead of permitting a fresh ambiguous commit', async () => {
    boundary.commit.mockRejectedValueOnce(new boundary.ApiError(503, 'trace-commit-unknown'));
    const actor = await extract(); await actor.click(screen.getByLabelText('Вибрати пропозицію 1'));
    await actor.click(screen.getByRole('button', { name: 'Зберегти вибрані завдання' }));
    expect(await screen.findByRole('button', { name: 'Повторити той самий запит' })).toBeEnabled();
    expect(screen.getByRole('alert')).toHaveTextContent('trace-commit-unknown');
    expect(screen.getByLabelText('Вибрати пропозицію 2')).toBeDisabled();
  });

  it('protects an in-flight commit from a double click', async () => {
    let finish!: (value: typeof receipt) => void;
    boundary.commit.mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    const actor = await extract(); await actor.click(screen.getByLabelText('Вибрати пропозицію 1'));
    await actor.dblClick(screen.getByRole('button', { name: 'Зберегти вибрані завдання' }));
    expect(commitCalls()).toHaveLength(1); finish(receipt);
    await waitFor(() => expect(screen.getByLabelText('Вибрати пропозицію 1')).toBeDisabled());
  });

  it('allows correction after known validation rollback and uses a new key for changed payload', async () => {
    boundary.commit.mockRejectedValueOnce(new boundary.ApiError(400, 'trace-validation', { 'items[0].title': ['Invalid title'] })).mockResolvedValueOnce(receipt);
    const actor = await extract(); await actor.click(screen.getByLabelText('Вибрати пропозицію 1'));
    await actor.click(screen.getByRole('button', { name: 'Зберегти вибрані завдання' }));
    await screen.findByText(/trace-validation/); const first = commitCalls()[0][2];
    const title = screen.getByLabelText('Назва пропозиції 1'); expect(title).toBeEnabled();
    expect(title).toHaveAccessibleDescription('Перевірте це поле.'); expect(title).toHaveFocus();
    await actor.clear(title); await actor.type(title, 'Виправлена назва');
    await actor.click(screen.getByRole('button', { name: 'Зберегти вибрані завдання' }));
    await waitFor(() => expect(commitCalls()).toHaveLength(2));
    expect(new Headers(commitCalls()[1][2].headers).get('Idempotency-Key')).not.toBe(new Headers(first.headers).get('Idempotency-Key'));
  });

  it('preserves edits after stale version and refetches immutable evidence before explicit retry', async () => {
    boundary.commit.mockRejectedValueOnce(new boundary.ApiError(409, 'trace-version')).mockResolvedValueOnce(receipt);
    currentDraft = { ...draft, version: versionTwo, items: [draft.items[0], { ...draft.items[1], materializedTaskId: 502 }] };
    const actor = await extract(); await actor.click(screen.getByLabelText('Вибрати пропозицію 1'));
    const title = screen.getByLabelText('Назва пропозиції 1'); await actor.clear(title); await actor.type(title, 'Мої збережені у формі зміни');
    await actor.click(screen.getByRole('button', { name: 'Зберегти вибрані завдання' }));
    await actor.click(await screen.findByRole('button', { name: 'Перевірити актуальну чернетку' }));
    await waitFor(() => expect(screen.getByLabelText('Вибрати пропозицію 2')).toBeDisabled());
    expect(screen.getByLabelText('Назва пропозиції 1')).toHaveValue('Мої збережені у формі зміни');
    expect(screen.getByText(quote, { exact: true })).toBeInTheDocument();
    await actor.click(screen.getByRole('button', { name: 'Зберегти вибрані завдання' }));
    await waitFor(() => expect(commitCalls()).toHaveLength(2));
    expect(commitCalls()[1][2].body.expectedVersion).toBe(versionTwo);
  });

  it('requires explicit fresh review after generic validation without field errors when a cached assignee departed', async () => {
    boundary.commit.mockRejectedValueOnce(new boundary.ApiError(400, 'trace-assignee-validation')).mockResolvedValueOnce(receipt);
    const actor = await extract(); await actor.click(screen.getByLabelText('Вибрати пропозицію 1'));
    const title = screen.getByLabelText('Назва пропозиції 1'); await actor.clear(title); await actor.paste('Мої правки перед зміною команди');
    await actor.selectOptions(screen.getByLabelText('Виконавець пропозиції 1'), '43');
    currentTeam = { ...team, users: [user] };
    const directoryReads = boundary.request.mock.calls.filter(call => call[0] === '/api/teams/17').length;
    await actor.click(screen.getByRole('button', { name: 'Зберегти вибрані завдання' }));
    const first = commitCalls()[0][2];
    const review = await screen.findByRole('button', { name: 'Перевірити актуальну чернетку' });
    expect(title).toHaveValue('Мої правки перед зміною команди'); expect(screen.getByLabelText('Вибрати пропозицію 1')).toBeChecked();
    expect(screen.getByRole('button', { name: 'Зберегти вибрані завдання' })).toBeDisabled();
    expect(boundary.request.mock.calls.filter(call => call[0] === '/api/teams/17')).toHaveLength(directoryReads);
    await actor.click(review);
    await waitFor(() => expect(within(screen.getByLabelText('Виконавець пропозиції 1')).queryByRole('option', { name: /another.olena@example.test/ })).not.toBeInTheDocument());
    expect(boundary.request.mock.calls.some(call => call[0] === `/api/ai/task-extractions/${draftId}`)).toBe(true);
    expect(title).toHaveValue('Мої правки перед зміною команди'); expect(screen.getByLabelText('Вибрати пропозицію 1')).toBeChecked();
    expect(commitCalls()).toHaveLength(1);
    await actor.selectOptions(screen.getByLabelText('Виконавець пропозиції 1'), '42');
    await actor.click(screen.getByRole('button', { name: 'Зберегти вибрані завдання' }));
    await waitFor(() => expect(commitCalls()).toHaveLength(2));
    expect(commitCalls()[1][2].body.items[0]).toMatchObject({ title: 'Мої правки перед зміною команди', assigneeId: 42 });
    expect(new Headers(commitCalls()[1][2].headers).get('Idempotency-Key')).not.toBe(new Headers(first.headers).get('Idempotency-Key'));
  });

  it('preserves local notes and edits when the server reports expiry', async () => {
    boundary.commit.mockRejectedValueOnce(new boundary.ApiError(410));
    const actor = await extract(); await actor.click(screen.getByLabelText('Вибрати пропозицію 1'));
    const title = screen.getByLabelText('Назва пропозиції 1'); await actor.clear(title); await actor.type(title, 'Зміни перед завершенням строку');
    await actor.click(screen.getByRole('button', { name: 'Зберегти вибрані завдання' }));
    expect(await screen.findByText(/Строк дії чернетки минув/)).toBeInTheDocument();
    expect(title).toHaveValue('Зміни перед завершенням строку'); expect(screen.getByLabelText('Нотатки', { exact: true })).toHaveValue(notes);
    expect(screen.getByRole('button', { name: 'Зберегти вибрані завдання' })).toBeDisabled();
  });

  it('clears cached assignee choices after revoked rights, refreshes the session and retains private edits', async () => {
    boundary.commit.mockRejectedValueOnce(new boundary.ApiError(403, 'trace-rights'));
    const actor = await extract(); await actor.click(screen.getByLabelText('Вибрати пропозицію 1'));
    const title = screen.getByLabelText('Назва пропозиції 1'); await actor.clear(title); await actor.type(title, 'Локальні зміни');
    await actor.click(screen.getByRole('button', { name: 'Зберегти вибрані завдання' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('trace-rights');
    expect(boundary.refresh).toHaveBeenCalled(); expect(title).toHaveValue('Локальні зміни');
    expect(screen.queryAllByRole('option', { name: /olena@example.test/ })).toHaveLength(0);
    expect(screen.getByRole('button', { name: 'Зберегти вибрані завдання' })).toBeDisabled();
  });

  it('keeps notes after extraction failure and does not substitute a fake draft', async () => {
    extractionFailure = new boundary.ApiError(503, 'trace-extraction');
    const actor = userEvent.setup(); page(); await actor.selectOptions(await screen.findByLabelText('Проєкт', { exact: true }), '71');
    await actor.type(screen.getByLabelText('Нотатки', { exact: true }), notes);
    await actor.click(screen.getByRole('button', { name: 'Запропонувати завдання' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('trace-extraction');
    expect(screen.getByLabelText('Нотатки', { exact: true })).toHaveValue(notes);
    expect(screen.queryByLabelText('Вибрати пропозицію 1')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Створити завдання вручну' })).toHaveAttribute('href', '/tasks/new?projectId=71');
  });

  it('deletes its draft explicitly without retaining notes in browser storage or URL', async () => {
    const actor = await extract();
    await actor.click(screen.getByRole('button', { name: 'Видалити чернетку' }));
    expect(boundary.request).toHaveBeenCalledWith(`/api/ai/task-extractions/${draftId}`, undefined, expect.objectContaining({ method: 'DELETE' }));
    await waitFor(() => expect(screen.queryByLabelText('Вибрати пропозицію 1')).not.toBeInTheDocument());
    expect(localStorage.length).toBe(0); expect(sessionStorage.length).toBe(0);
    expect(window.location.href).not.toContain(encodeURIComponent(notes));
  });

  it('resumes an authored draft from authenticated GET without fabricating the original complete notes', async () => {
    page(`/ai-notes?draftId=${draftId}`);
    await screen.findByLabelText('Вибрати пропозицію 1');
    expect(boundary.request).toHaveBeenCalledWith(`/api/ai/task-extractions/${draftId}`, expect.anything(), expect.anything());
    expect(screen.getByLabelText('Нотатки', { exact: true })).toHaveValue('');
    expect(screen.getByText(quote, { exact: true })).toBeInTheDocument();
    expect(screen.getByLabelText('Вибрати пропозицію 1')).not.toBeChecked();
  });
});
