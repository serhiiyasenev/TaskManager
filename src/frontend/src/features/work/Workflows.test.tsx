import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';
import { ApiError, request } from '../../lib/api';
import { TaskDetailPage, TaskFormPage, TasksPage } from './TasksPages';
import { ProjectDetailPage, ProjectFormPage, ProjectsPage } from './ProjectsPages';
import { DashboardPage } from './DashboardPage';
import { App } from '../../App';
import { NotificationInboxPageSchema } from '../../lib/generated/notifications';

vi.mock('../../lib/api', async original => ({ ...await original<typeof import('../../lib/api')>(), request: vi.fn() }));
// This workflow suite checks the actual App shell; its isolated DOM has no
// SignalR server. Protocol/reconnect behavior is covered by the inbox suites.
vi.mock('@microsoft/signalr', () => ({
  LogLevel: { None: 6 },
  HubConnectionBuilder: class {
    withUrl() { return this; }
    withAutomaticReconnect() { return this; }
    configureLogging() { return this; }
    build() { return { start: async () => {}, stop: async () => {}, on() {}, off() {}, onreconnecting() {}, onreconnected() {}, onclose() {} }; }
  },
}));
const member = { id: 1, userName: 'olena', email: 'olena@example.test', teamId: 1, firstName: 'Олена', lastName: 'Коваль', birthDay: '1990-01-01T00:00:00Z', registeredAt: '2026-10-01T00:00:00Z' };
const session = { user: { ...member, emailConfirmed: true, roles: [] }, status: 'authenticated', refresh: vi.fn(), login: vi.fn(), logout: vi.fn() };
vi.mock('../../lib/session', () => ({ useSession: () => session }));
const version = 'e6045143-63ab-4ebd-8ba7-0c949876bb42';
const task = { id: 101, projectId: 9, performerId: 1, name: 'Перевірити API оплати', description: 'Зіставити звіт і транзакції', state: 'ToDo', createdAt: '2026-10-01T09:00:00Z', finishedAt: null, dueDate: '2026-12-12T09:30:00Z', reminderEnabled: true, reminderOffsetMinutes: 60, escalationEnabled: false, escalationDelayMinutes: null, reminderSentAt: null, escalationSentAt: null, performer: member, version };
const team = { id: 1, name: 'Команда продукту', ownerId: 1, createdAt: task.createdAt, users: [member] };
const project = { id: 9, name: 'Платіжний сервіс', description: 'Надійні оплати', authorId: 1, teamId: 1, deadline: task.dueDate, createdAt: task.createdAt, team, tasks: [task] };
const page = { items: [task], page: 1, pageSize: 20, totalCount: 21 };
function screenFor(element: ReactNode, path: string, route = path.split('?')[0]) {
  return render(<MemoryRouter initialEntries={[path]}><Routes><Route path={route} element={element} /><Route path="*" element={<p>Збережено на сервері</p>} /></Routes></MemoryRouter>);
}
beforeEach(() => {
  vi.mocked(request).mockReset(); session.user = { ...member, emailConfirmed: true, roles: [] }; session.status = 'authenticated';
  vi.mocked(request).mockImplementation(async (path, _schema, options) => {
    if (options?.method === 'DELETE') return undefined;
    if (path.startsWith('/api/notifications?')) return NotificationInboxPageSchema.parse({ items: [], page: 1, pageSize: 20, totalCount: 0, unreadCount: 0 });
    if (path === '/api/projects') return options?.method ? project : [project];
    if (path.startsWith('/api/projects/')) return project;
    if (path === '/api/teams') return [team];
    if (path.startsWith('/api/teams/')) return team;
    if (path.startsWith('/api/tasks/page')) return { ...page, items: path.includes('state=InProgress') ? [] : [task], totalCount: path.includes('state=InProgress') ? 0 : page.totalCount };
    if (path.startsWith('/api/tasks')) return task;
    throw new Error('Unexpected test route');
  });
});
describe('task and project workflows', () => {
  it('filters and paginates real task requests while keeping the board selection', async () => {
    const user = userEvent.setup(); screenFor(<TasksPage />, '/tasks');
    expect(await screen.findByRole('link', { name: task.name })).toBeInTheDocument();
    await user.type(screen.getByLabelText('Пошук'), 'оплати');
    await user.click(screen.getByRole('button', { name: 'Показати' }));
    await waitFor(() => expect(vi.mocked(request).mock.calls.some(([path]) => path.includes('search=') && path.includes('page=1'))).toBe(true));
    await user.click(screen.getByRole('button', { name: 'Дошка' }));
    expect(await screen.findByLabelText(`Статус: ${task.name}`)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Далі' }));
    await waitFor(() => expect(vi.mocked(request).mock.calls.some(([path]) => path.includes('page=2'))).toBe(true));
  });
  it('changes board status with the server version', async () => {
    const user = userEvent.setup(); screenFor(<TasksPage />, '/tasks?view=board');
    await user.selectOptions(await screen.findByLabelText(`Статус: ${task.name}`), 'InProgress');
    await waitFor(() => expect(request).toHaveBeenCalledWith('/api/tasks/101/state', expect.anything(), expect.objectContaining({ method: 'PATCH', body: { state: 'InProgress', expectedVersion: version } })));
  });
  it('filters by a permitted team member and keeps that selection across paging', async () => {
    const user = userEvent.setup(); screenFor(<TasksPage />, '/tasks');
    await user.selectOptions(await screen.findByLabelText('Виконавець'), '1');
    await user.click(screen.getByRole('button', { name: 'Показати' }));
    await waitFor(() => expect(vi.mocked(request).mock.calls.some(([path]) => path.includes('performerId=1') && path.includes('page=1'))).toBe(true));
    await user.click(screen.getByRole('button', { name: 'Далі' }));
    await waitFor(() => expect(vi.mocked(request).mock.calls.some(([path]) => path.includes('performerId=1') && path.includes('page=2'))).toBe(true));
  });
  it('shows a useful empty filtered list and resets its filters', async () => {
    vi.mocked(request).mockImplementation(async path => path.startsWith('/api/tasks/page') ? { ...page, items: [], totalCount: 0 } : path.startsWith('/api/teams/') ? team : [project]);
    const user = userEvent.setup(); screenFor(<TasksPage />, '/tasks?search=missing');
    expect(await screen.findByText('Завдань не знайдено')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Очистити фільтри' }));
    expect(screen.getByLabelText('Пошук')).toHaveValue('');
  });
  it('creates a task with current team assignee and explicit reminder settings', async () => {
    const user = userEvent.setup(); screenFor(<TaskFormPage />, '/tasks/new');
    await user.type(await screen.findByLabelText('Назва'), 'Підготувати міграцію');
    await user.type(screen.getByLabelText('Строк виконання'), '2026-12-13T10:00');
    await user.click(screen.getByLabelText('Нагадати до строку'));
    await user.click(screen.getByRole('button', { name: 'Створити завдання' }));
    await waitFor(() => expect(request).toHaveBeenCalledWith('/api/tasks', expect.anything(), expect.objectContaining({ method: 'POST', body: expect.objectContaining({ name: 'Підготувати міграцію', performerId: 1, projectId: 9, reminderEnabled: true, reminderOffsetMinutes: 60 }) })));
    expect(await screen.findByText('Збережено на сервері')).toBeInTheDocument();
  });
  it('preserves edited task fields after a conflict and explicitly loads the current record', async () => {
    const user = userEvent.setup();
    const baseline = vi.mocked(request).getMockImplementation()!;
    let read = 0;
    vi.mocked(request).mockImplementation(async (path, schema, options) => {
      if (path === '/api/tasks/101' && options?.method === 'PUT') throw new ApiError(409);
      if (path === '/api/tasks/101') return ++read === 1 ? task : { ...task, name: 'Актуальна серверна назва', description: 'Опис, який змінив колега', reminderOffsetMinutes: 120, escalationEnabled: true, escalationDelayMinutes: 90, version: '26b7d75f-9863-4af8-b2bd-1a3d7f842c21' };
      return baseline(path, schema, options);
    });
    screenFor(<TaskFormPage />, '/tasks/101/edit', '/tasks/:id/edit');
    const name = await screen.findByLabelText('Назва'); await user.clear(name); await user.type(name, 'Мої незбережені зміни');
    await user.click(screen.getByRole('button', { name: 'Зберегти зміни' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Дані або права змінилися');
    expect(name).toHaveValue('Мої незбережені зміни');
    await user.click(screen.getByRole('button', { name: 'Завантажити поточну версію' }));
    expect(await screen.findByText('Актуальна серверна назва')).toBeInTheDocument();
    expect(screen.getByText('Опис, який змінив колега')).toBeInTheDocument();
    expect(screen.getByText('За 120 хв до строку')).toBeInTheDocument();
    expect(screen.getByText('Через 90 хв після строку')).toBeInTheDocument();
    expect(name).toHaveValue('Мої незбережені зміни');
  });
  it('confirms task deletion and sends a strong version header', async () => {
    const user = userEvent.setup(); vi.spyOn(window, 'confirm').mockReturnValue(true);
    screenFor(<TaskDetailPage />, '/tasks/101', '/tasks/:id');
    await screen.findByRole('heading', { name: task.name });
    await user.click(screen.getByRole('button', { name: 'Видалити' }));
    await waitFor(() => expect(request).toHaveBeenCalledWith('/api/tasks/101', undefined, { method: 'DELETE', headers: { 'If-Match': `"${version}"` } }));
  });
  it('creates a project without accepting an author from the form', async () => {
    const user = userEvent.setup(); screenFor(<ProjectFormPage />, '/projects/new');
    await user.type(await screen.findByLabelText('Назва'), 'Новий реліз');
    await user.type(screen.getByLabelText('Строк завершення'), '2026-12-13T10:00');
    await user.click(screen.getByRole('button', { name: 'Створити проєкт' }));
    await waitFor(() => expect(request).toHaveBeenCalledWith('/api/projects', expect.anything(), expect.objectContaining({ method: 'POST', body: expect.objectContaining({ name: 'Новий реліз', teamId: 1 }) })));
    const write = vi.mocked(request).mock.calls.find(([, , options]) => options?.method === 'POST')![2]!.body as object;
    expect(write).not.toHaveProperty('authorId');
  });
  it('lists projects and opens accessible project members and tasks', async () => {
    screenFor(<ProjectsPage />, '/projects');
    expect(await screen.findByRole('link', { name: project.name })).toBeInTheDocument();
  });
  it('hides project management from an ordinary member who is not its author', async () => {
    session.user = { ...session.user, id: 2 };
    screenFor(<ProjectDetailPage />, '/projects/9', '/projects/:id');
    await screen.findByRole('heading', { name: project.name });
    expect(screen.queryByRole('button', { name: 'Видалити' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Редагувати' })).not.toBeInTheDocument();
    expect(screen.getByText(member.email)).toBeInTheDocument();
  });
  it('keeps project information when the dashboard task section fails', async () => {
    const baseline = vi.mocked(request).getMockImplementation()!;
    vi.mocked(request).mockImplementation(async (path, schema, options) => { if (path.startsWith('/api/tasks')) throw new ApiError(503); return baseline(path, schema, options); });
    screenFor(<DashboardPage />, '/');
    expect(await screen.findByRole('link', { name: project.name })).toBeInTheDocument();
    expect(await screen.findByRole('alert')).toHaveTextContent('тимчасово недоступний');
  });
  it('renders the real workspace navigation and profile after a confirmed session', async () => {
    render(<MemoryRouter initialEntries={['/']}><App /></MemoryRouter>);
    expect(screen.getByRole('navigation', { name: 'Головна навігація' })).toBeInTheDocument();
    expect(await screen.findByRole('heading', { name: 'Вітаємо, Олена' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Олена Коваль/ })).toHaveAttribute('href', '/profile');
  });
});
