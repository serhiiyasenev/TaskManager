import { createContext, useContext } from 'react';
import type { SessionProfile } from './contracts';

export interface SessionContextValue {
  user: SessionProfile | null;
  status: 'loading' | 'authenticated' | 'anonymous' | 'error';
  expired?: boolean;
  refresh: () => Promise<void>;
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
}
export const SessionContext = createContext<SessionContextValue | null>(null);
export function useSession(): SessionContextValue {
  const context = useContext(SessionContext);
  if (!context) throw new Error('SessionProvider is required.');
  return context;
}
