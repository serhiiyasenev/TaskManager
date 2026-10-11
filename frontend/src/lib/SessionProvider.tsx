import { useCallback, useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { ApiError, request, sessionExpiredEvent } from './api';
import { SessionSchema } from './contracts';
import type { SessionProfile } from './contracts';
import { SessionContext } from './session';
import type { SessionContextValue } from './session';

export function SessionProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<SessionProfile | null>(null);
  const [status, setStatus] = useState<SessionContextValue['status']>('loading');
  const generation = useRef(0);
  const refresh = useCallback(async () => {
    const current = ++generation.current;
    try {
      const profile = await request('/api/auth/me', SessionSchema);
      if (current === generation.current) { setUser(profile); setStatus('authenticated'); }
    } catch (error) {
      if (current === generation.current) { setUser(null); setStatus(error instanceof ApiError && error.status === 401 ? 'anonymous' : 'error'); }
    }
  }, []);
  const login = useCallback(async (email: string, password: string) => {
    const current = ++generation.current;
    const profile = await request('/api/auth/login', SessionSchema, { method: 'POST', body: { email, password } });
    if (current === generation.current) { setUser(profile); setStatus('authenticated'); }
  }, []);
  const logout = useCallback(async () => {
    const current = ++generation.current;
    try { await request('/api/auth/logout', undefined, { method: 'POST' }); }
    catch (error) { if (!(error instanceof ApiError && error.status === 401)) throw error; }
    if (current === generation.current) { setUser(null); setStatus('anonymous'); }
  }, []);
  useEffect(() => {
    let active = true;
    const expired = () => { if (active) { ++generation.current; setUser(null); setStatus('anonymous'); } };
    window.addEventListener(sessionExpiredEvent, expired);
    void refresh();
    return () => { active = false; ++generation.current; window.removeEventListener(sessionExpiredEvent, expired); };
  }, [refresh]);
  return <SessionContext.Provider value={{ user, status, refresh, login, logout }}>{children}</SessionContext.Provider>;
}
