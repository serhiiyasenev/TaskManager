import { createContext, useContext } from 'react';
import type { NotificationInboxPage } from '../../lib/generated/notifications';

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'offline';
export interface NotificationsState {
  data: NotificationInboxPage | null;
  loading: boolean;
  error: unknown;
  connection: ConnectionStatus;
  page: number;
  unreadOnly: boolean;
  pendingRead: ReadonlySet<string>;
  setPage: (page: number) => void;
  setUnreadOnly: (value: boolean) => void;
  reload: () => Promise<void>;
  reconnect: () => Promise<void>;
  markRead: (id: string) => Promise<void>;
}
export const NotificationsContext = createContext<NotificationsState | null>(null);
export function useNotifications() { return useContext(NotificationsContext); }
