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
  const authenticated = useRef(false);
  const [expired, setExpired] = useState(false);
  const invalidate = useCallback(() => { ++generation.current; }, []);
  const refresh = useCallback(async () => {
    const current = ++generation.current;
    try {
      const profile = await request('/api/auth/me', SessionSchema);
      if (current === generation.current) { authenticated.current = true; setExpired(false); setUser(profile); setStatus('authenticated'); }
    } catch (error) {
      if (current === generation.current) { setExpired(authenticated.current && error instanceof ApiError && error.status === 401); authenticated.current = false; setUser(null); setStatus(error instanceof ApiError && error.status === 401 ? 'anonymous' : 'error'); }
    }
  }, []);
  const login = useCallback(async (email: string, password: string) => {
    const current = ++generation.current;
    const profile = await request('/api/auth/login', SessionSchema, { method: 'POST', body: { email, password } });
    if (current === generation.current) { authenticated.current = true; setExpired(false); setUser(profile); setStatus('authenticated'); }
  }, []);
  const logout = useCallback(async () => {
    const current = ++generation.current;
    try { await request('/api/auth/logout', undefined, { method: 'POST' }); }
    catch (error) { if (!(error instanceof ApiError && error.status === 401)) throw error; }
    if (current === generation.current) { authenticated.current = false; setExpired(false); setUser(null); setStatus('anonymous'); }
  }, []);
  useEffect(() => {
    let active = true;
    const expire = () => { if (active) { ++generation.current; authenticated.current = false; setExpired(true); setUser(null); setStatus('anonymous'); } };
    window.addEventListener(sessionExpiredEvent, expire);
    void refresh();
    return () => { active = false; invalidate(); window.removeEventListener(sessionExpiredEvent, expire); };
  }, [refresh, invalidate]);
  return <SessionContext.Provider value={{ user, status, expired, refresh, login, logout }}>{children}</SessionContext.Provider>;
}
