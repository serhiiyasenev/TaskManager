import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request, sessionExpiredEvent } from './api';
import { SessionProvider } from './SessionProvider';
import { useSession } from './session';
vi.mock('./api', async (original) => ({ ...await original<typeof import('./api')>(), request: vi.fn() }));
const profile = { id: 1, userName: 'member', email: 'member@example.test', firstName: 'Олена', lastName: 'Коваль', teamId: 1, emailConfirmed: true, roles: [] };
function Probe() {
  const session = useSession();
  return <><p role="status">{session.status}:{session.user?.email}</p><button onClick={() => void session.logout()}>Вийти</button></>;
}
describe('session response ordering', () => {
  beforeEach(() => vi.mocked(request).mockReset());
  it('does not restore a signed-out identity when an earlier profile request finishes late', async () => {
    let resolveProfile!: (value: unknown) => void;
    vi.mocked(request).mockImplementationOnce(() => new Promise(resolve => { resolveProfile = resolve; })).mockResolvedValueOnce(undefined);
    render(<SessionProvider><Probe /></SessionProvider>);
    fireEvent.click(screen.getByRole('button', { name: 'Вийти' }));
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('anonymous:'));
    await act(async () => resolveProfile(profile));
    expect(screen.getByRole('status')).toHaveTextContent('anonymous:');
    expect(screen.queryByText(/member@example.test/)).not.toBeInTheDocument();
  });
  it('does not restore an expired identity from an earlier response', async () => {
    let resolveProfile!: (value: unknown) => void;
    vi.mocked(request).mockImplementationOnce(() => new Promise(resolve => { resolveProfile = resolve; }));
    render(<SessionProvider><Probe /></SessionProvider>);
    act(() => window.dispatchEvent(new Event(sessionExpiredEvent)));
    await act(async () => resolveProfile(profile));
    expect(screen.getByRole('status')).toHaveTextContent('anonymous:');
  });
});
