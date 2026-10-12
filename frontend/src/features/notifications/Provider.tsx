import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import type { HubConnection } from '@microsoft/signalr';
import { ApiError, request } from '../../lib/api';
import { NotificationInboxItemSchema, NotificationInboxPageSchema, NotificationHintSchema } from '../../lib/generated/notifications';
import type { NotificationInboxPage } from '../../lib/generated/notifications';
import { useSession } from '../../lib/session';
import { NotificationsContext } from './context';
import type { ConnectionStatus } from './context';

interface Snapshot { scope: string; query: string; data: NotificationInboxPage | null; loading: boolean; error: unknown }
const PAGE_SIZE = 20;
const HINT_LIMIT = 256;
const INITIAL_RETRIES = [2_000, 10_000, 30_000];

export function NotificationsProvider({ children }: { children: ReactNode }) {
  const session = useSession();
  const scope = session.status === 'authenticated' && session.user
    ? JSON.stringify([session.user.id, session.user.teamId, [...new Set(session.user.roles.map(role => role.toLowerCase()))].sort()]) : 'anonymous';
  const [filter, setFilter] = useState({ scope, page: 1, unreadOnly: false });
  const page = filter.scope === scope ? filter.page : 1;
  const unreadOnly = filter.scope === scope ? filter.unreadOnly : false;
  const query = `/api/notifications?page=${page}&pageSize=${PAGE_SIZE}&unreadOnly=${unreadOnly}`;
  const [snapshot, setSnapshot] = useState<Snapshot>({ scope, query, data: null, loading: scope !== 'anonymous', error: undefined });
  const [connectionState, setConnectionState] = useState<{ scope: string; status: ConnectionStatus }>({ scope, status: scope === 'anonymous' ? 'offline' : 'connecting' });
  const [pendingRead, setPendingRead] = useState<Set<string>>(new Set());
  const scopeRef = useRef(scope); const epoch = useRef(0); const loadSequence = useRef(0);
  const blocked = useRef(false); const readIds = useRef(new Set<string>());
  const loadController = useRef<AbortController | null>(null); const readControllers = useRef(new Set<AbortController>());
  const connection = useRef<HubConnection | null>(null); const retryTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const sessionRefresh = useRef(session.refresh); const latestReload = useRef<() => Promise<boolean>>(async () => false);
  const manualStart = useRef<() => Promise<void>>(async () => {});
  const manualReconnectPending = useRef(false);
  const current = useCallback((key: string, generation: number) => scopeRef.current === key && epoch.current === generation, []);
  useLayoutEffect(() => { sessionRefresh.current = session.refresh; }, [session.refresh]);
  useLayoutEffect(() => {
    scopeRef.current = scope; epoch.current++; blocked.current = false; manualReconnectPending.current = false;
    loadController.current?.abort(); for (const controller of readControllers.current) controller.abort();
    readControllers.current.clear(); readIds.current.clear(); setPendingRead(new Set());
    setSnapshot({ scope, query: `/api/notifications?page=1&pageSize=${PAGE_SIZE}&unreadOnly=false`, data: null, loading: scope !== 'anonymous', error: undefined });
    setConnectionState({ scope, status: scope === 'anonymous' ? 'offline' : 'connecting' });
  }, [scope]);

  const failure = useCallback(async (error: unknown, generation: number) => {
    if (!current(scope, generation)) return;
    const denied = error instanceof ApiError && (error.status === 401 || error.status === 403);
    setSnapshot(previous => ({ ...previous, loading: false, error, data: denied ? null : previous.data }));
    if (denied) {
      blocked.current = true;
      if (retryTimer.current) clearTimeout(retryTimer.current);
      setConnectionState({ scope, status: 'offline' });
      void connection.current?.stop().catch(() => {});
      await sessionRefresh.current();
    }
  }, [current, scope]);

  const reload = useCallback(async () => {
    if (scope === 'anonymous') return false;
    const generation = epoch.current; const sequence = ++loadSequence.current;
    loadController.current?.abort(); const controller = new AbortController(); loadController.current = controller;
    setSnapshot(previous => ({ scope, query, data: previous.scope === scope && previous.query === query ? previous.data : null, loading: true, error: undefined }));
    try {
      const value = await request(query, NotificationInboxPageSchema, { signal: controller.signal });
      if (controller.signal.aborted || !current(scope, generation) || sequence !== loadSequence.current) return false;
      const last = Math.max(1, Math.ceil((unreadOnly ? value.unreadCount : value.totalCount) / PAGE_SIZE));
      if (page > last) { setFilter({ scope, page: last, unreadOnly }); return true; }
      setSnapshot({ scope, query, data: value, loading: false, error: undefined });
      return true;
    } catch (error) {
      if (!controller.signal.aborted && current(scope, generation) && sequence === loadSequence.current) await failure(error, generation);
      return false;
    }
  }, [current, failure, page, query, scope, unreadOnly]);
  useLayoutEffect(() => { latestReload.current = reload; }, [reload]);
  // Canonical reads can verify current permissions even while live hints are
  // blocked; they never reuse private rows from a different scope or query.
  useEffect(() => { void reload(); return () => loadController.current?.abort(); }, [reload]);

  useEffect(() => {
    if (scope === 'anonymous') return;
    const generation = epoch.current; let alive = true; let starting = false; let attempts = 0;
    const hints = new Set<string>();
    const hub = new HubConnectionBuilder().withUrl('/chathub', { withCredentials: true }).withAutomaticReconnect([0, 2_000, 10_000, 30_000]).configureLogging(LogLevel.None).build();
    connection.current = hub;
    const allowed = () => alive && current(scope, generation) && !blocked.current;
    const hint = (payload: unknown) => {
      if (!allowed()) return;
      const parsed = NotificationHintSchema.safeParse(payload);
      if (!parsed.success || hints.has(parsed.data.notificationId)) return;
      if (hints.size >= HINT_LIMIT) hints.delete(hints.values().next().value!);
      hints.add(parsed.data.notificationId); void latestReload.current();
    };
    hub.on('InboxChanged', hint);
    hub.onreconnecting(() => { if (allowed()) setConnectionState({ scope, status: 'reconnecting' }); });
    hub.onreconnected(() => {
      if (!allowed()) return;
      hints.clear(); setConnectionState({ scope, status: 'connected' }); void latestReload.current();
    });
    hub.onclose(() => {
      if (!allowed()) return;
      setConnectionState({ scope, status: 'offline' }); void latestReload.current();
    });
    const start = async () => {
      if (!allowed() || starting) return;
      starting = true; setConnectionState({ scope, status: 'connecting' });
      try {
        await hub.start();
        if (allowed()) { hints.clear(); setConnectionState({ scope, status: 'connected' }); await latestReload.current(); }
      } catch {
        if (!allowed()) return;
        const delay = INITIAL_RETRIES[attempts++];
        if (delay === undefined) setConnectionState({ scope, status: 'offline' });
        else retryTimer.current = setTimeout(() => { retryTimer.current = null; void start(); }, delay);
      } finally { starting = false; }
    };
    manualStart.current = async () => { if (retryTimer.current) clearTimeout(retryTimer.current); retryTimer.current = null; attempts = 0; await start(); };
    void start();
    return () => {
      alive = false; hints.clear(); if (retryTimer.current) clearTimeout(retryTimer.current); retryTimer.current = null;
      hub.off('InboxChanged', hint); void hub.stop().catch(() => {});
      if (connection.current === hub) connection.current = null;
    };
  }, [current, scope]);
  useEffect(() => () => {
    epoch.current++; loadController.current?.abort(); for (const controller of readControllers.current) controller.abort();
    readControllers.current.clear(); readIds.current.clear();
  }, []);

  const visible = snapshot.scope === scope && snapshot.query === query ? snapshot : { scope, query, data: null, loading: scope !== 'anonymous', error: undefined };
  const reconnect = async () => {
    if (scope === 'anonymous' || manualReconnectPending.current) return;
    const generation = epoch.current; manualReconnectPending.current = true;
    try {
      await connection.current?.stop().catch(() => {});
      if (!current(scope, generation)) return;
      const verified = await latestReload.current();
      if (!verified || !current(scope, generation)) return;
      blocked.current = false;
      await manualStart.current();
    } finally { if (current(scope, generation)) manualReconnectPending.current = false; }
  };
  const markRead = async (id: string) => {
    if (scope === 'anonymous' || blocked.current || readIds.current.has(id) || !visible.data?.items.some(value => value.id === id && !value.readAtUtc)) return;
    const generation = epoch.current; const controller = new AbortController(); readControllers.current.add(controller);
    readIds.current.add(id); setPendingRead(new Set(readIds.current));
    try {
      const value = await request(`/api/notifications/${id}/read`, NotificationInboxItemSchema, { method: 'PUT', signal: controller.signal });
      if (controller.signal.aborted || !current(scope, generation)) return;
      if (value.id !== id || value.messageId !== id) throw new ApiError(502);
      setSnapshot(previous => ({ ...previous, data: previous.data ? { ...previous.data, items: previous.query.endsWith('unreadOnly=true')
        ? previous.data.items.filter(item => item.id !== id) : previous.data.items.map(item => item.id === id ? value : item) } : null }));
      await latestReload.current();
    } catch (error) {
      if (!controller.signal.aborted && current(scope, generation)) {
        if (error instanceof ApiError && error.status === 404) { setSnapshot(previous => ({ ...previous, data: null })); await latestReload.current(); }
        else await failure(error, generation);
      }
    } finally {
      readControllers.current.delete(controller);
      if (current(scope, generation)) { readIds.current.delete(id); setPendingRead(new Set(readIds.current)); }
    }
  };
  return <NotificationsContext.Provider value={{ data: visible.data, loading: visible.loading, error: visible.error,
    connection: connectionState.scope === scope ? connectionState.status : scope === 'anonymous' ? 'offline' : 'connecting',
    page, unreadOnly, pendingRead, setPage: value => setFilter({ scope, page: Math.min(100_000, Math.max(1, value)), unreadOnly }),
    setUnreadOnly: value => setFilter({ scope, page: 1, unreadOnly: value }),
    reload: async () => { const generation = epoch.current; if (await latestReload.current() && current(scope, generation)) blocked.current = false; }, reconnect, markRead }}>{children}</NotificationsContext.Provider>;
}
