import { Link } from 'react-router-dom';
import { Empty, ErrorNotice, Loading, Pagination } from '../../components/Feedback';
import { formatDate } from '../../lib/dates';
import { useSession } from '../../lib/session';
import { useNotifications } from './context';
import { copy as m } from './copy';

export function InboxPage() {
  const session = useSession(); const inbox = useNotifications();
  if (!inbox) throw new Error('NotificationsProvider is required.');
  if (!session.user) return <p className="alert">{m.anonymous}</p>;
  const data = inbox.data; const knownCounts = data && !inbox.loading && !inbox.error;
  return <>
    <header className="page-header"><div><p className="eyebrow">{m.eyebrow}</p><h1>{m.title}</h1><p>{m.subtitle}</p></div>
      <button className="button secondary" disabled={inbox.loading} onClick={() => void inbox.reload()}>{m.refresh}</button></header>
    <div className="notification-toolbar"><label className="checkbox"><input type="checkbox" checked={inbox.unreadOnly} disabled={inbox.loading} onChange={event => inbox.setUnreadOnly(event.target.checked)} />{m.unreadOnly}</label>
      <p className="muted">{m.total}: {knownCounts ? data.totalCount : '—'} · {m.unreadCount}: {knownCounts ? data.unreadCount : '—'}</p></div>
    <p className={`notification-connection ${inbox.connection === 'offline' ? 'alert alert-warning' : 'muted'}`} role="status">{m[inbox.connection]}</p>
    {inbox.connection === 'offline' && <button className="button secondary" disabled={inbox.loading} onClick={() => void inbox.reconnect()}>{m.reconnect}</button>}
    {Boolean(inbox.error) && <ErrorNotice error={inbox.error} retry={() => void inbox.reload()} />}
    {inbox.loading && <Loading />}
    {data && Boolean(inbox.error) && <p className="muted">{m.cached}</p>}
    {!inbox.loading && !inbox.error && !data?.items.length && <Empty title={inbox.unreadOnly ? m.emptyUnread : m.empty}>
      <p>{inbox.unreadOnly ? m.emptyUnreadHelp : m.emptyHelp}</p><Link to="/tasks">{m.tasks}</Link></Empty>}
    {!!data?.items.length && <ul className="notification-list" aria-label={m.title}>{data.items.map(item => <li className={`panel notification-row${item.readAtUtc ? '' : ' unread'}`} key={item.id}>
      <div className="notification-copy"><span className="badge">{m.kinds[item.kind]}</span><h2><Link to={`/tasks/${item.taskId}`}>{item.taskName}</Link></h2>
        <p className="muted">{m.project}: <Link to={`/projects/${item.projectId}`}>{item.projectName}</Link></p>
        <p className="notification-meta"><time dateTime={item.occurredAtUtc}>{formatDate(item.occurredAtUtc)}</time> · <span>{item.readAtUtc ? m.read : m.unread}</span></p></div>
      {!item.readAtUtc && <button className="button secondary" aria-label={`${m.mark}: ${item.taskName}`} disabled={inbox.pendingRead.has(item.id)} onClick={() => void inbox.markRead(item.id)}>{m.mark}</button>}
    </li>)}</ul>}
    {data && !inbox.loading && !inbox.error && <Pagination page={data.page} pageSize={data.pageSize} total={inbox.unreadOnly ? data.unreadCount : data.totalCount} change={inbox.setPage} />}
  </>;
}

export function NotificationSummary() {
  const inbox = useNotifications(); const count = inbox?.data && !inbox.loading && !inbox.error ? inbox.data.unreadCount : '—';
  return <div className="stat"><p>{m.unreadSummary}</p><strong>{count}</strong><small>{m.unreadCaption}</small><Link to="/notifications">{m.title} →</Link></div>;
}
