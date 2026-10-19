import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, request } from '../../lib/api';
import type { AIDraft } from '../../lib/contracts';
import { NotificationInboxItemSchema, NotificationInboxPageSchema } from '../../lib/generated/notifications';
import { setLocale } from '../../lib/i18n';
import { NotificationsContext } from '../notifications/context';
import type { NotificationsState } from '../notifications/context';
import { InboxPage, NotificationSummary } from '../notifications/InboxPage';
import { notificationsCopy } from '../notifications/copy';
import { AINotesPage } from './AINotesPage';
import { aiCopy, explainAiMarker } from './aiCopy';

vi.mock('../../lib/api', async original => ({ ...await original<typeof import('../../lib/api')>(), request: vi.fn() }));
vi.mock('../../lib/session', () => ({ useSession: () => ({
  user: { id: 42, teamId: 17, roles: [], emailConfirmed: true }, refresh: vi.fn(),
}) }));

const draftId = '7a4f8cd9-cd7f-4b36-b9e0-b473f5dff64b';
const itemId = 'cb17b7a3-e6b3-4203-9830-8f8a84644348';
const version = '76d8c079-d82c-46c8-a184-58327b8ae6a0';
const quote = 'Олена перевірить API до п’ятниці.';
const notes = `${quote} Приватні синтетичні нотатки для тесту мови.`;
const title = 'Перевірити API';
const member = { id: 42, userName: 'olena', email: 'olena@example.test', teamId: 17, firstName: 'Олена', lastName: 'Коваль' };
const project = { id: 71, name: 'Проєкт з користувацьким ім’ям', teamId: 17 };
let draft: AIDraft;
const commit = vi.fn();
const notification = NotificationInboxItemSchema.parse({ id: itemId, messageId: itemId, kind: 3, taskId: 501, projectId: 71,
  taskName: 'Завдання з користувацькою назвою', projectName: project.name, occurredAtUtc: '2026-10-12T09:00:00Z' });
let inbox: NotificationsState;

beforeEach(() => {
  localStorage.clear(); setLocale('uk');
  draft = { id: draftId, ownerId: 42, projectId: 71, version, createdAtUtc: '2026-10-12T09:00:00Z', expiresAtUtc: '2030-10-13T09:00:00Z',
    locale: 'en', timeZoneId: 'Europe/Kyiv', referenceDate: '2026-10-12', items: [{ draftItemId: itemId, title, description: '',
      proposedAssigneeId: 42, originalAssigneeText: 'Олена', proposedDueAtUtc: null, originalDueText: null,
      sourceQuote: quote, sourceStart: 0, sourceLength: quote.length, isSuggestion: false,
      interpretations: ['date_only_end_of_day'], clarificationReasons: ['due_date_missing'], materializedTaskId: null }] };
  commit.mockReset();
  commit.mockResolvedValue({ draftId, draftVersion: version, items: [{ draftItemId: itemId, taskId: 501, taskVersion: version, outboxMessageId: version }] });
  vi.mocked(request).mockReset(); vi.mocked(request).mockImplementation(async (path, _schema, options) => {
    if (path === '/api/ai/status') return { available: true, provider: 'azure', maxInputCharacters: 20_000, maxItems: 25 };
    if (path === '/api/projects') return [project];
    if (path === '/api/teams/17') return { id: 17, users: [member] };
    if (path === '/api/ai/task-extractions') return draft;
    if (path === `/api/ai/task-extractions/${draftId}/commit`) return commit(options);
    if (path === `/api/ai/task-extractions/${draftId}`) return draft;
    throw new Error('Unexpected synthetic locale request');
  });
  inbox = { data: NotificationInboxPageSchema.parse({ items: [notification], page: 2, pageSize: 20, totalCount: 41, unreadCount: 7 }),
    loading: false, error: undefined, connection: 'offline', page: 2, unreadOnly: true, pendingRead: new Set(),
    reload: vi.fn().mockResolvedValue(undefined), reconnect: vi.fn().mockResolvedValue(undefined), markRead: vi.fn().mockResolvedValue(undefined),
    setPage: vi.fn(), setUnreadOnly: vi.fn() };
});

function page() { return render(<MemoryRouter><AINotesPage /></MemoryRouter>); }
async function extract() {
  const actor = userEvent.setup(); page();
  await actor.selectOptions(await screen.findByLabelText('Проєкт'), '71');
  await actor.click(screen.getByLabelText('Нотатки')); await actor.paste(notes);
  await actor.selectOptions(screen.getByLabelText('Мова відповіді'), 'en');
  await actor.click(screen.getByRole('button', { name: 'Запропонувати завдання' }));
  await screen.findByLabelText('Вибрати пропозицію 1');
  return actor;
}
function expectParity(uk: unknown, en: unknown) {
  if (typeof uk === 'string') {
    expect(typeof en).toBe('string');
    const placeholders = (value: string) => Array.from(value.matchAll(/\{([A-Za-z][A-Za-z0-9]*)\}/g), match => match[1]).sort();
    expect(placeholders(en as string)).toEqual(placeholders(uk));
    return;
  }
  if (Array.isArray(uk)) {
    expect(Array.isArray(en)).toBe(true); expect((en as unknown[]).length).toBe(uk.length);
    uk.forEach((value, index) => expectParity(value, (en as unknown[])[index])); return;
  }
  expect(Object.keys(en as object).sort()).toEqual(Object.keys(uk as object).sort());
  for (const key of Object.keys(uk as object)) expectParity((uk as Record<string, unknown>)[key], (en as Record<string, unknown>)[key]);
}

describe('AI and inbox reactive UI language', () => {
  it('keeps paired copy keys and interpolation placeholders aligned', () => {
    expectParity(aiCopy.uk, aiCopy.en); expectParity(notificationsCopy.uk, notificationsCopy.en);
    for (const locale of ['uk', 'en'] as const) {
      expect(explainAiMarker('date_only_end_of_day', locale, 'Europe/Kyiv')).toContain('Europe/Kyiv');
      expect(explainAiMarker('date_only_end_of_day', locale, 'Europe/Kyiv')).not.toContain('{zone}');
    }
  });

  it('renders English AI labels and marker explanations independently of the draft response language', async () => {
    draft.locale = 'uk'; setLocale('en');
    render(<MemoryRouter initialEntries={[`/ai-notes?draftId=${draftId}`]}><AINotesPage /></MemoryRouter>);
    expect(await screen.findByRole('heading', { name: 'Tasks from notes' })).toBeInTheDocument();
    expect(await screen.findByRole('region', { name: 'Proposal table' })).toBeInTheDocument();
    expect(screen.getByLabelText('Response language')).toHaveValue('uk');
    expect(screen.getByText(/A date without a time was read as 23:59 in the Europe\/Kyiv time zone/)).toBeInTheDocument();
    expect(screen.getByText('No deadline was specified. You can leave it empty or set it manually.')).toBeInTheDocument();
    expect(screen.getByText(quote)).toBeInTheDocument(); expect(screen.getByLabelText('Title proposal 1')).toHaveValue(title);
    expect(draft.locale).toBe('uk');
  });

  it('preserves notes, edited selection and an explicit AI response-language choice after a runtime UI switch', async () => {
    const actor = await extract();
    await actor.click(screen.getByLabelText('Вибрати пропозицію 1'));
    await actor.clear(screen.getByLabelText('Назва пропозиції 1')); await actor.type(screen.getByLabelText('Назва пропозиції 1'), 'Мої точні правки');
    act(() => setLocale('en'));
    expect(screen.getByLabelText('Notes')).toHaveValue(notes); expect(screen.getByLabelText('Response language')).toHaveValue('en');
    expect(screen.getByLabelText('Select proposal 1')).toBeChecked(); expect(screen.getByLabelText('Title proposal 1')).toHaveValue('Мої точні правки');
    expect(screen.getByText(quote)).toBeInTheDocument(); expect(draft.items[0].title).toBe(title);
    const extraction = vi.mocked(request).mock.calls.find(([path]) => path === '/api/ai/task-extractions');
    expect(extraction?.[2]?.body).toMatchObject({ notesText: notes, locale: 'en' });
    expect(Array.from({ length: localStorage.length }, (_, index) => localStorage.key(index))).toEqual(['taskmanager.ui-locale']);
    expect(localStorage.getItem('taskmanager.ui-locale')).toBe('en');
  });

  it('translates stored validation messages without resetting input values', async () => {
    const actor = userEvent.setup(); page(); await actor.selectOptions(await screen.findByLabelText('Проєкт'), '71');
    await actor.click(screen.getByRole('button', { name: 'Запропонувати завдання' }));
    expect(screen.getByText('Введіть нотатки з домовленостями.')).toBeInTheDocument();
    act(() => setLocale('en'));
    expect(screen.getByText('Enter notes containing commitments.')).toBeInTheDocument();
    expect(screen.getByLabelText('Project')).toHaveValue('71'); expect(screen.getByLabelText('Notes')).toHaveValue('');
  });

  it('preserves the exact unknown-outcome commit body and key across a language switch', async () => {
    commit.mockRejectedValueOnce(new ApiError(503)); const actor = await extract();
    await actor.click(screen.getByLabelText('Вибрати пропозицію 1'));
    await actor.click(screen.getByRole('button', { name: 'Зберегти вибрані завдання' }));
    await screen.findByRole('button', { name: 'Повторити той самий запит' });
    const original = commit.mock.calls[0][0];
    act(() => setLocale('en'));
    expect(screen.getByLabelText('Notes')).toHaveValue(notes);
    expect(screen.getByLabelText('Select proposal 1')).toBeChecked(); expect(screen.getByLabelText('Select proposal 1')).toBeDisabled();
    await actor.click(screen.getByRole('button', { name: 'Retry the identical request' }));
    await screen.findByRole('link', { name: 'Open created task' });
    expect(commit.mock.calls[1][0]).toEqual(original);
    expect(screen.getByText('Selected tasks saved.')).toBeInTheDocument();
  });

  it('keeps an in-flight commit locked and translates its eventual success without sending another request', async () => {
    let finish!: (value: unknown) => void;
    commit.mockReturnValueOnce(new Promise(resolve => { finish = resolve; }));
    const actor = await extract();
    await actor.click(screen.getByLabelText('Вибрати пропозицію 1'));
    await actor.click(screen.getByRole('button', { name: 'Зберегти вибрані завдання' }));
    await waitFor(() => expect(commit).toHaveBeenCalledTimes(1));
    const original = commit.mock.calls[0][0];
    act(() => setLocale('en'));
    expect(screen.getByLabelText('Select proposal 1')).toBeChecked(); expect(screen.getByLabelText('Select proposal 1')).toBeDisabled();
    expect(screen.getByLabelText('Notes')).toHaveValue(notes); expect(screen.getByLabelText('Response language')).toHaveValue('en');
    await act(async () => { finish({ draftId, draftVersion: version, items: [{ draftItemId: itemId, taskId: 501, taskVersion: version, outboxMessageId: version }] }); });
    await screen.findByRole('link', { name: 'Open created task' });
    expect(screen.getByText('Selected tasks saved.')).toBeInTheDocument();
    expect(commit).toHaveBeenCalledTimes(1); expect(commit.mock.calls[0][0]).toBe(original);
    act(() => setLocale('uk'));
    expect(screen.getByText('Вибрані завдання збережено.')).toBeInTheDocument();
  });

  it('renders English inbox status/actions while leaving user content and read state unchanged', async () => {
    setLocale('en'); const actor = userEvent.setup();
    render(<MemoryRouter><NotificationsContext.Provider value={inbox}><InboxPage /><NotificationSummary /></NotificationsContext.Provider></MemoryRouter>);
    expect(screen.getByRole('heading', { name: 'Notifications' })).toBeInTheDocument();
    expect(screen.getByText('Unread notifications')).toBeInTheDocument(); expect(screen.getByText('New task')).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Unread only' })).toBeChecked();
    expect(screen.getByRole('link', { name: notification.taskName })).toHaveAttribute('href', '/tasks/501');
    await actor.click(screen.getByRole('button', { name: 'Reconnect' })); expect(inbox.reconnect).toHaveBeenCalledTimes(1);
    await actor.click(screen.getByRole('button', { name: `Mark as read: ${notification.taskName}` })); expect(inbox.markRead).toHaveBeenCalledWith(itemId);
    expect(inbox.data?.page).toBe(2); expect(inbox.data?.unreadCount).toBe(7);
  });

  it('switches an existing inbox without changing loaded rows, filters or a pending mark-read', async () => {
    inbox.pendingRead = new Set([itemId]);
    render(<MemoryRouter><NotificationsContext.Provider value={inbox}><InboxPage /></NotificationsContext.Provider></MemoryRouter>);
    expect(screen.getByRole('button', { name: `Позначити прочитаним: ${notification.taskName}` })).toBeDisabled();
    act(() => setLocale('en'));
    await waitFor(() => expect(screen.getByRole('button', { name: `Mark as read: ${notification.taskName}` })).toBeDisabled());
    expect(screen.getByRole('checkbox', { name: 'Unread only' })).toBeChecked(); expect(inbox.reload).not.toHaveBeenCalled();
    expect(inbox.data?.items[0]).toEqual(notification); expect(inbox.page).toBe(2); expect(inbox.pendingRead.has(itemId)).toBe(true);
  });
});
