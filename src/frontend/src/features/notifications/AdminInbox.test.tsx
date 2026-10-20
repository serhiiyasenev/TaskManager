import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { SessionProfile } from '../../lib/contracts';
import { NotificationAdminInboxItemSchema, NotificationAdminInboxPageSchema, NotificationInboxPageSchema } from '../../lib/generated/notifications';
import type { NotificationAdminInboxPage } from '../../lib/generated/notifications';
import { setLocale } from '../../lib/i18n';
import { SessionContext } from '../../lib/session';
import { NotificationsContext } from './context';
import { InboxPage, NotificationSummary } from './InboxPage';

const boundary = vi.hoisted(() => {
  class ApiError extends Error {
    constructor(public status: number, public traceId?: string) { super('Synthetic safe error'); }
  }
  return { request: vi.fn(), refresh: vi.fn(), reload: vi.fn(), markRead: vi.fn(), ApiError };
});
vi.mock('../../lib/api', () => ({ request: boundary.request, ApiError: boundary.ApiError }));

const admin: SessionProfile = { id: 42, userName: 'admin', firstName: 'System', lastName: 'Admin', email: 'admin@example.test', emailConfirmed: true, teamId: 17, roles: ['Admin'] };
function row(index = 1, recipientId = 43, recipientName = 'Kate A') {
  const id = `00000000-0000-4000-8000-${index.toString(16).padStart(12, '0')}`;
  return NotificationAdminInboxItemSchema.parse({ id, messageId: id, kind: 1, taskId: 100 + index, projectId: 71,
    taskName: `Нагадування ${index}`, projectName: 'API project', occurredAtUtc: '2026-10-20T10:30:00Z', recipientId, recipientName });
}
function page(items = [row()], totalCount = items.length, unreadCount = totalCount, number = 1) {
  return NotificationAdminInboxPageSchema.parse({ items, page: number, pageSize: 20, totalCount, unreadCount });
}
const personal = {
  data: NotificationInboxPageSchema.parse({ items: [], page: 1, pageSize: 20, totalCount: 9, unreadCount: 7 }),
  loading: false, error: null, connection: 'connected' as const, page: 1, unreadOnly: false,
  pendingRead: new Set<string>(), setPage: vi.fn(), setUnreadOnly: vi.fn(), reload: boundary.reload,
  reconnect: vi.fn(), markRead: boundary.markRead,
};
function shell(user: SessionProfile | null = admin) {
  return <MemoryRouter><SessionContext.Provider value={{ user, status: user ? 'authenticated' : 'anonymous', refresh: boundary.refresh, login: vi.fn(), logout: vi.fn() }}>
    <NotificationsContext.Provider value={personal}><InboxPage /><NotificationSummary /></NotificationsContext.Provider>
  </SessionContext.Provider></MemoryRouter>;
}
let load: (query: URLSearchParams, signal?: AbortSignal) => Promise<NotificationAdminInboxPage>;
beforeEach(() => {
  vi.clearAllMocks(); boundary.refresh.mockResolvedValue(undefined); boundary.reload.mockResolvedValue(undefined);
  load = async () => page();
  boundary.request.mockImplementation(async (path: string, schema: { parse(value: unknown): unknown }, options?: { signal?: AbortSignal }) => {
    if (!path.startsWith('/api/notifications/all?')) throw new Error(`Unexpected synthetic route: ${path}`);
    return schema.parse(await load(new URL(path, 'https://fixture.test').searchParams, options?.signal));
  });
});
async function overview() {
  await userEvent.click(screen.getByRole('button', { name: 'Усі сповіщення' }));
  await screen.findByRole('link', { name: 'Нагадування 1' });
}
function deferred<T>() { let resolve!: (value: T) => void; return { promise: new Promise<T>(done => { resolve = done; }), resolve: (value: T) => resolve(value) }; }

describe('separate read-only admin notification overview', () => {
  it('keeps personal mode as the default and never requests admin data for ordinary users', async () => {
    const view = render(shell({ ...admin, roles: [] }));
    expect(screen.queryByRole('button', { name: 'Усі сповіщення' })).not.toBeInTheDocument();
    expect(boundary.request).not.toHaveBeenCalled();
    view.rerender(shell());
    expect(screen.getByRole('button', { name: 'Мої сповіщення' })).toHaveAttribute('aria-pressed', 'true');
    expect(boundary.request).not.toHaveBeenCalled();
  });

  it('shows cross-user recipients and their read status without any read actions or personal count changes', async () => {
    load = async () => page([row(), { ...row(2, 42, 'System Admin'), readAtUtc: '2026-10-20T10:31:00Z' }], 28, 21);
    render(shell()); await overview();
    expect(screen.getByRole('button', { name: 'Усі сповіщення' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByText(/Kate A/)).toBeInTheDocument(); expect(screen.getByText(/System Admin/)).toBeInTheDocument();
    expect(screen.getByText('Прочитано', { exact: true })).toBeInTheDocument(); expect(screen.getByText('Непрочитане', { exact: true })).toBeInTheDocument();
    expect(screen.getByText('Лише перегляд. Статус прочитання належить отримувачу. Щоб побачити нові події, оновіть сповіщення.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Позначити прочитаним/ })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Нагадування 1' })).toHaveAttribute('href', '/tasks/101');
    const summary = screen.getByText('Непрочитані сповіщення').closest('.stat') as HTMLElement;
    expect(within(summary).getByText('7', { exact: true })).toBeInTheDocument();
    expect(boundary.markRead).not.toHaveBeenCalled(); expect(boundary.reload).not.toHaveBeenCalled();
    expect(boundary.request.mock.calls[0][0]).toBe('/api/notifications/all?page=1&pageSize=20&unreadOnly=false');
    await userEvent.click(screen.getByRole('button', { name: 'Мої сповіщення' }));
    expect(screen.queryByText(/Kate A/)).not.toBeInTheDocument();
    expect(screen.getByText('Усього доступних: 9 · Непрочитаних: 7')).toBeInTheDocument();
  });

  it('uses global unread counts for pagination and resets the page when the unread filter changes', async () => {
    load = async query => page([row(Number(query.get('page')))], 61, 21, Number(query.get('page')));
    render(shell()); await overview();
    await userEvent.click(screen.getByRole('button', { name: 'Далі' })); await screen.findByRole('link', { name: 'Нагадування 2' });
    await userEvent.click(screen.getByLabelText('Лише непрочитані'));
    await screen.findByRole('link', { name: 'Нагадування 1' });
    expect(boundary.request.mock.calls.at(-1)?.[0]).toBe('/api/notifications/all?page=1&pageSize=20&unreadOnly=true');
    expect(screen.getByText(/Сторінка 1 з 2/)).toBeInTheDocument();
  });

  it('clamps an emptied final page after refresh rather than presenting a false empty inbox', async () => {
    let shrunk = false;
    load = async query => {
      const number = Number(query.get('page'));
      return page(shrunk && number === 2 ? [] : [row(number)], shrunk ? 20 : 21, shrunk ? 20 : 21, number);
    };
    render(shell()); await overview(); await userEvent.click(screen.getByRole('button', { name: 'Далі' }));
    await screen.findByRole('link', { name: 'Нагадування 2' }); shrunk = true;
    await userEvent.click(screen.getByRole('button', { name: 'Оновити сповіщення' }));
    await screen.findByRole('link', { name: 'Нагадування 1' });
    expect(screen.getByText(/Сторінка 1 з 1/)).toBeInTheDocument();
  });

  it.each([401, 403])('clears the overview and refreshes identity after HTTP %i', async status => {
    render(shell()); await overview();
    load = async () => { throw new boundary.ApiError(status, 'synthetic-admin-denied'); };
    await userEvent.click(screen.getByRole('button', { name: 'Оновити сповіщення' }));
    await screen.findByText(/synthetic-admin-denied/);
    expect(screen.queryByRole('link', { name: 'Нагадування 1' })).not.toBeInTheDocument();
    expect(boundary.refresh).toHaveBeenCalledTimes(1);
  });

  it('labels cached data during an outage and withholds current counts until retry succeeds', async () => {
    render(shell()); await overview();
    load = async () => { throw new boundary.ApiError(503, 'synthetic-admin-outage'); };
    await userEvent.click(screen.getByRole('button', { name: 'Оновити сповіщення' }));
    await screen.findByText(/synthetic-admin-outage/);
    expect(screen.getByRole('link', { name: 'Нагадування 1' })).toBeInTheDocument();
    expect(screen.getByText(/Показано раніше завантажені сповіщення/)).toBeInTheDocument();
    expect(screen.getByText(/: — · .*: —/)).toBeInTheDocument();
    load = async () => page(); await userEvent.click(screen.getByRole('button', { name: 'Повторити' }));
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
  });

  it('never resurrects access-denied rows while retrying or when a retry fails with a server outage', async () => {
    render(shell()); await overview();
    load = async () => { throw new boundary.ApiError(403, 'synthetic-admin-revoked'); };
    await userEvent.click(screen.getByRole('button', { name: 'Оновити сповіщення' }));
    await screen.findByText(/synthetic-admin-revoked/);
    const retry = deferred<NotificationAdminInboxPage>(); load = async () => retry.promise;
    await userEvent.click(screen.getByRole('button', { name: 'Повторити' }));
    expect(screen.queryByRole('link', { name: 'Нагадування 1' })).not.toBeInTheDocument();
    await act(async () => { retry.resolve(page([])); });
    load = async () => { throw new boundary.ApiError(503, 'synthetic-admin-later-outage'); };
    await userEvent.click(screen.getByRole('button', { name: 'Оновити сповіщення' }));
    await screen.findByText(/synthetic-admin-later-outage/);
    expect(screen.queryByRole('link', { name: 'Нагадування 1' })).not.toBeInTheDocument();
  });

  it('keeps denied data cleared after a failed retry and recovers only with a newly successful response', async () => {
    render(shell()); await overview();
    load = async () => { throw new boundary.ApiError(403, 'synthetic-admin-denied-retry'); };
    await userEvent.click(screen.getByRole('button', { name: 'Оновити сповіщення' }));
    await screen.findByText(/synthetic-admin-denied-retry/);
    load = async () => { throw new boundary.ApiError(503, 'synthetic-admin-retry-outage'); };
    await userEvent.click(screen.getByRole('button', { name: 'Повторити' }));
    await screen.findByText(/synthetic-admin-retry-outage/);
    expect(screen.queryByRole('link', { name: 'Нагадування 1' })).not.toBeInTheDocument();
    load = async () => page([row(2)]);
    await userEvent.click(screen.getByRole('button', { name: 'Повторити' }));
    await screen.findByRole('link', { name: 'Нагадування 2' });
    expect(screen.queryByRole('link', { name: 'Нагадування 1' })).not.toBeInTheDocument();
  });

  it('discards a response arriving after switching back to the personal inbox', async () => {
    const late = deferred<NotificationAdminInboxPage>(); let signal: AbortSignal | undefined;
    load = async (_query, current) => { signal = current; return late.promise; };
    render(shell()); await userEvent.click(screen.getByRole('button', { name: 'Усі сповіщення' }));
    await userEvent.click(screen.getByRole('button', { name: 'Мої сповіщення' }));
    expect(signal?.aborted).toBe(true);
    await act(async () => { late.resolve(page()); });
    expect(screen.queryByRole('link', { name: 'Нагадування 1' })).not.toBeInTheDocument();
  });

  it.each([
    { ...admin, id: 44, userName: 'another-admin' },
    { ...admin, teamId: 18 },
    { ...admin, roles: [] },
  ])('does not reveal a former scope response after account/team/role changes: %j', async changed => {
    const view = render(shell()); await overview();
    const late = deferred<NotificationAdminInboxPage>(); load = async () => late.promise;
    await userEvent.click(screen.getByRole('button', { name: 'Оновити сповіщення' }));
    view.rerender(shell(changed));
    expect(screen.queryByRole('link', { name: 'Нагадування 1' })).not.toBeInTheDocument();
    await act(async () => { late.resolve(page([row(3)])); });
    expect(screen.queryByRole('link', { name: 'Нагадування 3' })).not.toBeInTheDocument();
    if (!changed.roles.length) expect(screen.queryByRole('button', { name: 'Усі сповіщення' })).not.toBeInTheDocument();
  });

  it('translates mode labels and status without altering recipient or task content', async () => {
    render(shell()); await overview(); await act(async () => { setLocale('en'); });
    expect(screen.getByRole('heading', { name: 'All notifications' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'My notifications' })).toHaveAttribute('aria-pressed', 'false');
    expect(screen.getByRole('button', { name: 'All notifications' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'Refresh notifications' })).toBeInTheDocument();
    expect(screen.getByText(/Kate A/)).toBeInTheDocument(); expect(screen.getByRole('link', { name: 'Нагадування 1' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Mark as read/ })).not.toBeInTheDocument();
  });
});
