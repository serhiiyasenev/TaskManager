import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../App';
import { SessionContext } from '../../lib/session';
import type { SessionContextValue } from '../../lib/session';
import type { SessionProfile } from '../../lib/contracts';
import { NotificationInboxItemSchema, NotificationInboxPageSchema } from '../../lib/generated/notifications';
import type { NotificationInboxItem, NotificationInboxPage } from '../../lib/generated/notifications';
import { DashboardPage } from '../work/DashboardPage';
import { NotificationsProvider } from './Provider';
import { InboxPage } from './InboxPage';

const boundary = vi.hoisted(() => {
  class TestApiError extends Error {
    constructor(public status: number, public traceId?: string) { super('Безпечна помилка запиту.'); }
  }
  return { request: vi.fn(), refresh: vi.fn(), read: vi.fn(), ApiError: TestApiError };
});
const realtime = vi.hoisted(() => {
  const failures: unknown[] = [];
  class Connection {
    handlers = new Map<string, (value: unknown) => void>();
    reconnected?: () => void; reconnecting?: () => void; closed?: () => void;
    start = vi.fn(async () => { if (failures.length) throw failures.shift(); });
    stop = vi.fn(async () => {});
    on = vi.fn((name: string, handler: (value: unknown) => void) => this.handlers.set(name, handler));
    off = vi.fn((name: string) => this.handlers.delete(name));
    onreconnected(callback: () => void) { this.reconnected = callback; }
    onreconnecting(callback: () => void) { this.reconnecting = callback; }
    onclose(callback: () => void) { this.closed = callback; }
  }
  const connections: Connection[] = [];
  const urls: Array<{ path: string; options?: Record<string, unknown> }> = [];
  class Builder {
    withUrl(path: string, options?: Record<string, unknown>) { urls.push({ path, options }); return this; }
    withAutomaticReconnect() { return this; }
    configureLogging() { return this; }
    build() { const connection = new Connection(); connections.push(connection); return connection; }
  }
  return { connections, urls, failures, Builder };
});
vi.mock('../../lib/api', () => ({ request: boundary.request, ApiError: boundary.ApiError }));
vi.mock('@microsoft/signalr', () => ({ HubConnectionBuilder: realtime.Builder, LogLevel: { None: 6 } }));

const idOne = '45e38795-92cf-46aa-8850-7c06d17d0d34';
const idTwo = '4b867cd7-3452-411b-9092-92cfc5341f03';
const idThree = '8fcf2018-8eac-4c38-8ab2-5ce44dcc3c76';
const idFour = 'a6d791a2-6f42-4c59-9c76-a9b5413bd365';
const actor: SessionProfile = { id: 42, userName: 'olena', firstName: 'Олена', lastName: 'Коваль', email: 'olena@example.test', emailConfirmed: true, teamId: 17, roles: [] };
function item(id = idOne, kind: NotificationInboxItem['kind'] = 3, name = 'Перевірити API') {
  return NotificationInboxItemSchema.parse({ id, messageId: id, kind, taskId: 101, projectId: 71,
    taskName: name, projectName: 'Команда API', occurredAtUtc: '2026-10-12T09:00:00Z' });
}
function fixture(items: NotificationInboxItem[], unreadCount = items.filter(value => !value.readAtUtc).length, totalCount = items.length): NotificationInboxPage {
  return NotificationInboxPageSchema.parse({ items, page: 1, pageSize: 20, totalCount, unreadCount });
}
let reply = fixture([item()], 7, 41);
let loadOverride: ((options: { page: number; unreadOnly: boolean; signal?: AbortSignal }) => Promise<unknown>) | undefined;
function session(user: SessionProfile | null = actor): SessionContextValue {
  return { user, status: user ? 'authenticated' : 'anonymous', refresh: boundary.refresh, login: async () => {}, logout: async () => {} };
}
function shell(value = session(), child = <InboxPage />) {
  return <MemoryRouter><SessionContext.Provider value={value}><NotificationsProvider>{child}</NotificationsProvider></SessionContext.Provider></MemoryRouter>;
}
function loads() { return boundary.request.mock.calls.filter(call => (call[0] as string).startsWith('/api/notifications?')); }
async function ready() {
  await screen.findByRole('link', { name: 'Перевірити API' });
  await waitFor(() => expect(realtime.connections[0]?.start).toHaveBeenCalledTimes(1));
  await screen.findByText('Зв’язок наживо встановлено.');
  await waitFor(() => expect(screen.getByRole('button', { name: 'Оновити сповіщення' })).toBeEnabled());
}
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(finish => { resolve = finish; }); return { promise, resolve }; }

beforeEach(() => {
  reply = fixture([item()], 7, 41); loadOverride = undefined;
  boundary.request.mockReset(); boundary.refresh.mockReset(); boundary.read.mockReset(); boundary.refresh.mockResolvedValue(undefined);
  realtime.connections.length = 0; realtime.urls.length = 0; realtime.failures.length = 0;
  boundary.read.mockImplementation(async (id: string) => { const read = { ...reply.items.find(value => value.id === id)!, readAtUtc: '2026-10-12T10:00:00Z' };
    reply = { ...reply, items: reply.items.map(value => value.id === id ? read : value), unreadCount: 6 }; return read; });
  boundary.request.mockImplementation(async (path: string, schema?: { parse(value: unknown): unknown }, options: { method?: string; signal?: AbortSignal } = {}) => {
    let value: unknown;
    if (path.startsWith('/api/notifications?')) {
      const query = new URL(path, 'https://fixture.test').searchParams;
      const page = Number(query.get('page')); const unreadOnly = query.get('unreadOnly') === 'true';
      value = loadOverride ? await loadOverride({ page, unreadOnly, signal: options.signal }) : { ...reply, page };
    } else if (path.endsWith('/read') && options.method === 'PUT') value = await boundary.read(path.split('/').at(-2));
    else if (path === '/api/projects') value = [];
    else if (path.startsWith('/api/tasks/page?')) value = { items: [], page: 1, pageSize: 5, totalCount: 0 };
    else throw new Error(`Unexpected synthetic route ${path}`);
    try { return schema ? schema.parse(value) : value; } catch { throw new boundary.ApiError(502); }
  });
});
afterEach(() => { vi.useRealTimers(); });

describe('private canonical inbox and authenticated realtime hints', () => {
  it('renders all product kinds and canonical links without exposing internal identifiers or HTML', async () => {
    reply = fixture([item(idOne, 0), item(idTwo, 1, 'Нагадування API'), item(idThree, 2, 'Прострочений API'), item(idFour, 3, '<img src=x onerror=alert(1)>')]);
    render(shell()); await ready();
    for (const text of ['Завдання виконано', 'Нагадування про строк', 'Завдання прострочено', 'Нове завдання']) expect(screen.getByText(text)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '<img src=x onerror=alert(1)>' })).toHaveAttribute('href', '/tasks/101');
    expect(document.querySelector('img')).toBeNull(); expect(document.body.textContent).not.toContain(idOne);
    expect(realtime.urls[0].path).toBe('/chathub'); expect(realtime.urls[0].options?.skipNegotiation).not.toBe(true);
    expect(realtime.urls[0].options?.accessTokenFactory).toBeUndefined(); expect(localStorage.length).toBe(0); expect(sessionStorage.length).toBe(0);
  });

  it('shows an honest empty inbox and uses the server unread count on the dashboard instead of page length', async () => {
    render(shell(session(), <DashboardPage />));
    await screen.findByText('Непрочитані сповіщення');
    const stat = screen.getByText('Непрочитані сповіщення').closest('.stat')!;
    await waitFor(() => expect(within(stat as HTMLElement).getByText('7', { exact: true })).toBeInTheDocument());
    expect(within(stat as HTMLElement).queryByText('1', { exact: true })).not.toBeInTheDocument();
  });

  it('explains empty results while preserving connection status and a working refresh action', async () => {
    reply = fixture([]); render(shell());
    expect(await screen.findByRole('heading', { name: 'Сповіщень поки немає' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Оновити сповіщення' }));
    expect(loads().length).toBeGreaterThan(1); expect(screen.queryByRole('link', { name: 'Перевірити API' })).not.toBeInTheDocument();
  });

  it('marks a notification read with a bodyless idempotent PUT, blocks double clicks and refetches canonical counts', async () => {
    const pending = deferred<NotificationInboxItem>(); boundary.read.mockImplementationOnce(() => pending.promise);
    render(shell()); await ready(); const actorInput = userEvent.setup();
    await actorInput.dblClick(screen.getByRole('button', { name: 'Позначити прочитаним: Перевірити API' }));
    expect(boundary.read).toHaveBeenCalledTimes(1); const readCall = boundary.request.mock.calls.find(call => call[0] === `/api/notifications/${idOne}/read`)!;
    expect(readCall[2]).toMatchObject({ method: 'PUT' }); expect(readCall[2].body).toBeUndefined();
    reply = fixture([{ ...item(), readAtUtc: '2026-10-12T10:00:00Z' }], 6, 41);
    await act(async () => { pending.resolve(reply.items[0]); });
    expect(await screen.findByText('Прочитано')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Позначити прочитаним: Перевірити API' })).not.toBeInTheDocument();
  });

  it('filters unread rows, resets pagination and bases filtered pages on global unread count', async () => {
    loadOverride = async ({ page, unreadOnly }) => ({ ...fixture(page === 1 ? [item()] : [item(idTwo, 1, 'Сторінка два')], 21, 61), page,
      items: unreadOnly || page === 1 ? [item(page === 1 ? idOne : idTwo, 1, page === 1 ? 'Перевірити API' : 'Сторінка два')] : [] });
    render(shell()); await ready(); const actorInput = userEvent.setup(); await actorInput.click(screen.getByLabelText('Лише непрочитані'));
    await waitFor(() => expect(loads().at(-1)?.[0]).toContain('unreadOnly=true'));
    expect(await screen.findByText(/Сторінка 1 з 2/)).toBeInTheDocument();
    await actorInput.click(screen.getByRole('button', { name: 'Далі' })); await screen.findByRole('link', { name: 'Сторінка два' });
    expect(loads().at(-1)?.[0]).toContain('page=2'); expect(screen.getByRole('button', { name: 'Далі' })).toBeDisabled();
  });

  it('respects the current unread filter when an earlier read request completes', async () => {
    const read = deferred<NotificationInboxItem>(); boundary.read.mockImplementationOnce(() => read.promise);
    render(shell()); await ready(); const actorInput = userEvent.setup();
    await actorInput.click(screen.getByRole('button', { name: 'Позначити прочитаним: Перевірити API' }));
    await actorInput.click(screen.getByLabelText('Лише непрочитані'));
    await waitFor(() => expect(loads().at(-1)?.[0]).toContain('unreadOnly=true'));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Оновити сповіщення' })).toBeEnabled());
    const latest = deferred<NotificationInboxPage>(); loadOverride = async () => latest.promise;
    await act(async () => { read.resolve({ ...item(), readAtUtc: '2026-10-12T10:00:00Z' }); });
    expect(screen.queryByRole('link', { name: 'Перевірити API' })).not.toBeInTheDocument();
    await act(async () => { latest.resolve(fixture([], 0, 41)); });
    expect(await screen.findByRole('heading', { name: 'Непрочитаних сповіщень немає' })).toBeInTheDocument();
  });

  it('ignores malformed or repeated hints and refetches authoritative data after reconnect', async () => {
    render(shell()); await ready(); const connection = realtime.connections[0]; const hint = connection.handlers.get('InboxChanged')!;
    const before = loads().length;
    await act(async () => { hint({ notificationId: idTwo, taskName: 'Untrusted extra field' }); hint({ notificationId: 'not-a-guid' }); });
    expect(loads()).toHaveLength(before);
    reply = fixture([item(), item(idTwo, 1, 'Нове нагадування')], 8, 42);
    await act(async () => { hint({ notificationId: idTwo }); hint({ notificationId: idTwo }); });
    await screen.findByRole('link', { name: 'Нове нагадування' }); expect(loads()).toHaveLength(before + 1);
    await act(async () => { connection.reconnecting?.(); }); expect(screen.getByText(/Відновлюємо зв’язок/)).toBeInTheDocument();
    await act(async () => { connection.reconnected?.(); }); await waitFor(() => expect(loads()).toHaveLength(before + 2));
    expect(screen.getAllByRole('link', { name: 'Нове нагадування' })).toHaveLength(1);
    await act(async () => { connection.closed?.(); }); await waitFor(() => expect(loads()).toHaveLength(before + 3));
    expect(screen.getByText(/Немає з’єднання наживо/)).toBeInTheDocument();
  });

  it('bounds initial start retries and offers explicit reconnect while HTTP inbox remains usable', async () => {
    vi.useFakeTimers(); realtime.failures.push(...Array.from({ length: 4 }, () => new Error('Synthetic unavailable socket')));
    render(shell()); await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });
    expect(realtime.connections[0].start).toHaveBeenCalledTimes(4);
    expect(screen.getByRole('link', { name: 'Перевірити API' })).toBeInTheDocument(); expect(screen.getByText(/Немає з’єднання наживо/)).toBeInTheDocument();
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); }); expect(realtime.connections[0].start).toHaveBeenCalledTimes(4);
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Відновити з’єднання' })); });
    await act(async () => { await Promise.resolve(); }); expect(realtime.connections[0].start).toHaveBeenCalledTimes(5);
  });

  it('clears private rows, stops the socket and ignores late HTTP/hints after logout', async () => {
    const view = render(shell()); await ready(); const old = realtime.connections[0]; const hint = old.handlers.get('InboxChanged')!;
    const late = deferred<NotificationInboxPage>(); let requestSignal: AbortSignal | undefined;
    loadOverride = async ({ signal }) => { requestSignal = signal; return late.promise; };
    await act(async () => { hint({ notificationId: idTwo }); });
    view.rerender(shell(session(null))); expect(screen.queryByRole('link', { name: 'Перевірити API' })).not.toBeInTheDocument();
    expect(requestSignal?.aborted).toBe(true); expect(old.stop).toHaveBeenCalled(); const count = loads().length;
    await act(async () => { late.resolve(fixture([item(idTwo, 1, 'Приватний старий рядок')])); hint({ notificationId: idThree }); });
    expect(screen.queryByText('Приватний старий рядок')).not.toBeInTheDocument(); expect(loads()).toHaveLength(count);
  });

  it('does not let an old account response or callback populate a new account inbox', async () => {
    const view = render(shell()); await ready(); const hint = realtime.connections[0].handlers.get('InboxChanged')!;
    const late = deferred<NotificationInboxPage>(); loadOverride = async () => late.promise;
    await act(async () => { hint({ notificationId: idTwo }); });
    loadOverride = undefined; reply = fixture([item(idThree, 3, 'Нове приватне завдання')], 2, 2);
    view.rerender(shell(session({ ...actor, id: 43, userName: 'another' })));
    await screen.findByRole('link', { name: 'Нове приватне завдання' }); const count = loads().length;
    await act(async () => { late.resolve(fixture([item(idTwo, 1, 'Чужа стара відповідь')])); hint({ notificationId: idFour }); });
    expect(screen.queryByText('Чужа стара відповідь')).not.toBeInTheDocument(); expect(loads()).toHaveLength(count);
  });

  it.each([{ teamId: 18, roles: [] }, { teamId: 17, roles: ['Admin'] }])('clears scope changes without remounting unrelated draft inputs: %j', async change => {
    const child = <><input aria-label="Незбережена назва" defaultValue="Мої правки" /><InboxPage /></>;
    const view = render(shell(session(), child)); await ready(); const input = screen.getByLabelText('Незбережена назва');
    await userEvent.clear(input); await userEvent.type(input, 'Незбережені нові правки');
    const old = realtime.connections[0]; const late = deferred<NotificationInboxPage>(); loadOverride = async () => late.promise;
    view.rerender(shell(session({ ...actor, ...change }), child));
    expect(screen.queryByRole('link', { name: 'Перевірити API' })).not.toBeInTheDocument(); expect(old.stop).toHaveBeenCalled();
    expect(screen.getByLabelText('Незбережена назва')).toBe(input); expect(input).toHaveValue('Незбережені нові правки');
    await act(async () => { late.resolve(fixture([])); });
  });

  it.each([401, 403])('clears private rows and refreshes the session on HTTP %i', async status => {
    render(shell()); await ready(); const old = realtime.connections[0];
    loadOverride = async () => { throw new boundary.ApiError(status, 'synthetic-inbox-rights'); };
    await userEvent.click(screen.getByRole('button', { name: 'Оновити сповіщення' }));
    await screen.findByText(/synthetic-inbox-rights/); expect(screen.queryByRole('link', { name: 'Перевірити API' })).not.toBeInTheDocument();
    expect(boundary.refresh).toHaveBeenCalled(); expect(old.stop).toHaveBeenCalled();
  });

  it('rechecks canonical access before reconnecting after 403 and keeps private hints blocked until success', async () => {
    render(shell()); await ready(); const connection = realtime.connections[0]; const oldHint = connection.handlers.get('InboxChanged')!;
    loadOverride = async () => { throw new boundary.ApiError(403, 'synthetic-inbox-rights'); };
    const user = userEvent.setup(); await user.click(screen.getByRole('button', { name: 'Оновити сповіщення' }));
    await screen.findByText(/synthetic-inbox-rights/); const deniedLoads = loads().length;
    await user.click(screen.getByRole('button', { name: 'Відновити з’єднання' }));
    await waitFor(() => expect(loads()).toHaveLength(deniedLoads + 1));
    expect(connection.start).toHaveBeenCalledTimes(1); expect(screen.queryByRole('link', { name: 'Перевірити API' })).not.toBeInTheDocument();
    const fresh = deferred<NotificationInboxPage>(); loadOverride = async () => fresh.promise;
    await user.dblClick(screen.getByRole('button', { name: 'Відновити з’єднання' }));
    await waitFor(() => expect(loads()).toHaveLength(deniedLoads + 2));
    expect(screen.getByRole('button', { name: 'Відновити з’єднання' })).toBeDisabled();
    await act(async () => { oldHint({ notificationId: idTwo }); });
    expect(loads()).toHaveLength(deniedLoads + 2); expect(connection.start).toHaveBeenCalledTimes(1);
    reply = fixture([item(idTwo, 1, 'Актуально доступне завдання')]);
    await act(async () => { fresh.resolve(reply); });
    expect(await screen.findByRole('link', { name: 'Актуально доступне завдання' })).toBeInTheDocument();
    await screen.findByText('Зв’язок наживо встановлено.'); expect(connection.start).toHaveBeenCalledTimes(2);
  });

  it('removes an inaccessible notification after read 404 and reloads current visible counts', async () => {
    boundary.read.mockImplementationOnce(async () => { reply = fixture([]); throw new boundary.ApiError(404); });
    render(shell()); await ready(); await userEvent.click(screen.getByRole('button', { name: 'Позначити прочитаним: Перевірити API' }));
    await waitFor(() => expect(screen.queryByRole('link', { name: 'Перевірити API' })).not.toBeInTheDocument());
    expect(await screen.findByRole('heading', { name: 'Сповіщень поки немає' })).toBeInTheDocument();
  });

  it('retains previously loaded rows during a network error with safe retry instead of invented empty content', async () => {
    render(shell()); await ready(); loadOverride = async () => { throw new boundary.ApiError(503, 'synthetic-inbox-outage'); };
    await userEvent.click(screen.getByRole('button', { name: 'Оновити сповіщення' })); await screen.findByText(/synthetic-inbox-outage/);
    expect(screen.getByRole('link', { name: 'Перевірити API' })).toBeInTheDocument(); expect(screen.queryByRole('heading', { name: 'Сповіщень поки немає' })).not.toBeInTheDocument();
    loadOverride = undefined; await userEvent.click(screen.getByRole('button', { name: 'Повторити' })); await ready();
  });

  it('provides the real authenticated inbox route and navigation', async () => {
    render(<MemoryRouter initialEntries={['/notifications']}><SessionContext.Provider value={session()}><App /></SessionContext.Provider></MemoryRouter>);
    expect(await screen.findByRole('heading', { name: 'Сповіщення' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Сповіщення/ })).toHaveAttribute('href', '/notifications');
    await ready();
  });
});
