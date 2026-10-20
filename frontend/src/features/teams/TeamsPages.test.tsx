import type { ReactNode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { SessionProfile, Team } from '../../lib/contracts';
import { TeamDetailPage, TeamsPage } from './TeamsPages';

const boundary = vi.hoisted(() => {
  class TestApiError extends Error {
    constructor(public status: number, public traceId?: string, public fieldErrors: Record<string, string[]> = {}) {
      super('Дію не вдалося виконати.');
    }
  }
  return { request: vi.fn(), refresh: vi.fn(), user: null as SessionProfile | null, ApiError: TestApiError };
});
vi.mock('../../lib/api', () => ({ request: boundary.request, ApiError: boundary.ApiError }));
vi.mock('../../lib/session', () => ({ useSession: () => ({ user: boundary.user, status: 'authenticated', refresh: boundary.refresh }) }));

const owner: SessionProfile = { id: 42, userName: 'olena', email: 'olena@example.test', firstName: 'Олена', lastName: 'Коваль', teamId: 17, emailConfirmed: true, roles: [] };
const peer = { id: 43, userName: 'ivan', email: 'ivan@example.test', firstName: 'Іван', lastName: 'Мельник', teamId: 17 };
const team: Team = { id: 17, name: 'Команда API', createdAt: '2026-10-01T12:00:00Z', ownerId: 42, users: [{ ...owner, userName: 'olena', email: 'olena@example.test' }, peer] };
const invitation = { id: 'ebc8a8c0-ae46-4a94-adb9-4de46620c14e', email: 'new@example.test', createdAtUtc: '2026-10-11T12:00:00Z', expiresAtUtc: '2030-10-18T12:00:00Z', acceptedAtUtc: null, revokedAtUtc: null };
const privateToken = 'private-token-1234567890-private-token';

function page(element: ReactNode, path: string) {
  return render(<MemoryRouter initialEntries={[path]}><Routes>
    <Route path="/teams" element={path === '/teams' ? element : <p>Список команд</p>} />
    <Route path="/teams/:id" element={path === '/teams' ? <p>Нова команда</p> : element} />
  </Routes></MemoryRouter>);
}
function respond(teamValue: Team = team, invitations = [invitation]) {
  boundary.request.mockImplementation(async (path: string, _schema: unknown, options?: { method?: string; body?: unknown }) => {
    if (options?.method) return undefined;
    if (path === '/api/teams/17/invitations') return invitations;
    if (path === '/api/teams/17') return teamValue;
    if (path === '/api/teams') return [teamValue];
    throw new Error(`Unexpected test request ${path}`);
  });
}
beforeEach(() => { boundary.request.mockReset(); boundary.refresh.mockReset(); boundary.refresh.mockResolvedValue(undefined); boundary.user = owner; });

describe('team workflows', () => {
  it('shows honest empty state and creates a named team with session refresh', async () => {
    boundary.user = { ...owner, teamId: null };
    boundary.request.mockResolvedValueOnce([]).mockResolvedValueOnce({ id: 17, name: 'Нова команда', createdAt: team.createdAt, ownerId: 42 });
    const user = userEvent.setup(); page(<TeamsPage />, '/teams');
    expect(await screen.findByRole('heading', { name: 'Ви ще не в команді' })).toBeInTheDocument();
    await user.type(screen.getByLabelText('Назва команди'), 'Нова команда');
    await user.click(screen.getByRole('button', { name: 'Створити команду' }));
    expect(boundary.request).toHaveBeenCalledWith('/api/teams', expect.anything(), expect.objectContaining({ method: 'POST', body: { name: 'Нова команда' } }));
    expect(boundary.refresh).toHaveBeenCalled();
    expect(await screen.findByText('Нова команда')).toBeInTheDocument();
  });

  it('lists scoped team members and hides creation for a member already in a team', async () => {
    respond(); page(<TeamsPage />, '/teams');
    expect(await screen.findByRole('link', { name: 'Команда API' })).toHaveAttribute('href', '/teams/17');
    expect(screen.queryByRole('button', { name: 'Створити команду' })).not.toBeInTheDocument();
  });

  it('ordinary members see members and confirm a voluntary leave, without fetching owner invitations', async () => {
    boundary.user = { ...owner, id: 43, email: peer.email, userName: peer.userName }; respond();
    const user = userEvent.setup(); page(<TeamDetailPage />, '/teams/17');
    expect(await screen.findByText('Іван Мельник')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Створити запрошення' })).not.toBeInTheDocument();
    expect(boundary.request.mock.calls.some(call => call[0] === '/api/teams/17/invitations')).toBe(false);
    await user.click(screen.getByRole('button', { name: 'Залишити команду' }));
    expect(boundary.request.mock.calls.some(call => call[0] === '/api/teams/leave')).toBe(false);
    await user.click(screen.getByRole('button', { name: 'Підтвердити вихід' }));
    expect(boundary.request).toHaveBeenCalledWith('/api/teams/leave', undefined, expect.objectContaining({ method: 'POST' }));
    expect(boundary.refresh).toHaveBeenCalled();
    expect(await screen.findByText('Список команд')).toBeInTheDocument();
  });

  it('does not grant owner actions from ownerId when the current session belongs to another team', async () => {
    boundary.user = { ...owner, teamId: 99 }; respond(); page(<TeamDetailPage />, '/teams/17');
    await screen.findByRole('heading', { name: 'Команда API' });
    expect(screen.queryByRole('button', { name: 'Створити запрошення' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Розпустити команду' })).not.toBeInTheDocument();
    expect(boundary.request.mock.calls.some(call => call[0] === '/api/teams/17/invitations')).toBe(false);
  });

  it('creates a private invitation and copies the returned token link without claiming email delivery', async () => {
    respond();
    boundary.request.mockImplementation(async (path: string, _schema: unknown, options?: { method?: string }) => {
      if (path === '/api/teams/17/invitations' && options?.method === 'POST') return { ...invitation, token: privateToken };
      return path.endsWith('/invitations') ? [invitation] : team;
    });
    const user = userEvent.setup(); const clipboard = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    page(<TeamDetailPage />, '/teams/17'); await screen.findByRole('heading', { name: 'Команда API' });
    await user.type(screen.getByLabelText('Email запрошеного учасника'), 'new@example.test');
    await user.click(screen.getByRole('button', { name: 'Створити запрошення' }));
    expect(boundary.request).toHaveBeenCalledWith('/api/teams/17/invitations', expect.anything(), expect.objectContaining({ method: 'POST', body: { email: 'new@example.test' } }));
    await user.click(await screen.findByRole('button', { name: 'Скопіювати нове запрошення' }));
    expect(clipboard).toHaveBeenCalledWith(`${window.location.origin}/invite?token=${privateToken}`);
    expect(await screen.findByRole('status')).toHaveTextContent(/Посилання скопійовано/);
    expect(screen.queryByText(/Лист надіслано/)).not.toBeInTheDocument();
  });

  it('revokes an invitation through the API and reloads its server status', async () => {
    let revoked = false;
    respond(); boundary.request.mockImplementation(async (path: string, _schema: unknown, options?: { method?: string }) => {
      if (options?.method === 'DELETE') { revoked = true; return undefined; }
      return path.endsWith('/invitations') ? [{ ...invitation, revokedAtUtc: revoked ? '2026-10-11T13:00:00Z' : null }] : team;
    });
    const user = userEvent.setup(); page(<TeamDetailPage />, '/teams/17');
    await user.click(await screen.findByRole('button', { name: 'Відкликати запрошення new@example.test' }));
    expect(boundary.request).toHaveBeenCalledWith(`/api/teams/17/invitations/${invitation.id}`, undefined, expect.objectContaining({ method: 'DELETE' }));
    expect(await screen.findByText('Відкликане')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Відкликати запрошення new@example.test' })).not.toBeInTheDocument();
  });

  it('confirms member removal and refreshes the authoritative member list', async () => {
    let removed = false;
    boundary.request.mockImplementation(async (path: string, _schema: unknown, options?: { method?: string }) => {
      if (options?.method === 'DELETE') { removed = true; return undefined; }
      return path.endsWith('/invitations') ? [] : { ...team, users: removed ? [team.users[0]] : team.users };
    });
    const user = userEvent.setup(); page(<TeamDetailPage />, '/teams/17');
    await user.click(await screen.findByRole('button', { name: 'Вилучити Іван Мельник' }));
    expect(boundary.request.mock.calls.some(call => call[2]?.method === 'DELETE')).toBe(false);
    await user.click(screen.getByRole('button', { name: 'Підтвердити вилучення' }));
    expect(boundary.request).toHaveBeenCalledWith('/api/teams/17/members/43', undefined, expect.objectContaining({ method: 'DELETE' }));
    await waitFor(() => expect(screen.queryByText('Іван Мельник')).not.toBeInTheDocument());
    expect(boundary.refresh).toHaveBeenCalled();
  });

  it('renames using only the name contract and refetches current team data', async () => {
    let currentName = team.name;
    boundary.request.mockImplementation(async (path: string, _schema: unknown, options?: { method?: string; body?: { name: string } }) => {
      if (options?.method === 'PUT') { currentName = options.body!.name; return { ...team, name: currentName }; }
      return path.endsWith('/invitations') ? [] : { ...team, name: currentName };
    });
    const user = userEvent.setup(); page(<TeamDetailPage />, '/teams/17');
    const name = await screen.findByLabelText('Назва команди'); await user.clear(name); await user.type(name, 'Нова назва');
    await user.click(screen.getByRole('button', { name: 'Зберегти назву' }));
    expect(boundary.request).toHaveBeenCalledWith('/api/teams/17', expect.anything(), expect.objectContaining({ method: 'PUT', body: { name: 'Нова назва' } }));
    expect(await screen.findByRole('heading', { name: 'Нова назва' })).toBeInTheDocument();
  });

  it('preserves the team and explains project dependencies after refused disband', async () => {
    respond(); boundary.request.mockImplementation(async (path: string, _schema: unknown, options?: { method?: string }) => {
      if (options?.method === 'DELETE') throw new boundary.ApiError(409, 'trace-team-delete');
      return path.endsWith('/invitations') ? [] : team;
    });
    const user = userEvent.setup(); page(<TeamDetailPage />, '/teams/17');
    await user.click(await screen.findByRole('button', { name: 'Розпустити команду' }));
    await user.click(screen.getByRole('button', { name: 'Підтвердити розпуск' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/проєкт/i);
    expect(screen.getByRole('heading', { name: 'Команда API' })).toBeInTheDocument();
    expect(boundary.refresh).not.toHaveBeenCalled();
  });

  it('hides cached owner controls and refreshes rights after a forbidden mutation', async () => {
    respond(); boundary.request.mockImplementation(async (path: string, _schema: unknown, options?: { method?: string }) => {
      if (options?.method === 'POST') throw new boundary.ApiError(403, 'trace-revoked');
      return path.endsWith('/invitations') ? [invitation] : team;
    });
    const user = userEvent.setup(); page(<TeamDetailPage />, '/teams/17');
    await user.type(await screen.findByLabelText('Email запрошеного учасника'), 'new@example.test');
    await user.click(screen.getByRole('button', { name: 'Створити запрошення' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('trace-revoked');
    expect(boundary.refresh).toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: 'Створити запрошення' })).not.toBeInTheDocument();
    expect(screen.queryByText('new@example.test')).not.toBeInTheDocument();
  });
});
