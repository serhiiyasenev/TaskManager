import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, request } from '../../lib/api';
import { setLocale } from '../../lib/i18n';
import { DashboardPage } from './DashboardPage';
import { TaskFormPage, TasksPage } from './TasksPages';
import { ProjectDetailPage, ProjectFormPage, ProjectsPage } from './ProjectsPages';
import { UsersPage } from '../admin/UsersPage';
import { workCopy } from './workCopy';

vi.mock('../../lib/api', async original => ({ ...await original<typeof import('../../lib/api')>(), request: vi.fn() }));
vi.mock('../../lib/session', () => ({ useSession: () => session }));
const member = { id: 11, userName: 'owner', email: 'owner@example.test', teamId: 7,
  firstName: 'Тестовий', lastName: 'Власник', emailConfirmed: true, roles: [] as string[] };
const session = { user: member, status: 'authenticated', refresh: vi.fn(), login: vi.fn(), logout: vi.fn() };
const task = { id: 101, projectId: 9, performerId: 11, name: 'Перевірити український звіт', description: 'Зміст користувача без перекладу',
  state: 'ToDo', createdAt: '2026-10-12T09:00:00Z', dueDate: '2030-10-14T09:00:00Z', reminderEnabled: true,
  reminderOffsetMinutes: 30, escalationEnabled: false, version: 'e6045143-63ab-4ebd-8ba7-0c949876bb42', performer: member };
const team = { id: 7, name: 'Українська команда', ownerId: 11, createdAt: task.createdAt, users: [member] };
const project = { id: 9, authorId: 11, teamId: 7, name: 'Український проєкт', description: 'Користувацька мета',
  deadline: task.dueDate, createdAt: task.createdAt, team, tasks: [task] };
function page(element: ReactNode, path = '/', route = path.split('?')[0]) {
  return render(<MemoryRouter initialEntries={[path]}><Routes><Route path={route} element={element} /></Routes></MemoryRouter>);
}
beforeEach(() => {
  setLocale('uk'); session.user = { ...member, roles: [] }; vi.mocked(request).mockReset();
  vi.mocked(request).mockImplementation(async (path, schema, options) => {
    let value: unknown;
    if (path === '/api/projects') value = options?.method ? project : [project];
    else if (path.startsWith('/api/projects/')) value = project;
    else if (path === '/api/teams') value = [team];
    else if (path.startsWith('/api/teams/')) value = team;
    else if (path === '/api/users') value = [member, { ...member, id: 12, teamId: null, userName: 'member', email: 'member@example.test' }];
    else if (path.startsWith('/api/tasks/page')) value = { page: 1, pageSize: 20, totalCount: 1,
      items: path.includes('state=InProgress') ? [] : [task] };
    else if (path.startsWith('/api/tasks')) value = task;
    else throw new Error('Unexpected synthetic work route.');
    return schema ? schema.parse(value) : value;
  });
});
afterEach(() => setLocale('uk'));

describe('work page language', () => {
  it('keeps Ukrainian and English copy keys and interpolation placeholders aligned', () => {
    expect(Object.keys(workCopy.en).sort()).toEqual(Object.keys(workCopy.uk).sort());
    for (const key of Object.keys(workCopy.uk) as Array<keyof typeof workCopy.uk>) {
      const placeholders = (value: string) => [...value.matchAll(/\{(\w+)\}/g)].map(match => match[1]).sort();
      expect(workCopy.en[key].trim(), key).not.toBe('');
      expect(placeholders(workCopy.en[key]), key).toEqual(placeholders(workCopy.uk[key]));
    }
  });
  it('renders the English admin dashboard without translating names or narrowing queries', async () => {
    setLocale('en'); session.user = { ...member, roles: ['admin'] }; page(<DashboardPage />);
    expect(await screen.findByRole('link', { name: task.name })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Upcoming system tasks' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Work overview' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Welcome, Тестовий' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'All teams' })).toHaveAttribute('href', '/teams');
    expect(screen.getByRole('link', { name: project.name })).toBeInTheDocument();
    expect(vi.mocked(request).mock.calls.filter(([path]) => path.startsWith('/api/tasks/page'))
      .every(([path]) => !new URL(path, 'https://unit.example.test').searchParams.has('performerId'))).toBe(true);
  });
  it('renders English task table labels, reminder copy and unchanged user content', async () => {
    setLocale('en'); page(<TasksPage />, '/tasks');
    const region = await screen.findByRole('region', { name: 'Task list' });
    expect(within(region).getByRole('link', { name: task.name })).toBeInTheDocument();
    expect(within(region).getByText('30 min before')).toBeInTheDocument();
    expect(within(region).getByText('To do')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Tasks' })).toBeInTheDocument();
    expect(screen.getByPlaceholderText('Name or description')).toBeInTheDocument();
  });
  it('renders English project detail headings while preserving project, member and task text', async () => {
    setLocale('en'); page(<ProjectDetailPage />, '/projects/9', '/projects/:id');
    expect(await screen.findByRole('heading', { name: project.name })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Project goal' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Team members' })).toBeInTheDocument();
    expect(screen.getByText(project.description)).toBeInTheDocument();
    expect(screen.getByText('To do')).toBeInTheDocument();
    expect(screen.getByText(member.email)).toBeInTheDocument();
  });
  it('localizes the administrator directory captions and leaves user identities untouched', async () => {
    setLocale('en'); session.user = { ...member, roles: ['admin'] }; page(<UsersPage />);
    const table = await screen.findByRole('table');
    expect(screen.getByRole('region', { name: 'System users' })).toBeInTheDocument();
    expect(within(table).getByRole('columnheader', { name: 'Username' })).toBeInTheDocument();
    expect(within(table).getByText('No team')).toBeInTheDocument();
    expect(within(table).getByText('member@example.test')).toBeInTheDocument();
    expect(screen.getByText('2 users')).toBeInTheDocument();
  });
  it('switches task form language without clearing edits, UTC input or generated validation', async () => {
    const user = userEvent.setup(); page(<TaskFormPage />, '/tasks/new');
    await user.type(await screen.findByLabelText('Назва'), 'Моя незбережена назва');
    await user.type(screen.getByLabelText('Опис'), 'Український опис');
    await user.type(screen.getByLabelText('Строк виконання'), '2030-10-14T10:30');
    await user.click(screen.getByLabelText('Нагадати до строку'));
    const reads = vi.mocked(request).mock.calls.length;
    act(() => setLocale('en'));
    expect(screen.getByRole('heading', { name: 'New task' })).toBeInTheDocument();
    expect(screen.getByLabelText('Name')).toHaveValue('Моя незбережена назва');
    expect(screen.getByLabelText('Description')).toHaveValue('Український опис');
    expect(screen.getByLabelText('Task deadline')).toHaveValue('2030-10-14T10:30');
    expect(screen.getByLabelText('Remind before the deadline')).toBeChecked();
    expect(vi.mocked(request).mock.calls).toHaveLength(reads);
    await user.clear(screen.getByLabelText('Task deadline'));
    await user.click(screen.getByRole('button', { name: 'Create task' }));
    expect(await screen.findByText('A deadline is required for reminders.')).toBeInTheDocument();
    act(() => setLocale('uk'));
    expect(screen.getByText('Для нагадування потрібен строк виконання.')).toBeInTheDocument();
    expect(screen.getByLabelText('Назва')).toHaveValue('Моя незбережена назва');
    expect(vi.mocked(request).mock.calls.every(([, , options]) => !options?.method)).toBe(true);
  });
  it('keeps entered task filters and board data when switching language', async () => {
    const user = userEvent.setup(); page(<TasksPage />, '/tasks?view=board');
    await screen.findByLabelText(`Статус: ${task.name}`);
    await user.type(screen.getByLabelText('Пошук'), 'український пошук');
    await user.selectOptions(screen.getByRole('combobox', { name: /^Статус(?!:)/ }), 'InProgress');
    const reads = vi.mocked(request).mock.calls.length;
    act(() => setLocale('en'));
    expect(screen.getByLabelText('Search')).toHaveValue('український пошук');
    expect(screen.getByRole('combobox', { name: /^Status(?!:)/ })).toHaveValue('InProgress');
    expect(screen.getByRole('button', { name: 'Board' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByLabelText(`Status: ${task.name}`)).toBeInTheDocument();
    expect(vi.mocked(request).mock.calls).toHaveLength(reads);
    await user.click(screen.getByRole('button', { name: 'Apply' }));
    await waitFor(() => expect(vi.mocked(request).mock.calls.some(([path]) => {
      const query = new URL(path, 'https://unit.example.test').searchParams;
      return query.get('search') === 'український пошук' && query.get('state') === 'InProgress';
    })).toBe(true));
  });
  it('localizes server validation fallback without displaying server text or discarding edits', async () => {
    const baseline = vi.mocked(request).getMockImplementation()!;
    vi.mocked(request).mockImplementation(async (path, schema, options) => {
      if (options?.method === 'POST') throw new ApiError(400, undefined, { Name: ['untrusted server validation sentinel'] });
      return baseline(path, schema, options);
    });
    setLocale('en'); const user = userEvent.setup(); page(<TaskFormPage />, '/tasks/new');
    await user.type(await screen.findByLabelText('Name'), 'Незбережена українська назва');
    await user.click(screen.getByRole('button', { name: 'Create task' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Check the entered information.');
    expect(screen.queryByText('untrusted server validation sentinel')).not.toBeInTheDocument();
    act(() => setLocale('uk'));
    expect(screen.getByRole('alert')).toHaveTextContent('Перевірте введені дані.');
    expect(screen.getByLabelText('Назва')).toHaveValue('Незбережена українська назва');
  });
  it('keeps project form edits and project search results when switching language', async () => {
    const user = userEvent.setup(); const form = page(<ProjectFormPage />, '/projects/new');
    await user.type(await screen.findByLabelText('Назва'), 'Мій український проєкт');
    await user.type(screen.getByLabelText('Опис'), 'Мета без перекладу');
    const reads = vi.mocked(request).mock.calls.length;
    act(() => setLocale('en'));
    expect(screen.getByRole('heading', { name: 'New project' })).toBeInTheDocument();
    expect(screen.getByLabelText('Name')).toHaveValue('Мій український проєкт');
    expect(screen.getByLabelText('Description')).toHaveValue('Мета без перекладу');
    expect(vi.mocked(request).mock.calls).toHaveLength(reads);
    form.unmount(); page(<ProjectsPage />, '/projects');
    await user.type(await screen.findByLabelText('Find a project'), 'Український');
    act(() => setLocale('uk'));
    expect(screen.getByLabelText('Знайти проєкт')).toHaveValue('Український');
    expect(screen.getByRole('link', { name: project.name })).toBeInTheDocument();
  });
});
