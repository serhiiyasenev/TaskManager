import type { Page, Route } from '@playwright/test';
import { NotificationInboxPageSchema } from '../src/lib/generated/notifications';

// Rendered UI evidence only: these are deliberately invented actors/data.
// This fixture does not validate SQL, server authorization or Identity cookies.
export const syntheticUser = { id: 42, userName: 'olena', email: 'olena@example.test', firstName: 'Олена', lastName: 'Коваль', teamId: 17, emailConfirmed: true, roles: [] as string[] };
export const longTaskName = 'Перевірити збереження довгої назви завдання та доступність роботи з командою на вузькому екрані';
const versionOne = '81becfd7-915a-4de2-bfdd-96ef156f9962';
export const versionTwo = '69bb239c-42dd-4cc5-9302-7c8212e2908d';
const createdAt = '2026-10-01T12:00:00Z';
const invitationId = 'ebc8a8c0-ae46-4a94-adb9-4de46620c14e';
export const syntheticInvitationToken = 'synthetic-private-invitation-token-1234567890';

export interface CapturedRequest { path: string; method: string; body?: Record<string, unknown>; csrf?: string }
export interface SyntheticOptions {
  anonymous?: boolean; longNames?: boolean; emptyTasks?: boolean;
  taskFailure?: number; projectFailure?: number; conflictOnce?: boolean; invitationForbidden?: boolean; holdTasks?: boolean;
}

export async function syntheticApi(page: Page, options: SyntheticOptions = {}) {
  const settings = { ...options };
  const user = { ...syntheticUser, ...(settings.longNames ? { firstName: 'Олена-Марія', lastName: 'Коваленко-Довгопрізвищенко' } : {}) };
  const member = { ...user, birthDay: '1990-01-01T00:00:00', registeredAt: createdAt };
  const peer = { ...member, id: 43, userName: 'ivan', email: 'ivan@example.test', firstName: 'Іван', lastName: 'Мельник' };
  let team = { id: 17, name: settings.longNames ? 'Команда дослідження довгих назв і перевірки робочих процесів' : 'Команда API', createdAt, ownerId: 42, users: [member, peer] };
  let tasks = [
    { id: 101, name: settings.longNames ? longTaskName : 'Підготувати перевірку доступу', state: 'ToDo', dueDate: '2020-10-01T12:00:00Z' },
    { id: 102, name: 'Перевірити запрошення учасників', state: 'InProgress', dueDate: '2030-10-12T15:00:00Z' },
    { id: 103, name: 'Описати правила проєкту', state: 'Done', dueDate: '2030-10-14T09:00:00Z' },
    { id: 104, name: 'Закрити зайвий експеримент', state: 'Canceled', dueDate: null },
  ].map(task => ({ ...task, projectId: 71, performerId: 42, description: 'Синтетичний опис для перевірки React UI.', createdAt, finishedAt: task.state === 'Done' ? createdAt : null,
    reminderEnabled: task.id === 101, reminderOffsetMinutes: task.id === 101 ? 60 : null,
    escalationEnabled: false, escalationDelayMinutes: null, reminderSentAt: null, escalationSentAt: null,
    performer: member, version: versionOne }));
  let projects = [{ id: 71, name: settings.longNames ? 'Впровадження підтвердження email та командного контролю доступу' : 'Оновлення TaskManager', description: 'Зрозумілі завдання та поточні права учасників.', deadline: '2030-11-01T12:00:00Z', authorId: 42, teamId: 17, createdAt, team, tasks }];
  let authenticated = !settings.anonymous;
  let conflicted = false;
  let csrfNumber = 0;
  const captured: CapturedRequest[] = [];
  const invitations = [{ id: invitationId, email: 'new@example.test', createdAtUtc: createdAt, expiresAtUtc: '2030-10-18T12:00:00Z', acceptedAtUtc: null, revokedAtUtc: null as string | null }];
  let releaseTasks: () => void = () => {};
  const taskGate = new Promise<void>(resolve => { releaseTasks = resolve; });
  const json = (route: Route, value: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value), headers: { 'Cache-Control': 'no-store' } });
  const problem = (route: Route, status: number, trace = 'synthetic-ui-trace') => route.fulfill({ status, contentType: 'application/problem+json', headers: { 'X-Correlation-ID': trace }, body: JSON.stringify({ status, title: 'Synthetic controlled API outcome', traceId: trace }) });

  await page.route('**/api/**', async route => {
    const incoming = route.request(); const url = new URL(incoming.url()); const path = url.pathname;
    const method = incoming.method();
    const body = incoming.postData() ? incoming.postDataJSON() as Record<string, unknown> : undefined;
    const headers = await incoming.allHeaders();
    captured.push({ path: path + url.search, method, body, csrf: headers['x-csrf-token'] });
    if (path === '/api/auth/csrf') return json(route, { requestToken: `synthetic-csrf-${++csrfNumber}` });
    if (path === '/api/auth/me') return authenticated ? json(route, user) : problem(route, 401);
    if (path === '/api/auth/login') { authenticated = true; return json(route, user); }
    if (path === '/api/auth/logout') { authenticated = false; return route.fulfill({ status: 204 }); }
    if (path.startsWith('/api/auth/')) return json(route, { message: 'Synthetic account action completed.' }, 202);
    if (!authenticated) return problem(route, 401);
    if (path === '/api/notifications' && method === 'GET') return json(route, NotificationInboxPageSchema.parse({
      items: [], page: Number(url.searchParams.get('page') ?? 1), pageSize: Number(url.searchParams.get('pageSize') ?? 20), totalCount: 0, unreadCount: 0,
    }));
    if (path === '/api/users/42') return json(route, member);
    if (path === '/api/tasks/page') {
      if (settings.holdTasks) await taskGate;
      if (settings.taskFailure) return problem(route, settings.taskFailure, 'synthetic-task-trace');
      let visible = settings.emptyTasks ? [] : [...tasks];
      if (url.searchParams.get('state')) visible = visible.filter(task => task.state === url.searchParams.get('state'));
      if (url.searchParams.get('search')) visible = visible.filter(task => `${task.name} ${task.description}`.toLowerCase().includes(url.searchParams.get('search')!.toLowerCase()));
      const pageNumber = Number(url.searchParams.get('page') ?? 1); const pageSize = Number(url.searchParams.get('pageSize') ?? 20);
      return json(route, { items: visible.slice((pageNumber - 1) * pageSize, pageNumber * pageSize), page: pageNumber, pageSize, totalCount: visible.length });
    }
    if (path === '/api/tasks' && method === 'POST') {
      const task = { ...tasks[0], ...body, id: 105, version: versionTwo } as typeof tasks[number]; tasks.push(task); return json(route, task);
    }
    if (/^\/api\/tasks\/\d+$/.test(path)) {
      const id = Number(path.split('/').at(-1)); const index = tasks.findIndex(task => task.id === id);
      if (index < 0) return problem(route, 404);
      if (method === 'PUT') {
        if (settings.conflictOnce && !conflicted) { conflicted = true; tasks[index] = { ...tasks[index], name: 'Редагування колеги', version: versionTwo }; return problem(route, 409, 'synthetic-conflict-trace'); }
        tasks[index] = { ...tasks[index], ...body, version: versionTwo } as typeof tasks[number];
      }
      if (method === 'DELETE') { tasks = tasks.filter(task => task.id !== id); return route.fulfill({ status: 204 }); }
      return json(route, tasks[index]);
    }
    if (path === '/api/projects') {
      if (settings.projectFailure) return problem(route, settings.projectFailure, 'synthetic-project-trace');
      if (method === 'POST') { const project = { ...projects[0], ...body, id: 72 } as typeof projects[number]; projects.push(project); return json(route, project); }
      return json(route, projects);
    }
    if (/^\/api\/projects\/\d+$/.test(path)) {
      const id = Number(path.split('/').at(-1)); const index = projects.findIndex(project => project.id === id);
      if (index < 0) return problem(route, 404);
      if (method === 'PUT') projects[index] = { ...projects[index], ...body } as typeof projects[number];
      if (method === 'DELETE') { projects = projects.filter(project => project.id !== id); return route.fulfill({ status: 204 }); }
      return json(route, projects[index]);
    }
    if (path === '/api/teams') return json(route, [team]);
    if (path === '/api/teams/17') return json(route, team);
    if (path === '/api/teams/17/invitations') {
      if (method === 'POST') {
        if (settings.invitationForbidden) { team = { ...team, ownerId: 43 }; return problem(route, 403, 'synthetic-permission-trace'); }
        return json(route, { ...invitations[0], email: body?.email, token: syntheticInvitationToken }, 201);
      }
      return json(route, invitations);
    }
    if (path.startsWith('/api/teams/17/invitations/') && method === 'DELETE') { invitations[0].revokedAtUtc = '2026-10-11T12:00:00Z'; return route.fulfill({ status: 204 }); }
    if (path === '/api/team-invitations/preview') return json(route, { teamName: team.name, expired: false });
    if (path === '/api/team-invitations/accept') return json(route, { teamId: 17 });
    return problem(route, 501, 'unhandled-synthetic-route');
  });
  // Quiet canonical baseline for screens whose assertions do not exercise the
  // inbox. Inbox-specific fixtures registered later override these routes.
  await page.route('**/chathub/negotiate**', route => authenticated ? json(route, {
    negotiateVersion: 1, connectionId: 'synthetic-baseline', connectionToken: 'synthetic-baseline',
    availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text'] }],
  }) : problem(route, 401));
  await page.routeWebSocket(url => url.pathname === '/chathub', socket => {
    socket.onMessage(message => {
      const text = typeof message === 'string' ? message : message.toString('utf8');
      for (const raw of text.split('\u001e').filter(Boolean)) {
        const frame = JSON.parse(raw) as { protocol?: string; type?: number };
        if (frame.protocol === 'json') socket.send('{}\u001e');
        else if (frame.type === 6) socket.send('{"type":6}\u001e');
      }
    });
  });
  return { settings, captured, user, releaseTasks: () => { settings.holdTasks = false; releaseTasks(); } };
}
