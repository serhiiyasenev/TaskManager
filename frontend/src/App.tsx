import { useEffect, useRef, useState } from 'react';
import { Link, Navigate, NavLink, Outlet, Route, Routes, useLocation } from 'react-router-dom';
import { useSession } from './lib/session';
import { ErrorNotice, Loading } from './components/Feedback';
import { ConfirmEmailPage, ForgotPasswordPage, InvitationPage, LoginPage, ProfilePage, RegisterPage, ResetPasswordPage } from './features/auth/AuthPages';
import { DashboardPage } from './features/work/DashboardPage';
import { TasksPage, TaskDetailPage, TaskFormPage } from './features/work/TasksPages';
import { ProjectDetailPage, ProjectFormPage, ProjectsPage } from './features/work/ProjectsPages';
import { TeamDetailPage, TeamsPage } from './features/teams/TeamsPages';
import { AINotesPage } from './features/ai/AINotesPage';
import { UsersPage } from './features/admin/UsersPage';
import { NotificationsProvider } from './features/notifications/Provider';
import { InboxPage } from './features/notifications/InboxPage';
import { useNotifications } from './features/notifications/context';
import { LanguageSwitcher } from './components/LanguageSwitcher';
import { Icon } from './components/Icon';
import { commonCopy } from './lib/commonCopy';
import { useLocale } from './lib/i18n';

function Workspace() {
  const { locale } = useLocale(); const m = commonCopy[locale];
  const session = useSession();
  const notifications = useNotifications();
  const location = useLocation();
  const [logoutError, setLogoutError] = useState<unknown>();
  const [signingOut, setSigningOut] = useState(false);
  const navigation = useRef<HTMLElement>(null);
  const navigationScope = session.user ? `${session.user.id}:${session.user.roles.join(',')}` : '';
  const navigationBadge = notifications?.data && !notifications.loading && !notifications.error ? notifications.data.unreadCount : 0;
  useEffect(() => { document.getElementById('main-content')?.focus(); }, [location.pathname]);
  useEffect(() => {
    function revealActive() {
      const menu = navigation.current;
      const active = menu?.querySelector<HTMLElement>('[aria-current="page"]');
      if (!menu || !active || menu.scrollWidth <= menu.clientWidth) return;
      const bounds = menu.getBoundingClientRect(); const item = active.getBoundingClientRect();
      if (item.left < bounds.left + 8) menu.scrollLeft -= bounds.left + 8 - item.left;
      else if (item.right > bounds.right - 26) menu.scrollLeft += item.right - bounds.right + 26;
    }
    revealActive();
    window.addEventListener('resize', revealActive, { passive: true });
    return () => window.removeEventListener('resize', revealActive);
  }, [location.pathname, locale, navigationScope, navigationBadge]);
  if (session.status === 'loading') return <div className="auth-shell"><Loading /></div>;
  if (session.status === 'error') return <div className="auth-shell"><div className="auth-panel"><LanguageSwitcher /><h1>{m.connectionLost}</h1><ErrorNotice error={null} retry={() => void session.refresh()} /></div></div>;
  if (!session.user) return <Navigate replace to={`/login?returnTo=${encodeURIComponent(location.pathname + location.search)}${session.expired ? '&expired=1' : ''}`} />;
  const isAdmin = session.user.roles.some(role => role.toLowerCase() === 'admin');
  const scopeKey = JSON.stringify([session.user.id, session.user.teamId,
    [...new Set(session.user.roles.map(role => role.toLowerCase()))].sort()]);
  const name = `${session.user.firstName} ${session.user.lastName}`.trim();
  const displayName = name || session.user.userName || m.profile;
  const pageName = location.pathname.startsWith('/tasks') ? m.tasks
    : location.pathname.startsWith('/projects') ? m.projects
    : location.pathname.startsWith('/teams') ? (isAdmin ? m.teams : m.team)
    : location.pathname.startsWith('/users') ? m.users
    : location.pathname.startsWith('/ai-notes') ? m.ai
    : location.pathname.startsWith('/notifications') ? m.notifications
    : location.pathname.startsWith('/profile') ? m.profile : m.overview;
  async function logout() {
    setSigningOut(true); setLogoutError(undefined);
    try { await session.logout(); } catch (error) { setLogoutError(error); }
    finally { setSigningOut(false); }
  }
  return <div className="app-shell">
    <a className="skip-link" href="#main-content">{m.skip}</a>
    <aside className="sidebar"><Link className="brand" to="/"><span className="brand-mark"><Icon name="check" /></span>TaskManager<span className="brand-cursor" aria-hidden="true">_</span></Link>
      <div className="sidebar-navigation"><p className="sidebar-label">{m.workspace}</p><nav ref={navigation} aria-label={m.navigation}>
        <NavLink to="/" end><Icon name="overview" />{m.overview}</NavLink>
        <NavLink to="/tasks"><Icon name="tasks" />{m.tasks}</NavLink>
        <NavLink to="/projects"><Icon name="projects" />{m.projects}</NavLink>
        <NavLink to="/teams"><Icon name="team" />{isAdmin ? m.teams : m.team}</NavLink>
        {isAdmin && <NavLink to="/users"><Icon name="users" />{m.users}</NavLink>}
        <NavLink to="/ai-notes"><Icon name="sparkles" />{m.ai}</NavLink>
        <NavLink to="/notifications"><Icon name="bell" />{m.notifications}{navigationBadge > 0 && <span className="badge notification-count">{navigationBadge}</span>}</NavLink>
      </nav></div><div className="sidebar-footer"><LanguageSwitcher /><p className="sidebar-tagline">{m.footerFirst}<br />{m.footerSecond}</p></div></aside>
    <div className="workspace"><header className="topbar"><div className="topbar-path"><span>{m.workspace}</span><span className="path-divider" aria-hidden="true">/</span><strong>{pageName}</strong></div><div className="user-menu">
      <Link to="/profile" className="profile-link" aria-label={displayName} title={displayName}><span className="avatar" aria-hidden="true">{displayName.split(' ').map(part => part[0]).join('').slice(0, 2)}</span><span className="user-name">{displayName}</span></Link>
      <button className="button secondary compact" disabled={signingOut} onClick={() => void logout()}><Icon name="logout" />{signingOut ? m.loggingOut : m.logout}</button></div></header>
      <main className="page" id="main-content" tabIndex={-1}>{!!logoutError && <ErrorNotice error={logoutError} />}<Outlet key={scopeKey} /></main></div>
  </div>;
}
export function App() {
  const { locale } = useLocale(); const m = commonCopy[locale];
  useEffect(() => { document.title = m.documentTitle; }, [m.documentTitle]);
  return <NotificationsProvider><Routes>
    <Route element={<main className="auth-shell"><div className="auth-language"><LanguageSwitcher /></div><Outlet /></main>}>
      <Route path="/login" element={<LoginPage />} /><Route path="/register" element={<RegisterPage />} />
      <Route path="/confirm-email" element={<ConfirmEmailPage />} /><Route path="/forgot-password" element={<ForgotPasswordPage />} />
      <Route path="/reset-password" element={<ResetPasswordPage />} /><Route path="/invite" element={<InvitationPage />} />
    </Route>
    <Route element={<Workspace />}>
      <Route index element={<DashboardPage />} /><Route path="/profile" element={<ProfilePage />} />
      <Route path="/tasks" element={<TasksPage />} /><Route path="/tasks/new" element={<TaskFormPage />} />
      <Route path="/tasks/:id" element={<TaskDetailPage />} /><Route path="/tasks/:id/edit" element={<TaskFormPage />} />
      <Route path="/projects" element={<ProjectsPage />} /><Route path="/projects/new" element={<ProjectFormPage />} />
      <Route path="/projects/:id" element={<ProjectDetailPage />} /><Route path="/projects/:id/edit" element={<ProjectFormPage />} />
      <Route path="/teams" element={<TeamsPage />} /><Route path="/teams/:id" element={<TeamDetailPage />} />
      <Route path="/users" element={<UsersPage />} />
      <Route path="/ai-notes" element={<AINotesPage />} />
      <Route path="/notifications" element={<InboxPage />} />
      <Route path="*" element={<><h1>{m.missingPage}</h1><Link to="/">{m.returnOverview}</Link></>} />
    </Route>
  </Routes></NotificationsProvider>;
}
