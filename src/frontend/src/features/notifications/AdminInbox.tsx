import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Empty, ErrorNotice, Loading, Pagination } from '../../components/Feedback';
import { ApiError, request } from '../../lib/api';
import { formatDate } from '../../lib/dates';
import { NotificationAdminInboxPageSchema } from '../../lib/generated/notifications';
import { useLocale } from '../../lib/i18n';
import { useSession } from '../../lib/session';
import { useLoad } from '../../lib/useLoad';
import { notificationsCopy } from './copy';

const pageSize = 20;

// Mounted only for the current administrator scope. It never subscribes to
// recipient hubs or writes another user's read marker.
export function AdminInbox() {
  const { locale } = useLocale(); const m = notificationsCopy[locale];
  const { refresh } = useSession();
  const [{ page, unreadOnly }, setFilter] = useState({ page: 1, unreadOnly: false });
  const [blocked, setBlocked] = useState(false);
  const query = `/api/notifications/all?page=${page}&pageSize=${pageSize}&unreadOnly=${unreadOnly}`;
  const inbox = useLoad(query, signal => request(query, NotificationAdminInboxPageSchema, { signal }));
  const denied = inbox.error instanceof ApiError && (inbox.error.status === 401 || inbox.error.status === 403);
  const data = denied || blocked ? undefined : inbox.data;
  const knownCounts = data && !inbox.loading && !inbox.error;
  useEffect(() => { if (denied) void refresh(); }, [denied, refresh]);
  useEffect(() => {
    if (denied) setBlocked(true);
    else if (inbox.data && !inbox.loading && !inbox.error) setBlocked(false);
  }, [denied, inbox.data, inbox.loading, inbox.error]);
  useEffect(() => {
    if (!data || inbox.loading || inbox.error) return;
    const last = Math.max(1, Math.ceil((unreadOnly ? data.unreadCount : data.totalCount) / pageSize));
    if (page > last) setFilter({ page: last, unreadOnly });
  }, [data, inbox.loading, inbox.error, page, unreadOnly]);
  return <>
    <header className="page-header"><div><p className="eyebrow">{m.adminEyebrow}</p><h1>{m.all}</h1><p>{m.adminSubtitle}</p></div>
      <button className="button secondary" disabled={inbox.loading} onClick={() => void inbox.reload()}>{m.refresh}</button></header>
    <div className="notification-toolbar"><label className="checkbox"><input type="checkbox" checked={unreadOnly} disabled={inbox.loading}
      onChange={event => setFilter({ page: 1, unreadOnly: event.target.checked })} />{m.unreadOnly}</label>
      <p className="muted">{m.total}: {knownCounts ? data.totalCount : '—'} · {m.recipientUnreadCount}: {knownCounts ? data.unreadCount : '—'}</p></div>
    <p className="notification-connection muted" role="status">{m.adminHelp}</p>
    {!!inbox.error && <ErrorNotice error={inbox.error} retry={() => void inbox.reload()} />}
    {inbox.loading && <Loading />}
    {data && !!inbox.error && <p className="muted">{m.cached}</p>}
    {!inbox.loading && !inbox.error && !data?.items.length && <Empty title={unreadOnly ? m.emptyUnread : m.empty}>
      <p>{unreadOnly ? m.emptyUnreadHelp : m.emptyHelp}</p><Link to="/tasks">{m.tasks}</Link></Empty>}
    {!!data?.items.length && <ul className="notification-list" aria-label={m.all}>{data.items.map(item => <li className={`panel notification-row${item.readAtUtc ? '' : ' unread'}`} key={item.id}>
      <div className="notification-copy"><span className="badge">{m.kinds[item.kind]}</span><h2><Link to={`/tasks/${item.taskId}`}>{item.taskName}</Link></h2>
        <p>{m.recipient}: <strong>{item.recipientName}</strong> <span className="notification-meta">#{item.recipientId}</span></p>
        <p className="muted">{m.project}: <Link to={`/projects/${item.projectId}`}>{item.projectName}</Link></p>
        <p className="notification-meta"><time dateTime={item.occurredAtUtc}>{formatDate(item.occurredAtUtc)}</time> · <span>{item.readAtUtc ? m.read : m.unread}</span></p></div>
    </li>)}</ul>}
    {data && !inbox.loading && !inbox.error && <Pagination page={data.page} pageSize={data.pageSize}
      total={unreadOnly ? data.unreadCount : data.totalCount} change={next => setFilter({ page: next, unreadOnly })} />}
  </>;
}
