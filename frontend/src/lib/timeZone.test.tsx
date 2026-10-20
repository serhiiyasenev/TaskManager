import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AINotesPage } from '../features/ai/AINotesPage';
import { TaskFormPage } from '../features/work/TasksPages';
import { request } from './api';
import { browserTimeZone, canonicalTimeZone } from './timeZone';

vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), request: vi.fn() }));
vi.mock('./session', () => ({ useSession: () => ({ user: { id: 1, teamId: 1 }, refresh: vi.fn() }) }));

const draftId = '7a4f8cd9-cd7f-4b36-b9e0-b473f5dff64b';
const project = { id: 9, name: 'Тестовий проєкт', teamId: 1 };
const draft = { id: draftId, ownerId: 1, projectId: 9, version: draftId,
  createdAtUtc: '2026-10-12T09:00:00Z', expiresAtUtc: '2030-10-13T09:00:00Z', locale: 'uk',
  timeZoneId: 'Europe/Kiev', referenceDate: '2026-10-12', items: [] };

function browserReports(timeZone: string) {
  const options = Intl.DateTimeFormat().resolvedOptions();
  vi.spyOn(Intl.DateTimeFormat.prototype, 'resolvedOptions').mockReturnValue({ ...options, timeZone });
}

beforeEach(() => {
  vi.mocked(request).mockReset();
  vi.mocked(request).mockImplementation(async path => {
    if (path === '/api/projects') return [project];
    if (path === '/api/teams/1') return { id: 1, users: [] };
    if (path === '/api/ai/status') return { available: true, provider: 'azure', maxInputCharacters: 20_000, maxItems: 25 };
    if (path === `/api/ai/task-extractions/${draftId}`) return draft;
    throw new Error('Unexpected synthetic timezone route');
  });
});

describe('user-facing time zones', () => {
  it('uses Kyiv when Intl returns the legacy browser identifier', () => {
    browserReports('Europe/Kiev');
    expect(browserTimeZone()).toBe('Europe/Kyiv');
    expect(canonicalTimeZone('Europe/Kiev')).toBe('Europe/Kyiv');
  });

  it.each(['Europe/Kyiv', 'Europe/Warsaw', 'America/Toronto', 'Asia/Tokyo', 'UTC'])('preserves %s', timeZone => {
    browserReports(timeZone);
    expect(browserTimeZone()).toBe(timeZone);
    expect(canonicalTimeZone(timeZone)).toBe(timeZone);
  });

  it.each(['2026-01-12T12:00:00Z', '2026-07-12T12:00:00Z', '2026-03-29T01:30:00Z', '2026-10-25T01:30:00Z'])('preserves the local deadline at %s', instant => {
    const format = (timeZone: string) => new Intl.DateTimeFormat('uk-UA', {
      timeZone, dateStyle: 'full', timeStyle: 'long',
    }).format(new Date(instant));
    expect(format(canonicalTimeZone('Europe/Kiev'))).toBe(format('Europe/Kiev'));
  });

  it('shows Kyiv in the actual task form with a legacy browser runtime', async () => {
    browserReports('Europe/Kiev');
    render(<MemoryRouter><TaskFormPage /></MemoryRouter>);
    expect(await screen.findByText('Строк задається у вашому часовому поясі: Europe/Kyiv.')).toBeInTheDocument();
  });

  it('defaults and restores AI zones as Kyiv while preserving an editable user choice', async () => {
    browserReports('Europe/Kiev');
    const view = render(<MemoryRouter><AINotesPage /></MemoryRouter>);
    const zone = await screen.findByLabelText('Часовий пояс', { exact: true });
    expect(zone).toHaveValue('Europe/Kyiv');
    const actor = userEvent.setup();
    await actor.clear(zone); await actor.type(zone, 'Europe/Warsaw');
    await actor.type(screen.getByLabelText('Нотатки', { exact: true }), 'Тест');
    expect(zone).toHaveValue('Europe/Warsaw');
    view.unmount();

    render(<MemoryRouter initialEntries={[`/ai-notes?draftId=${draftId}`]}><AINotesPage /></MemoryRouter>);
    await waitFor(() => expect(screen.getByLabelText('Часовий пояс', { exact: true })).toHaveValue('Europe/Kyiv'));
    expect(await screen.findByText(/Часовий пояс нотаток: Europe\/Kyiv/)).toBeInTheDocument();
    expect(draft.timeZoneId).toBe('Europe/Kiev');
  });
});
