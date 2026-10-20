import { createElement, Fragment } from 'react';
import type { ReactNode } from 'react';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { SessionProvider } from './SessionProvider';
import { useSession } from './session';
import { localeStorageKey, setLocale } from './i18n';
import type { SessionProfile } from './contracts';

// Real App/session refresh, API transport and response schemas. HTTP responses
// are explicit synthetic fixtures; Identity/SQL/SignalR are not claimed here.
vi.mock('../features/notifications/Provider', () => ({
  NotificationsProvider: ({ children }: { children: ReactNode }) => children,
}));
vi.mock('../features/notifications/context', () => ({ useNotifications: () => null }));

const instant = '2026-10-01T09:00:00Z';
const version = 'b4ce88bd-4822-46cb-9d52-cd9f46827847';
const draftId = 'e5d5c4f6-cf03-474a-85e1-c12171d4b401';
const source = 'Synthetic private source from former administrator scope';
const projects = [1, 2].map(id => ({ id, authorId: 11, teamId: id,
  name: `Synthetic project team ${id}`, description: '', createdAt: instant, deadline: instant }));
let profile: SessionProfile;
let holdTaskRead = false;
let releaseTaskRead: (() => void) | undefined;
const allowed = () => profile.roles.includes('admin') ? [1, 2] : profile.teamId === null ? [] : [profile.teamId];
function json(value: unknown, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
}
function tasks() {
  return { items: allowed().map(id => ({ id, projectId: id, performerId: 11,
    name: `Synthetic task team ${id}`, description: '', state: 'ToDo', createdAt: instant,
    reminderEnabled: false, escalationEnabled: false, version })), page: 1, pageSize: 20, totalCount: allowed().length };
}
function RefreshProbe() {
  const session = useSession();
  return createElement('button', { onClick: () => void session.refresh() }, 'Refresh synthetic identity');
}
function page(path: string) {
  return render(createElement(SessionProvider, null,
    createElement(Fragment, null, createElement(RefreshProbe),
      createElement(MemoryRouter, { initialEntries: [path] }, createElement(App)))));
}
async function refreshIdentity() {
  await userEvent.setup().click(screen.getByRole('button', { name: 'Refresh synthetic identity' }));
}
beforeEach(() => {
  profile = { id: 11, userName: 'synthetic-owner', email: 'owner@example.test', firstName: 'Synthetic',
    lastName: 'Owner', teamId: 1, emailConfirmed: true, roles: ['admin'] };
  holdTaskRead = false; releaseTaskRead = undefined;
  vi.stubGlobal('fetch', vi.fn<typeof fetch>(async input => {
    const path = String(input).split('?')[0];
    if (path === '/api/auth/me') return json({ ...profile, roles: [...profile.roles] });
    if (path === '/api/projects') return json(projects.filter(value => allowed().includes(value.teamId)));
    if (path === '/api/tasks/page') {
      const response = json(tasks());
      if (holdTaskRead) { holdTaskRead = false; return new Promise<Response>(resolve => { releaseTaskRead = () => resolve(response); }); }
      return response;
    }
    const team = path.match(/^\/api\/teams\/([12])$/);
    if (team) return json({ id: Number(team[1]), name: `Synthetic team ${team[1]}`, createdAt: instant, users: [] });
    if (path === '/api/ai/status') return json({ available: true, provider: 'fake', maxInputCharacters: 20_000, maxItems: 25 });
    if (path === `/api/ai/task-extractions/${draftId}`) return profile.roles.includes('admin')
      ? json({ id: draftId, ownerId: 11, projectId: 2, version, createdAtUtc: instant,
        expiresAtUtc: '2030-10-01T09:00:00Z', locale: 'uk', timeZoneId: 'Europe/Kyiv', referenceDate: '2026-10-01',
        items: [{ draftItemId: version, title: 'Synthetic proposal', description: '', sourceQuote: source,
          sourceStart: 0, sourceLength: source.length, isSuggestion: false, interpretations: [], clarificationReasons: [] }] })
      : json({ status: 403 }, 403);
    throw new Error('Unexpected synthetic security boundary route');
  }));
});

describe('private workspace scope after actual session refresh', () => {
  it('removes former administrator task rows and reloads the current member scope', async () => {
    page('/tasks?view=board');
    await screen.findByRole('link', { name: 'Synthetic task team 2' });
    profile = { ...profile, roles: [] };
    await refreshIdentity();
    await waitFor(() => expect(screen.queryByRole('link', { name: 'Synthetic task team 2' })).not.toBeInTheDocument());
    expect(await screen.findByRole('link', { name: 'Synthetic task team 1' })).toBeInTheDocument();
  });

  it('removes the old team project directory when the current membership changes', async () => {
    profile = { ...profile, roles: [] };
    page('/projects');
    await screen.findByRole('link', { name: 'Synthetic project team 1' });
    profile = { ...profile, teamId: 2 };
    await refreshIdentity();
    expect(await screen.findByRole('link', { name: 'Synthetic project team 2' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Synthetic project team 1' })).not.toBeInTheDocument();
  });

  it('clears restored AI source on scope loss while a public locale change preserves it', async () => {
    const storage = vi.spyOn(Storage.prototype, 'setItem');
    page(`/ai-notes?draftId=${draftId}`);
    await screen.findByText(source);
    act(() => setLocale('en'));
    expect(screen.getByText(source)).toBeInTheDocument();
    profile = { ...profile, roles: [] };
    await refreshIdentity();
    await waitFor(() => expect(screen.queryByText(source)).not.toBeInTheDocument());
    expect(storage.mock.calls.every(([key, value]) => key === localeStorageKey && value === 'en')).toBe(true);
  });

  it('ignores a former-scope HTTP response that arrives after refreshed membership', async () => {
    holdTaskRead = true;
    page('/tasks?view=board');
    await waitFor(() => expect(releaseTaskRead).toBeTypeOf('function'));
    profile = { ...profile, roles: [], teamId: 1 };
    await refreshIdentity();
    expect(await screen.findByRole('link', { name: 'Synthetic task team 1' })).toBeInTheDocument();
    await act(async () => releaseTaskRead!());
    expect(screen.queryByRole('link', { name: 'Synthetic task team 2' })).not.toBeInTheDocument();
  });
});
