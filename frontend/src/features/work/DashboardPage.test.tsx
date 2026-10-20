import { render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from '../../lib/api';
import { SessionContext } from '../../lib/session';
import type { SessionProfile } from '../../lib/contracts';
import { DashboardPage } from './DashboardPage';

vi.mock('../../lib/api', async original => ({ ...await original<typeof import('../../lib/api')>(), request: vi.fn() }));

const member: SessionProfile = { id: 11, userName: 'demo', email: 'demo@example.test', firstName: 'Тестовий',
  lastName: 'Користувач', teamId: 7, emailConfirmed: true, roles: [] };
let currentUser = member;
const version = 'e6045143-63ab-4ebd-8ba7-0c949876bb42';
const myTask = { id: 101, projectId: 9, performerId: 11, name: 'Моя підготовка релізу', description: '', state: 'ToDo',
  createdAt: '2026-10-12T09:00:00Z', dueDate: '2030-10-14T09:00:00Z', reminderEnabled: false,
  escalationEnabled: false, version };
const otherTask = { ...myTask, id: 102, projectId: 10, performerId: 42, name: 'Реліз іншої команди' };
const progressTask = { ...otherTask, id: 103, name: 'Міграція іншої команди', state: 'InProgress', dueDate: '2030-10-13T09:00:00Z' };
const newTeamTask = { ...myTask, id: 106, projectId: 10, name: 'Моє завдання в новій команді' };
const projects = [9, 10, 11, 12].map(id => ({ id, authorId: id === 9 ? 11 : 42, teamId: id === 9 ? 7 : 8,
  name: `Проєкт ${id}`, description: '', deadline: '2030-10-20T09:00:00Z', createdAt: '2026-10-12T09:00:00Z' }));

function dashboard() {
  return <SessionContext.Provider value={{ user: currentUser, status: 'authenticated',
    refresh: async () => {}, login: async () => {}, logout: async () => {} }}>
    <MemoryRouter><DashboardPage /></MemoryRouter>
  </SessionContext.Provider>;
}
function taskQueries() {
  return vi.mocked(request).mock.calls.filter(([path]) => path.startsWith('/api/tasks/page'))
    .map(([path]) => new URL(path, 'https://unit.example.test').searchParams);
}
function count(label: string) {
  const overview = screen.getByRole('region', { name: 'Огляд роботи' });
  const card = within(overview).getByText(label, { exact: true }).closest('.stat');
  if (!card) throw new Error('The requested overview card was not rendered.');
  return card.querySelector('strong');
}

beforeEach(() => {
  currentUser = { ...member, roles: [] };
  vi.mocked(request).mockReset();
  vi.mocked(request).mockImplementation(async (path, schema) => {
    const url = new URL(path, 'https://unit.example.test');
    const admin = currentUser.roles.some(role => role.toLowerCase() === 'admin');
    let value: unknown;
    if (url.pathname === '/api/projects') value = admin ? projects
      : currentUser.teamId === null ? [] : currentUser.teamId === 8 ? projects.slice(1, 2) : projects.slice(0, 1);
    else if (url.pathname === '/api/tasks/page') {
      const personal = url.searchParams.get('performerId') === String(member.id);
      const progress = url.searchParams.get('state') === 'InProgress';
      const scopedTask = currentUser.teamId === 8 ? newTeamTask : myTask;
      value = { page: 1, pageSize: 5, totalCount: personal && currentUser.teamId === null ? 0 : personal ? (progress ? 2 : 1) : (progress ? 3 : 12),
        items: personal && currentUser.teamId === null ? [] : progress ? [personal ? {
          ...scopedTask, id: currentUser.teamId === 8 ? 107 : 104,
          name: currentUser.teamId === 8 ? 'Нова міграція' : 'Моя міграція', state: 'InProgress',
        } : progressTask] : [personal ? scopedTask : otherTask] };
    } else throw new Error('Unexpected dashboard request.');
    // Keep production schema validation on fixtures despite mocking transport.
    return schema ? schema.parse(value) : value;
  });
});

describe('dashboard access scope', () => {
  it('shows system-wide admin counters, other assignees and unfiltered overview links', async () => {
    currentUser = { ...member, teamId: null, roles: ['AdMiN'] };
    render(dashboard());
    await waitFor(() => expect(count('До виконання')).toHaveTextContent('12'));
    expect(count('У роботі')).toHaveTextContent('3');
    expect(count('Усі проєкти')).toHaveTextContent('4');
    expect(taskQueries()).toHaveLength(2);
    expect(taskQueries().every(query => !query.has('performerId'))).toBe(true);
    expect(taskQueries().map(query => query.get('state')).sort()).toEqual(['InProgress', 'ToDo']);
    expect(await screen.findByRole('link', { name: otherTask.name })).toHaveAttribute('href', '/tasks/102');
    expect(screen.getByRole('heading', { name: 'Найближчі завдання системи' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Проєкти системи' })).toBeInTheDocument();
    expect(screen.getByText('Адміністраторський огляд', { exact: true })).toBeInTheDocument();
    const allTasks = screen.getByRole('link', { name: 'Усі завдання →' });
    expect(new URL(allTasks.getAttribute('href')!, 'https://unit.example.test').searchParams.has('performerId')).toBe(false);
    expect(screen.getByRole('link', { name: 'Усі команди' })).toHaveAttribute('href', '/teams');
    expect(screen.getByRole('link', { name: 'Усі проєкти' })).toHaveAttribute('href', '/projects');
    expect(screen.queryByRole('heading', { name: 'Створіть свою команду' })).not.toBeInTheDocument();
  });

  it('retains ordinary member personal requests, counters, captions and links', async () => {
    render(dashboard());
    await waitFor(() => expect(count('До виконання')).toHaveTextContent('1'));
    expect(count('У роботі')).toHaveTextContent('2');
    expect(count('Доступні проєкти')).toHaveTextContent('1');
    expect(taskQueries()).toHaveLength(2);
    expect(taskQueries().every(query => query.get('performerId') === String(member.id))).toBe(true);
    expect(screen.getByRole('heading', { name: 'Мої найближчі завдання' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Проєкти команди' })).toBeInTheDocument();
    expect(screen.getByText('Ваш робочий день', { exact: true })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Усі завдання →' }))
      .toHaveAttribute('href', '/tasks?performerId=11&sort=due&direction=asc');
    expect(screen.queryByRole('link', { name: 'Усі команди' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: otherTask.name })).not.toBeInTheDocument();
  });

  it('refreshes both task and project scope when the same session gains the admin role', async () => {
    const view = render(dashboard());
    await waitFor(() => expect(count('До виконання')).toHaveTextContent('1'));
    expect(count('Доступні проєкти')).toHaveTextContent('1');
    const previousTaskReads = taskQueries().length;
    const previousProjectReads = vi.mocked(request).mock.calls.filter(([path]) => path === '/api/projects').length;
    currentUser = { ...member, roles: ['admin'] };
    view.rerender(dashboard());
    await waitFor(() => expect(taskQueries().length).toBe(previousTaskReads + 2));
    expect(vi.mocked(request).mock.calls.filter(([path]) => path === '/api/projects')).toHaveLength(previousProjectReads + 1);
    expect(taskQueries().slice(-2).every(query => !query.has('performerId'))).toBe(true);
    expect(count('До виконання')).toHaveTextContent('12');
    expect(count('Усі проєкти')).toHaveTextContent('4');
  });

  it('refetches current membership and removes old team rows after a move and revocation', async () => {
    const view = render(dashboard());
    expect(await screen.findByRole('link', { name: myTask.name })).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: projects[0].name })).toBeInTheDocument();
    const initialTaskReads = taskQueries().length;
    const initialProjectReads = vi.mocked(request).mock.calls.filter(([path]) => path === '/api/projects').length;

    currentUser = { ...member, teamId: 8 };
    view.rerender(dashboard());
    await waitFor(() => expect(taskQueries().length).toBe(initialTaskReads + 2));
    expect(await screen.findByRole('link', { name: newTeamTask.name })).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: projects[1].name })).toBeInTheDocument();
    expect(vi.mocked(request).mock.calls.filter(([path]) => path === '/api/projects')).toHaveLength(initialProjectReads + 1);
    expect(screen.queryByRole('link', { name: myTask.name })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Моя міграція' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: projects[0].name })).not.toBeInTheDocument();

    currentUser = { ...member, teamId: null };
    view.rerender(dashboard());
    await waitFor(() => expect(count('Доступні проєкти')).toHaveTextContent('0'));
    expect(taskQueries()).toHaveLength(initialTaskReads + 4);
    expect(vi.mocked(request).mock.calls.filter(([path]) => path === '/api/projects')).toHaveLength(initialProjectReads + 2);
    expect(count('До виконання')).toHaveTextContent('0');
    expect(count('У роботі')).toHaveTextContent('0');
    expect(screen.queryByRole('link', { name: newTeamTask.name })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: projects[1].name })).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Створіть свою команду' })).toBeInTheDocument();
    expect(taskQueries().every(query => query.get('performerId') === String(member.id))).toBe(true);
  });
});
