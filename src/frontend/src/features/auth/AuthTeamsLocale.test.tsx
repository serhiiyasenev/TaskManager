import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, request } from '../../lib/api';
import { setLocale, useLocale } from '../../lib/i18n';
import { ConfirmEmailPage, ForgotPasswordPage, InvitationPage, LoginPage, ProfilePage, RegisterPage, ResetPasswordPage } from './AuthPages';
import { authCopy } from './authCopy';
import { TeamDetailPage } from '../teams/TeamsPages';
import { teamsCopy } from '../teams/teamsCopy';

vi.mock('../../lib/api', async original => ({ ...await original<typeof import('../../lib/api')>(), request: vi.fn() }));
vi.mock('../../lib/session', () => ({ useSession: () => session }));
const owner = { id: 42, userName: 'olena', email: 'olena@example.test', firstName: 'Олена', lastName: 'Коваль',
  teamId: 17, emailConfirmed: true, roles: [] };
const session = { user: owner, status: 'authenticated', login: vi.fn(), refresh: vi.fn(), logout: vi.fn() };
const team = { id: 17, name: 'Команда API', ownerId: 42, createdAt: '2026-10-01T12:00:00Z', users: [owner] };
const token = 'synthetic-private-invitation-token-1234567890';
const invitation = { id: 'ebc8a8c0-ae46-4a94-adb9-4de46620c14e', email: 'new@example.test',
  createdAtUtc: '2026-10-01T12:00:00Z', expiresAtUtc: '2030-10-18T12:00:00Z', acceptedAtUtc: null, revokedAtUtc: null };

function LocaleControl() {
  const { locale, setLocale: selectLocale } = useLocale();
  return <button type="button" onClick={() => selectLocale(locale === 'uk' ? 'en' : 'uk')}>Switch locale</button>;
}
function page(element: ReactNode, path = '/login') {
  return render(<MemoryRouter initialEntries={[path]}><LocaleControl /><Routes>
    <Route path={path.startsWith('/teams/') ? '/teams/:id' : path.split('?')[0]} element={element} />
    <Route path="*" element={<p>Destination</p>} />
  </Routes></MemoryRouter>);
}

beforeEach(() => {
  setLocale('uk'); session.user = owner; session.login.mockReset(); session.refresh.mockReset(); session.logout.mockReset();
  session.login.mockResolvedValue(undefined); session.refresh.mockResolvedValue(undefined); session.logout.mockResolvedValue(undefined);
  vi.mocked(request).mockReset();
  vi.mocked(request).mockImplementation(async (path, _schema, options) => {
    if (path.startsWith('/api/team-invitations/preview?')) return { teamName: team.name, expired: false };
    if (path === '/api/team-invitations/accept') return { teamId: 17 };
    if (path === '/api/teams/17/invitations') return options?.method === 'POST' ? { ...invitation, token } : [invitation];
    if (path === '/api/teams/17') return team;
    if (path === '/api/users/42') return { ...owner, birthDay: '1990-01-01T00:00:00Z' };
    return { message: 'Done' };
  });
});
afterEach(() => { cleanup(); setLocale('uk'); });

describe('reactive account and team languages', () => {
  it.each([{ name: 'accounts', catalog: authCopy }, { name: 'teams', catalog: teamsCopy }])('keeps $name keys and placeholders paired', ({ catalog }) => {
    expect(Object.keys(catalog.en).sort()).toEqual(Object.keys(catalog.uk).sort());
    const target = catalog.en as Record<string, string>;
    for (const [key, value] of Object.entries(catalog.uk)) {
      expect(target[key].trim(), key).not.toBe('');
      expect(target[key].match(/\{[A-Za-z][A-Za-z0-9_]*\}/g) ?? [], key).toEqual(value.match(/\{[A-Za-z][A-Za-z0-9_]*\}/g) ?? []);
    }
  });

  it('signs in through English labels with the same session workflow', async () => {
    setLocale('en'); page(<LoginPage />); const actor = userEvent.setup();
    await actor.type(screen.getByLabelText('Email'), owner.email);
    await actor.type(screen.getByLabelText('Password'), 'SyntheticPassword123!');
    expect(screen.getByRole('link', { name: 'Forgot password?' })).toHaveAttribute('href', '/forgot-password');
    await actor.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(session.login).toHaveBeenCalledWith(owner.email, 'SyntheticPassword123!');
    expect(await screen.findByText('Destination')).toBeInTheDocument();
  });

  it('switches a typed login in place without clearing fields or sending requests', async () => {
    page(<LoginPage />); const actor = userEvent.setup();
    await actor.type(screen.getByLabelText('Email'), owner.email); await actor.type(screen.getByLabelText('Пароль'), 'KeepPassword123!');
    await actor.click(screen.getByRole('button', { name: 'Switch locale' }));
    expect(screen.getByLabelText('Password')).toHaveValue('KeepPassword123!');
    expect(screen.getByLabelText('Email')).toHaveValue(owner.email);
    expect(screen.getByRole('heading', { name: 'Sign in to TaskManager' })).toBeInTheDocument();
    expect(session.login).not.toHaveBeenCalled(); expect(request).not.toHaveBeenCalled();
  });

  it('translates a stored validation message while preserving registration data', async () => {
    page(<RegisterPage />, '/register'); const actor = userEvent.setup();
    await actor.type(screen.getByLabelText('Пароль'), 'PasswordOne123!'); await actor.type(screen.getByLabelText('Повторіть пароль'), 'DifferentPassword123!');
    await actor.click(screen.getByRole('button', { name: 'Зареєструватися' }));
    expect(screen.getByLabelText('Повторіть пароль')).toHaveAccessibleDescription('Паролі не збігаються.');
    await actor.click(screen.getByRole('button', { name: 'Switch locale' }));
    expect(screen.getByLabelText('Repeat password')).toHaveValue('DifferentPassword123!');
    expect(screen.getByLabelText('Repeat password')).toHaveAccessibleDescription('Passwords do not match.');
    expect(request).not.toHaveBeenCalled();
  });

  it('translates a completed recovery notice without sending another recovery email', async () => {
    page(<ForgotPasswordPage />, '/forgot-password'); const actor = userEvent.setup();
    await actor.type(screen.getByLabelText('Email'), owner.email); await actor.click(screen.getByRole('button', { name: 'Надіслати посилання' }));
    expect(await screen.findByRole('status')).toHaveTextContent(authCopy.uk.recoverySent);
    await actor.click(screen.getByRole('button', { name: 'Switch locale' }));
    expect(screen.getByRole('status')).toHaveTextContent(authCopy.en.recoverySent);
    expect(request).toHaveBeenCalledTimes(1); expect(screen.getByLabelText('Email')).toHaveValue(owner.email);
  });

  it.each([
    { element: <ConfirmEmailPage />, path: '/confirm-email?userId=42&token=synthetic', heading: 'Email confirmation' },
    { element: <ResetPasswordPage />, path: '/reset-password?email=olena%40example.test&token=synthetic', heading: 'Reset password' },
    { element: <ProfilePage />, path: '/profile', heading: 'My profile' },
  ])('uses English account copy at $path', async ({ element, path, heading }) => {
    setLocale('en'); page(element, path);
    expect(await screen.findByRole('heading', { name: heading })).toBeInTheDocument();
    if (path === '/profile') {
      expect(await screen.findByLabelText('Birthday (optional)')).toBeInTheDocument();
      expect(screen.getByText(/Email confirmed/)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument();
    }
  });

  it('preserves the exact invitation token and account while changing language', async () => {
    setLocale('en'); page(<InvitationPage />, `/invite?token=${token}`); const actor = userEvent.setup();
    expect(await screen.findByRole('heading', { name: 'Team invitation' })).toBeInTheDocument();
    expect(await screen.findByRole('heading', { name: team.name })).toBeInTheDocument();
    await actor.click(screen.getByRole('button', { name: 'Switch locale' }));
    await actor.click(screen.getByRole('button', { name: 'Приєднатися до команди' }));
    await waitFor(() => expect(request).toHaveBeenCalledWith('/api/team-invitations/accept', expect.anything(), expect.objectContaining({ method: 'POST', body: { token } })));
    expect(vi.mocked(request).mock.calls.filter(([path]) => path.startsWith('/api/team-invitations/preview?'))).toHaveLength(1);
    expect(session.user).toBe(owner); expect(session.refresh).toHaveBeenCalled();
  });

  it('switches owner invitation controls without losing a typed name or one-time link', async () => {
    page(<TeamDetailPage />, '/teams/17'); const actor = userEvent.setup();
    const name = await screen.findByLabelText('Назва команди'); await actor.clear(name); await actor.type(name, 'My typed team');
    await actor.type(screen.getByLabelText('Email запрошеного учасника'), invitation.email);
    await actor.click(screen.getByRole('button', { name: 'Створити запрошення' }));
    const link = await screen.findByLabelText('Посилання для запрошеної людини');
    const privateLink = (link as HTMLInputElement).value;
    await actor.click(screen.getByRole('button', { name: 'Switch locale' }));
    expect(screen.getByLabelText('Team name')).toHaveValue('My typed team');
    expect(screen.getByLabelText('Link for the invited person')).toHaveValue(privateLink);
    expect(screen.getByRole('button', { name: 'Copy new invitation' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: team.name })).toBeInTheDocument();
    expect(screen.getAllByText(/Expires:.*Oct/).length).toBeGreaterThan(0);
    expect(vi.mocked(request).mock.calls.filter(([, , options]) => options?.method === 'POST')).toHaveLength(1);
  });

  it('translates a stored team success and preserves the user-authored team name', async () => {
    page(<TeamDetailPage />, '/teams/17'); const actor = userEvent.setup();
    await screen.findByLabelText('Назва команди'); await actor.click(screen.getByRole('button', { name: 'Зберегти назву' }));
    expect(await screen.findByRole('status')).toHaveTextContent(teamsCopy.uk.renamed);
    await actor.click(screen.getByRole('button', { name: 'Switch locale' }));
    expect(screen.getByRole('status')).toHaveTextContent(teamsCopy.en.renamed);
    expect(screen.getByRole('heading', { name: team.name })).toBeInTheDocument();
  });

  it('uses English copy for a rejected invitation without showing raw server content', async () => {
    setLocale('en'); vi.mocked(request).mockRejectedValueOnce(new ApiError(403));
    page(<InvitationPage />, `/invite?token=${token}`);
    expect(await screen.findByRole('alert')).toHaveTextContent(authCopy.en.wrongInvitationEmail);
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});
