import { useCallback, useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { ApiError, request, sessionExpiredEvent } from './api';
import { SessionSchema } from './contracts';
import type { SessionProfile } from './contracts';
import { SessionContext } from './session';
import type { SessionContextValue } from './session';

export function SessionProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<SessionProfile | null>(null);
  const [status, setStatus] = useState<SessionContextValue['status']>('loading');
  const refresh = useCallback(async () => {
    try { const profile = await request('/api/auth/me', SessionSchema); setUser(profile); setStatus('authenticated'); }
    catch (error) { setUser(null); setStatus(error instanceof ApiError && error.status === 401 ? 'anonymous' : 'error'); }
  }, []);
  const login = useCallback(async (email: string, password: string) => {
    const profile = await request('/api/auth/login', SessionSchema, { method: 'POST', body: { email, password } });
    setUser(profile); setStatus('authenticated');
  }, []);
  const logout = useCallback(async () => {
    try { await request('/api/auth/logout', undefined, { method: 'POST' }); }
    catch (error) { if (!(error instanceof ApiError && error.status === 401)) throw error; }
    setUser(null); setStatus('anonymous');
  }, []);
  useEffect(() => {
    let active = true;
    const expired = () => { if (active) { setUser(null); setStatus('anonymous'); } };
    window.addEventListener(sessionExpiredEvent, expired);
    void refresh();
    return () => { active = false; window.removeEventListener(sessionExpiredEvent, expired); };
  }, [refresh]);
  return <SessionContext.Provider value={{ user, status, refresh, login, logout }}>{children}</SessionContext.Provider>;
}
