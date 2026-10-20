import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { TaskRecord } from '../../lib/contracts';
import { setLocale } from '../../lib/i18n';
import { TasksPage } from './TasksPages';

const actor = { id: 1, userName: 'owner', email: 'owner@example.test', firstName: 'Тестовий', lastName: 'Адмін', teamId: 1 };
vi.mock('../../lib/session', () => ({ useSession: () => ({ user: { ...actor, emailConfirmed: true, roles: ['admin'] }, status: 'authenticated' }) }));
const version = 'e6045143-63ab-4ebd-8ba7-0c949876bb42';
const nextVersion = '26b7d75f-9863-4af8-b2bd-1a3d7f842c21';
const project = { id: 9, name: 'Тестовий проєкт', description: '', authorId: 1, teamId: 1,
  createdAt: '2026-10-01T09:00:00Z', deadline: '2030-10-01T09:00:00Z' };
const team = { id: 1, name: 'Тестова команда', ownerId: 1, createdAt: project.createdAt, users: [actor] };
const seed: TaskRecord[] = [17, 18, 19].map(id => ({ id, projectId: 9, performerId: id === 17 ? 99 : 1,
  name: `Synthetic task ${id}`, description: 'Existing description', state: 'ToDo', createdAt: project.createdAt,
  dueDate: null, finishedAt: null, reminderEnabled: false, reminderOffsetMinutes: null, reminderSentAt: null,
  escalationEnabled: false, escalationDelayMinutes: null, escalationSentAt: null, version }));
interface Write { path: string; body: Record<string, unknown>; init: RequestInit }
let records: TaskRecord[];
let writes: Write[];
let failure: number | undefined;
const held = new Set<number>();
const complete = new Map<number, () => void>();
function json(value: unknown, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json', 'X-Correlation-ID': 'synthetic-board-reference' } });
}
function page() { return render(<MemoryRouter initialEntries={['/tasks?view=board']}><TasksPage /></MemoryRouter>); }

beforeEach(() => {
  records = seed.map(task => ({ ...task })); writes = []; failure = undefined; held.clear(); complete.clear();
  vi.stubGlobal('fetch', vi.fn<typeof fetch>(async (input, init = {}) => {
    const path = String(input).split('?')[0];
    if (path === '/api/auth/csrf') return json({ requestToken: 'synthetic-csrf-token' });
    if (path === '/api/tasks/page') return json({ items: records, page: 1, pageSize: 20, totalCount: records.length });
    if (path === '/api/projects') return json([project]);
    if (path === '/api/teams/1') return json(team);
    const match = path.match(/^\/api\/tasks\/(\d+)(\/state)?$/);
    if (!match || !['PUT', 'PATCH'].includes(init.method ?? '')) throw new Error('Unexpected synthetic board HTTP route');
    const id = Number(match[1]); const task = records.find(value => value.id === id)!;
    const body = JSON.parse(String(init.body)) as Record<string, unknown>;
    writes.push({ path, body, init });
    // The real full-write service rejects an unchanged legacy performer who
    // no longer belongs to the project's team. A state-only command does not
    // reassign this person or resubmit unrelated task fields.
    if (init.method === 'PUT' && task.performerId === 99)
      return json({ status: 400, code: 'Error.Validation.PerformerId' }, 400);
    if (init.method === 'PATCH' && (match[2] !== '/state' || Object.keys(body).sort().join() !== 'expectedVersion,state'))
      return json({ status: 400, code: 'Error.Validation' }, 400);
    if (failure) return json({ status: failure, code: 'Error.Conflict' }, failure);
    if (body.expectedVersion !== task.version) return json({ status: 409, code: 'Error.Conflict' }, 409);
    const commit = () => {
      const saved = { ...task, state: body.state as TaskRecord['state'], version: nextVersion };
      records = records.map(value => value.id === id ? saved : value);
      return json(saved);
    };
    if (held.has(id)) return new Promise<Response>(resolve => complete.set(id, () => resolve(commit())));
    return commit();
  }));
});

describe('board status command', () => {
  it.each([{ locale: 'uk' as const, prefix: 'Статус:' }, { locale: 'en' as const, prefix: 'Status:' }])('changes an existing legacy assignment in $locale without resubmitting it', async ({ locale, prefix }) => {
    setLocale(locale); page(); const user = userEvent.setup();
    await user.selectOptions(await screen.findByLabelText(`${prefix} ${seed[0].name}`), 'InProgress');
    await waitFor(() => expect(screen.getByLabelText(`${prefix} ${seed[0].name}`)).toHaveValue('InProgress'));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(writes).toHaveLength(1);
    expect(writes[0].path).toBe('/api/tasks/17/state');
    expect(writes[0].init.method).toBe('PATCH');
    expect(writes[0].body).toEqual({ state: 'InProgress', expectedVersion: version });
    expect(new Headers(writes[0].init.headers).get('X-CSRF-TOKEN')).toBe('synthetic-csrf-token');
    expect(writes[0].init.credentials).toBe('same-origin');
    expect(records[0].performerId).toBe(99); expect(records[0].projectId).toBe(9);
    expect(records[0].description).toBe(seed[0].description);
  });

  it('keeps the other card pending when the first independently queued change completes', async () => {
    held.add(18); held.add(19); page(); const user = userEvent.setup();
    const first = await screen.findByLabelText(`Статус: ${seed[1].name}`);
    const second = screen.getByLabelText(`Статус: ${seed[2].name}`);
    await user.selectOptions(first, 'InProgress'); await user.selectOptions(second, 'Done');
    expect(first).toBeDisabled(); expect(second).toBeDisabled();
    await waitFor(() => expect(complete.size).toBe(2)); complete.get(18)!();
    await waitFor(() => expect(screen.getByLabelText(`Статус: ${seed[1].name}`)).toHaveValue('InProgress'));
    expect(screen.getByLabelText(`Статус: ${seed[2].name}`)).toBeDisabled();
    await user.selectOptions(screen.getByLabelText(`Статус: ${seed[2].name}`), 'Canceled');
    expect(writes.filter(write => write.path.includes('/19'))).toHaveLength(1);
    complete.get(19)!();
    await waitFor(() => expect(screen.getByLabelText(`Статус: ${seed[2].name}`)).toHaveValue('Done'));
  });

  it.each([400, 403, 409, 428])('retains original cards and states on HTTP %s', async status => {
    failure = status; page(); const user = userEvent.setup();
    await user.selectOptions(await screen.findByLabelText(`Статус: ${seed[1].name}`), 'Done');
    expect(await screen.findByRole('alert')).toHaveTextContent('synthetic-board-reference');
    expect(screen.getByRole('link', { name: seed[1].name })).toBeInTheDocument();
    expect(screen.getByLabelText(`Статус: ${seed[1].name}`)).toHaveValue('ToDo');
    expect(records[1].state).toBe('ToDo'); expect(writes).toHaveLength(1);
  });

  it('reloads a conflict before another explicit change and sends the refreshed version', async () => {
    failure = 409; page(); const user = userEvent.setup();
    await user.selectOptions(await screen.findByLabelText(`Статус: ${seed[1].name}`), 'Done');
    await screen.findByRole('alert'); records = records.map(task => task.id === 18 ? { ...task, version: nextVersion } : task);
    failure = undefined; await user.click(screen.getByRole('button', { name: 'Повторити' }));
    await user.selectOptions(await screen.findByLabelText(`Статус: ${seed[1].name}`), 'Done');
    await waitFor(() => expect(screen.getByLabelText(`Статус: ${seed[1].name}`)).toHaveValue('Done'));
    expect(writes[1].body).toEqual({ state: 'Done', expectedVersion: nextVersion });
  });

  it('does not send a write when the chosen state is already current', async () => {
    page(); const user = userEvent.setup();
    await user.selectOptions(await screen.findByLabelText(`Статус: ${seed[1].name}`), 'ToDo');
    expect(writes).toHaveLength(0);
  });

  it('makes the horizontally scrolling board a named keyboard-accessible region', async () => {
    page(); await screen.findByRole('link', { name: seed[0].name });
    const board = screen.getByRole('region', { name: 'Дошка завдань поточної сторінки' });
    expect(board).toHaveAttribute('tabindex', '0');
    expect(within(board).getAllByRole('combobox')).toHaveLength(3);
  });

  it.each([{ locale: 'uk' as const, prefix: 'Перетягнути:' }, { locale: 'en' as const, prefix: 'Move:' }])('moves a card with the keyboard in $locale and retains its concurrency token', async ({ locale, prefix }) => {
    setLocale(locale); page(); const user = userEvent.setup();
    const handle = await screen.findByRole('button', { name: `${prefix} ${seed[0].name}` });
    handle.focus(); await user.keyboard(' {ArrowRight}{Enter}');
    await waitFor(() => expect(records[0].state).toBe('InProgress'));
    const moved = screen.getByRole('link', { name: seed[0].name }).closest('[data-task-id]')!;
    expect(moved.closest('[data-state]')).toHaveAttribute('data-state', 'InProgress');
    expect(writes).toHaveLength(1);
    expect(writes[0].body).toEqual({ state: 'InProgress', expectedVersion: version });
    expect(writes[0].init.method).toBe('PATCH');
    expect(screen.getByRole('button', { name: `${prefix} ${seed[0].name}` })).toHaveFocus();
  });

  it('cancels keyboard dragging and ignores a drop in the current column', async () => {
    page(); const user = userEvent.setup();
    const handle = await screen.findByRole('button', { name: `Перетягнути: ${seed[0].name}` });
    handle.focus(); await user.keyboard(' {ArrowRight}{Escape}');
    expect(records[0].state).toBe('ToDo'); expect(writes).toHaveLength(0);
    await user.keyboard(' {Enter}');
    expect(records[0].state).toBe('ToDo'); expect(writes).toHaveLength(0);
  });

  it('retains the source card after a rejected keyboard drop', async () => {
    failure = 409; page(); const user = userEvent.setup();
    const handle = await screen.findByRole('button', { name: `Перетягнути: ${seed[0].name}` });
    handle.focus(); await user.keyboard(' {ArrowRight}{Enter}');
    expect(await screen.findByRole('alert')).toHaveTextContent('synthetic-board-reference');
    expect(records[0].state).toBe('ToDo');
    expect(screen.getByRole('link', { name: seed[0].name }).closest('[data-state]')).toHaveAttribute('data-state', 'ToDo');
    expect(screen.getByRole('button', { name: `Перетягнути: ${seed[0].name}` })).toBeEnabled();
    expect(writes).toHaveLength(1);
  });

  it('blocks duplicate keyboard drops and dropdown edits while a card save is pending', async () => {
    held.add(17); page(); const user = userEvent.setup();
    const name = `Перетягнути: ${seed[0].name}`;
    const handle = await screen.findByRole('button', { name });
    handle.focus(); await user.keyboard(' {ArrowRight}{Enter}');
    await waitFor(() => expect(complete.has(17)).toBe(true));
    expect(screen.getByRole('button', { name })).toBeDisabled();
    expect(screen.getByLabelText(`Статус: ${seed[0].name}`)).toBeDisabled();
    await user.keyboard(' {ArrowRight}{Enter}');
    expect(writes).toHaveLength(1);
    complete.get(17)!();
    await waitFor(() => expect(screen.getByRole('button', { name })).toBeEnabled());
    expect(records[0].state).toBe('InProgress'); expect(writes).toHaveLength(1);
  });
});
