import type { Page, Route, WebSocketRoute } from '@playwright/test';
import { NotificationInboxItemSchema, NotificationInboxPageSchema, NotificationHintSchema } from '../src/lib/generated/notifications';
import type { NotificationInboxItem } from '../src/lib/generated/notifications';
import { longTaskName } from './syntheticApi';

// Explicitly synthetic HTTP and SignalR protocol fixtures. The real React App
// and installed client SDK run; no Identity, SQL, broker or Azure service runs.
export function inboxFixtureItem(index = 1, long = false): NotificationInboxItem {
  const id = `00000000-0000-4000-8000-${index.toString(16).padStart(12, '0')}`;
  return NotificationInboxItemSchema.parse({ id, messageId: id, kind: (index - 1) % 4, taskId: 100 + index, projectId: 71,
    taskName: index === 1 && long ? longTaskName : `Перевірка сповіщення ${index}`,
    projectName: 'Команда узгодження доступних завдань', occurredAtUtc: '2026-10-12T09:00:00Z' });
}
export interface InboxFixtureOptions {
  count?: number; longNames?: boolean; holdLoad?: boolean; holdRead?: boolean;
  inboxFailure?: number; readFailure?: number; negotiateFailure?: number;
}
export async function syntheticInboxApi(page: Page, options: InboxFixtureOptions = {}) {
  const settings = { ...options };
  let items = Array.from({ length: options.count ?? 4 }, (_, index) => inboxFixtureItem(index + 1, options.longNames));
  const captured: Array<{ path: string; method: string; body?: unknown; csrf?: string }> = [];
  const sockets = new Set<WebSocketRoute>();
  let negotiations = 0; let handshakes = 0; let closes = 0;
  let releaseLoad: () => void = () => {}; let releaseRead: () => void = () => {};
  const loadGate = new Promise<void>(resolve => { releaseLoad = resolve; });
  const readGate = new Promise<void>(resolve => { releaseRead = resolve; });
  const json = (route: Route, value: unknown) => route.fulfill({ status: 200, contentType: 'application/json', headers: { 'Cache-Control': 'no-store' }, body: JSON.stringify(value) });
  const problem = (route: Route, status: number) => route.fulfill({ status, contentType: 'application/problem+json', headers: { 'X-Correlation-ID': 'synthetic-inbox-trace' },
    body: JSON.stringify({ status, title: 'Controlled synthetic inbox outcome', traceId: 'synthetic-inbox-trace' }) });
  await page.route('**/api/auth/me', route => settings.inboxFailure === 401 || settings.readFailure === 401 ? problem(route, 401) : route.fallback());
  await page.route('**/api/notifications**', async route => {
    const incoming = route.request(); const url = new URL(incoming.url()); const headers = await incoming.allHeaders();
    captured.push({ path: url.pathname + url.search, method: incoming.method(), body: incoming.postData() ? incoming.postDataJSON() : undefined, csrf: headers['x-csrf-token'] });
    if (url.pathname === '/api/notifications') {
      if (settings.holdLoad) await loadGate;
      if (settings.inboxFailure) return problem(route, settings.inboxFailure);
      const number = Number(url.searchParams.get('page') ?? 1); const pageSize = Number(url.searchParams.get('pageSize') ?? 20);
      const visible = url.searchParams.get('unreadOnly') === 'true' ? items.filter(item => !item.readAtUtc) : items;
      return json(route, NotificationInboxPageSchema.parse({ items: visible.slice((number - 1) * pageSize, number * pageSize), page: number, pageSize,
        totalCount: items.length, unreadCount: items.filter(item => !item.readAtUtc).length }));
    }
    const id = url.pathname.split('/').at(-2); const item = items.find(value => value.id === id);
    if (!url.pathname.endsWith('/read') || incoming.method() !== 'PUT' || !item) return problem(route, 404);
    if (settings.holdRead) await readGate;
    if (settings.readFailure) return problem(route, settings.readFailure);
    item.readAtUtc ??= '2026-10-12T10:00:00Z'; return json(route, NotificationInboxItemSchema.parse(item));
  });
  await page.route('**/chathub/negotiate**', async route => {
    negotiations++;
    if (settings.negotiateFailure) return problem(route, settings.negotiateFailure);
    return json(route, { negotiateVersion: 1, connectionId: `synthetic-${negotiations}`, connectionToken: `synthetic-${negotiations}`,
      availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text'] }] });
  });
  await page.routeWebSocket(url => url.pathname === '/chathub', socket => {
    sockets.add(socket);
    socket.onMessage(message => {
      const text = typeof message === 'string' ? message : message.toString('utf8');
      for (const raw of text.split('\u001e').filter(Boolean)) {
        const frame = JSON.parse(raw) as { protocol?: string; type?: number };
        if (frame.protocol === 'json') { handshakes++; socket.send('{}\u001e'); }
        else if (frame.type === 6) socket.send('{"type":6}\u001e');
      }
    });
    socket.onClose(async (code, reason) => { sockets.delete(socket); closes++; await socket.close({ code: code ?? 1000, reason: reason ?? '' }); });
  });
  return {
    settings, captured, stats: () => ({ negotiations, handshakes, closes, open: sockets.size }),
    releaseLoad: () => { settings.holdLoad = false; releaseLoad(); }, releaseRead: () => { settings.holdRead = false; releaseRead(); },
    add: (item: NotificationInboxItem) => { items = [NotificationInboxItemSchema.parse(item), ...items]; },
    hint: (id: string) => { const payload = NotificationHintSchema.parse({ notificationId: id }); for (const socket of sockets) socket.send(`${JSON.stringify({ type: 1, target: 'InboxChanged', arguments: [payload] })}\u001e`); },
    invalidHint: () => { for (const socket of sockets) socket.send(`${JSON.stringify({ type: 1, target: 'InboxChanged', arguments: [{ notificationId: 'invalid', taskName: 'Untrusted' }] })}\u001e`); },
    disconnect: async () => { const live = [...sockets]; sockets.clear(); await Promise.all(live.map(socket => socket.close({ code: 1001, reason: 'Synthetic disconnect' }))); },
  };
}
