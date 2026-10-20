import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../App';
import { ApiError, request } from '../../lib/api';
import { UsersPage } from './UsersPage';

vi.mock('../../lib/api', async original => ({ ...await original<typeof import('../../lib/api')>(), request: vi.fn() }));
vi.mock('../../lib/session', () => ({ useSession: () => session }));
vi.mock('../notifications/Provider', () => ({ NotificationsProvider: ({ children }: { children: ReactNode }) => children }));
vi.mock('../notifications/context', () => ({ useNotifications: () => null }));
const session = { status: 'authenticated', user: { id: 11, firstName: 'Адмін', lastName: 'Тестовий', userName: 'test-admin', roles: ['admin'] }, refresh: vi.fn(), logout: vi.fn() };
const teams = [{ id: 1, name: 'Тестова команда A', createdAt: '2026-10-01T09:00:00Z', users: [] },
  { id: 2, name: 'Тестова команда B', createdAt: '2026-10-01T09:00:00Z', users: [] }];
const users = Array.from({ length: 14 }, (_, index) => ({ id: index + 1, firstName: `Учасник ${index + 1}`, lastName: 'Тестовий',
  userName: `member${index + 1}`, email: `member${index + 1}@example.test`, teamId: index === 13 ? null : index % 2 + 1,
  passwordHash: 'private-password-hash-sentinel', securityStamp: 'private-security-stamp-sentinel' }));
let directory = users;
function page(element: ReactNode = <UsersPage />) { return render(<MemoryRouter initialEntries={['/users']}>{element}</MemoryRouter>); }

beforeEach(() => {
  directory = users; session.user.roles = ['admin']; vi.mocked(request).mockReset();
  vi.mocked(request).mockImplementation(async (path, schema) => {
    if (path === '/api/users') return schema!.parse(directory);
    if (path === '/api/teams') return schema!.parse(teams);
    throw new Error('Unexpected synthetic admin directory route');
  });
});

describe('administrator user directory', () => {
  it.each([{ roles: [] }, { roles: ['member'] }, { roles: ['administrator'] }])('denies $roles before mounting any directory loader', ({ roles }) => {
    session.user.roles = roles; page();
    expect(screen.getByRole('alert')).toHaveTextContent('Ви не маєте дозволу на цю дію.');
    expect(request).not.toHaveBeenCalled();
  });

  it('shows all fourteen users with canonical team links and no authentication internals', async () => {
    session.user.roles = ['ADMIN']; page();
    const table = await screen.findByRole('table');
    expect(within(table).getAllByRole('row')).toHaveLength(15);
    expect(screen.getByText('14 користувачів')).toBeInTheDocument();
    for (const user of users) {
      expect(within(table).getByText(`${user.firstName} ${user.lastName}`)).toBeInTheDocument();
      expect(within(table).getByText(user.userName)).toBeInTheDocument();
      expect(within(table).getByText(user.email)).toBeInTheDocument();
    }
    expect(within(table).getAllByRole('link', { name: teams[0].name })[0]).toHaveAttribute('href', '/teams/1');
    expect(within(table).getAllByRole('link', { name: teams[1].name })[0]).toHaveAttribute('href', '/teams/2');
    expect(within(table).getByText('Без команди')).toBeInTheDocument();
    expect(screen.queryByText(/private-.*-sentinel/)).not.toBeInTheDocument();
    expect(vi.mocked(request).mock.calls.every(([, , options]) => !options?.method || options.method === 'GET')).toBe(true);
  });

  it('shows an empty directory honestly', async () => {
    directory = []; page();
    expect(await screen.findByText('Користувачів поки немає')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('retries a failed API read and replaces the error with the actual directory', async () => {
    vi.mocked(request).mockRejectedValueOnce(new ApiError(503)); page();
    expect(await screen.findByRole('alert')).toHaveTextContent('Сервіс тимчасово недоступний.');
    await userEvent.setup().click(screen.getByRole('button', { name: 'Повторити' }));
    await screen.findByRole('table');
    expect(vi.mocked(request).mock.calls.filter(([path]) => path === '/api/users')).toHaveLength(2);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('clears the mounted directory when the refreshed session loses its administrator role', async () => {
    const view = page(); await screen.findByRole('table');
    const calls = vi.mocked(request).mock.calls.length;
    session.user.roles = []; view.rerender(<MemoryRouter><UsersPage /></MemoryRouter>);
    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(screen.queryByText(users[0].email)).not.toBeInTheDocument();
    await waitFor(() => expect(request).toHaveBeenCalledTimes(calls));
  });

  it('exposes the real directory route and plural teams navigation only to administrators', async () => {
    page(<App />);
    const navigation = screen.getByRole('navigation', { name: 'Головна навігація' });
    expect(within(navigation).getByRole('link', { name: 'Користувачі' })).toHaveAttribute('href', '/users');
    expect(within(navigation).getByRole('link', { name: 'Команди' })).toHaveAttribute('href', '/teams');
    expect(await screen.findByRole('table')).toBeInTheDocument();
  });

  it('hides the administrator navigation and rejects a direct ordinary-user route', () => {
    session.user.roles = ['member']; page(<App />);
    const navigation = screen.getByRole('navigation', { name: 'Головна навігація' });
    expect(within(navigation).queryByRole('link', { name: 'Користувачі' })).not.toBeInTheDocument();
    expect(within(navigation).getByRole('link', { name: 'Команда' })).toBeInTheDocument();
    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(request).not.toHaveBeenCalled();
  });
});
