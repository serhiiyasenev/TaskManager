import type { ReactNode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { SessionProfile } from '../../lib/contracts';
import { ConfirmEmailPage, ForgotPasswordPage, InvitationPage, LoginPage, ProfilePage, RegisterPage, ResetPasswordPage } from './AuthPages';

const boundary = vi.hoisted(() => {
  class TestApiError extends Error {
    constructor(public status: number, message = 'Безпечна помилка', public traceId?: string,
      public fieldErrors: Record<string, string[]> = {}) { super(message); }
  }
  return {
    request: vi.fn(), login: vi.fn(), refresh: vi.fn(), logout: vi.fn(),
    user: null as SessionProfile | null,
    status: 'anonymous' as 'anonymous' | 'authenticated' | 'loading' | 'error',
    ApiError: TestApiError,
  };
});
vi.mock('../../lib/api', () => ({ request: boundary.request, ApiError: boundary.ApiError }));
vi.mock('../../lib/session', () => ({ useSession: () => ({ user: boundary.user, status: boundary.status,
  login: boundary.login, refresh: boundary.refresh, logout: boundary.logout }) }));

const profile: SessionProfile = {
  id: 42, userName: 'olena', email: 'olena@example.test', firstName: 'Олена', lastName: 'Коваль',
  teamId: null, emailConfirmed: true, roles: [],
};
const token = 'synthetic-invitation-token-1234567890';

function page(element: ReactNode, path = '/login') {
  return render(<MemoryRouter initialEntries={[path]}><Routes>
    <Route path={path.split('?')[0]} element={element} />
    <Route path="*" element={<p>Робочий простір</p>} />
  </Routes></MemoryRouter>);
}

beforeEach(() => {
  boundary.request.mockReset(); boundary.login.mockReset(); boundary.refresh.mockReset(); boundary.logout.mockReset();
  boundary.login.mockResolvedValue(undefined); boundary.refresh.mockResolvedValue(undefined); boundary.logout.mockResolvedValue(undefined);
  boundary.request.mockResolvedValue({ message: 'Done' }); boundary.user = null; boundary.status = 'anonymous';
});

describe('browser account workflows', () => {
  it('submits labelled login fields through the cookie session hook and returns to the workspace', async () => {
    const user = userEvent.setup(); page(<LoginPage />);
    await user.type(screen.getByLabelText('Email'), 'olena@example.test');
    await user.type(screen.getByLabelText('Пароль'), 'TestPassword123!');
    await user.click(screen.getByRole('button', { name: 'Увійти' }));
    expect(boundary.login).toHaveBeenCalledWith('olena@example.test', 'TestPassword123!');
    expect(await screen.findByText('Робочий простір')).toBeInTheDocument();
  });

  it('preserves input and explains failed login with a safe trace identifier', async () => {
    boundary.login.mockRejectedValueOnce(new boundary.ApiError(401, '<script>private internal detail</script>', 'trace-auth'));
    const user = userEvent.setup(); page(<LoginPage />);
    await user.type(screen.getByLabelText('Email'), 'olena@example.test');
    await user.type(screen.getByLabelText('Пароль'), 'TestPassword123!');
    await user.click(screen.getByRole('button', { name: 'Увійти' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/email.*пароль/i);
    expect(screen.getByRole('alert')).toHaveTextContent('trace-auth');
    expect(screen.getByLabelText('Email')).toHaveValue('olena@example.test');
    expect(screen.queryByText(/private internal detail/)).not.toBeInTheDocument();
  });

  it('does not submit a registration with mismatched passwords and links the error to the input', async () => {
    const user = userEvent.setup(); page(<RegisterPage />, '/register');
    await user.type(screen.getByLabelText('Ім’я користувача'), 'olena');
    await user.type(screen.getByLabelText('Ім’я', { exact: true }), 'Олена');
    await user.type(screen.getByLabelText('Прізвище'), 'Коваль');
    await user.type(screen.getByLabelText('Email'), 'olena@example.test');
    await user.type(screen.getByLabelText('Пароль'), 'TestPassword123!');
    await user.type(screen.getByLabelText('Повторіть пароль'), 'DifferentPassword123!');
    await user.click(screen.getByRole('button', { name: 'Зареєструватися' }));
    expect(boundary.request).not.toHaveBeenCalled();
    const field = screen.getByLabelText('Повторіть пароль');
    expect(field).toHaveAttribute('aria-invalid', 'true');
    expect(field).toHaveAccessibleDescription(/Паролі не збігаються/);
  });

  it('registers without privilege fields and offers a real confirmation resend', async () => {
    const user = userEvent.setup(); page(<RegisterPage />, '/register');
    await user.type(screen.getByLabelText('Ім’я користувача'), 'olena');
    await user.type(screen.getByLabelText('Ім’я', { exact: true }), 'Олена');
    await user.type(screen.getByLabelText('Прізвище'), 'Коваль');
    await user.type(screen.getByLabelText('Email'), 'olena@example.test');
    await user.type(screen.getByLabelText('Пароль'), 'TestPassword123!');
    await user.type(screen.getByLabelText('Повторіть пароль'), 'TestPassword123!');
    await user.click(screen.getByRole('button', { name: 'Зареєструватися' }));
    expect(await screen.findByRole('status')).toHaveTextContent(/Перевірте пошту/);
    const options = boundary.request.mock.calls.find(call => call[0] === '/api/auth/register')?.[2];
    expect(options.body).toEqual({ userName: 'olena', firstName: 'Олена', lastName: 'Коваль', email: 'olena@example.test', password: 'TestPassword123!' });
    await user.click(screen.getByRole('button', { name: 'Надіслати підтвердження ще раз' }));
    expect(boundary.request).toHaveBeenCalledWith('/api/auth/resend-confirmation', expect.anything(), expect.objectContaining({ method: 'POST', body: { email: 'olena@example.test' } }));
  });

  it('requires an explicit click before posting the confirmation token from the URL', async () => {
    const user = userEvent.setup(); page(<ConfirmEmailPage />, '/confirm-email?userId=42&token=encoded-value');
    expect(boundary.request).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Підтвердити email' }));
    expect(boundary.request).toHaveBeenCalledWith('/api/auth/confirm-email', expect.anything(), expect.objectContaining({ method: 'POST', body: { userId: '42', token: 'encoded-value' } }));
    expect(await screen.findByRole('status')).toHaveTextContent(/Email підтверджено/);
  });

  it('shows generic recovery notice after a real forgot-password request', async () => {
    const user = userEvent.setup(); page(<ForgotPasswordPage />, '/forgot-password');
    await user.type(screen.getByLabelText('Email'), 'unknown@example.test');
    await user.click(screen.getByRole('button', { name: 'Надіслати посилання' }));
    expect(boundary.request).toHaveBeenCalledWith('/api/auth/forgot-password', expect.anything(), expect.objectContaining({ method: 'POST', body: { email: 'unknown@example.test' } }));
    expect(await screen.findByRole('status')).toHaveTextContent(/Якщо.*адрес/i);
  });

  it('resets the password using the encoded recovery-link token without replacing it', async () => {
    const user = userEvent.setup(); page(<ResetPasswordPage />, '/reset-password?email=olena%40example.test&token=encoded-recovery-token');
    await user.type(screen.getByLabelText('Новий пароль'), 'ReplacementPassword123!');
    await user.type(screen.getByLabelText('Повторіть новий пароль'), 'ReplacementPassword123!');
    await user.click(screen.getByRole('button', { name: 'Змінити пароль' }));
    expect(boundary.request).toHaveBeenCalledWith('/api/auth/reset-password', expect.anything(), expect.objectContaining({ method: 'POST', body: { email: 'olena@example.test', token: 'encoded-recovery-token', newPassword: 'ReplacementPassword123!' } }));
    expect(await screen.findByRole('status')).toHaveTextContent(/Пароль змінено/);
  });

  it('updates profile names while retaining the server membership and email', async () => {
    boundary.user = profile; boundary.status = 'authenticated';
    boundary.request.mockResolvedValueOnce({ ...profile, birthDay: '1990-01-01T00:00:00', registeredAt: '2026-01-01T00:00:00Z' });
    const user = userEvent.setup(); page(<ProfilePage />, '/profile');
    const firstName = await screen.findByLabelText('Ім’я', { exact: true });
    await user.clear(firstName); await user.type(firstName, 'Олена Марія');
    await user.click(screen.getByRole('button', { name: 'Зберегти профіль' }));
    expect(boundary.request).toHaveBeenCalledWith('/api/users/42', expect.anything(), expect.objectContaining({ method: 'PUT', body: expect.objectContaining({ firstName: 'Олена Марія', lastName: 'Коваль', teamId: null, email: 'olena@example.test' }) }));
    expect(boundary.refresh).toHaveBeenCalled();
    expect(await screen.findByRole('status')).toHaveTextContent(/Профіль збережено/);
  });
});

describe('private team invitation', () => {
  it('previews before joining, requires confirmation and refreshes the server session', async () => {
    boundary.user = profile; boundary.status = 'authenticated';
    boundary.request.mockResolvedValueOnce({ teamName: 'Команда API', expired: false }).mockResolvedValueOnce({ teamId: 17 });
    const user = userEvent.setup(); page(<InvitationPage />, `/invite?token=${token}`);
    expect(await screen.findByText('Команда API')).toBeInTheDocument();
    expect(boundary.request.mock.calls.filter(call => call[0] === '/api/team-invitations/accept')).toHaveLength(0);
    await user.click(screen.getByRole('button', { name: 'Приєднатися до команди' }));
    expect(boundary.request).toHaveBeenCalledWith('/api/team-invitations/accept', expect.anything(), expect.objectContaining({ method: 'POST', body: { token } }));
    expect(boundary.refresh).toHaveBeenCalled();
    expect(await screen.findByRole('status')).toHaveTextContent(/Ви приєдналися/);
  });

  it('keeps an expired invitation closed', async () => {
    boundary.user = profile; boundary.status = 'authenticated';
    boundary.request.mockResolvedValueOnce({ teamName: 'Команда API', expired: true });
    page(<InvitationPage />, `/invite?token=${token}`);
    expect(await screen.findByRole('alert')).toHaveTextContent(/прострочене|відкликане/);
    expect(screen.queryByRole('button', { name: 'Приєднатися до команди' })).not.toBeInTheDocument();
  });

  it('explains wrong-email denial and leaves the invitation unaccepted', async () => {
    boundary.user = profile; boundary.status = 'authenticated';
    boundary.request.mockResolvedValueOnce({ teamName: 'Команда API', expired: false }).mockRejectedValueOnce(new boundary.ApiError(403));
    const user = userEvent.setup(); page(<InvitationPage />, `/invite?token=${token}`);
    await user.click(await screen.findByRole('button', { name: 'Приєднатися до команди' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/email|адрес/i);
    expect(boundary.refresh).not.toHaveBeenCalled();
  });

  it('offers sign-in with the same invitation route and never joins anonymously', async () => {
    boundary.request.mockResolvedValueOnce({ teamName: 'Команда API', expired: false });
    page(<InvitationPage />, `/invite?token=${token}`);
    const login = await screen.findByRole('link', { name: 'Увійти та переглянути запрошення' });
    expect(login).toHaveAttribute('href', `/login?returnTo=${encodeURIComponent(`/invite?token=${token}`)}`);
    expect(boundary.request.mock.calls.filter(call => call[0] === '/api/team-invitations/accept')).toHaveLength(0);
  });

  it('retries a failed preview rather than displaying invented team data', async () => {
    boundary.request.mockRejectedValueOnce(new boundary.ApiError(503)).mockResolvedValueOnce({ teamName: 'Команда після повтору', expired: false });
    const user = userEvent.setup(); page(<InvitationPage />, `/invite?token=${token}`);
    await user.click(await screen.findByRole('button', { name: 'Повторити' }));
    expect(await screen.findByText('Команда після повтору')).toBeInTheDocument();
    await waitFor(() => expect(boundary.request).toHaveBeenCalledTimes(2));
  });
});
