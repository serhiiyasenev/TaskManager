import { useEffect, useState } from 'react';
import { Link, Navigate, NavLink, Outlet, Route, Routes, useLocation } from 'react-router-dom';
import { useSession } from './lib/session';
import { ErrorNotice, Loading } from './components/Feedback';
import { ConfirmEmailPage, ForgotPasswordPage, InvitationPage, LoginPage, ProfilePage, RegisterPage, ResetPasswordPage } from './features/auth/AuthPages';
import { DashboardPage } from './features/work/DashboardPage';
import { TasksPage, TaskDetailPage, TaskFormPage } from './features/work/TasksPages';
import { ProjectDetailPage, ProjectFormPage, ProjectsPage } from './features/work/ProjectsPages';
import { TeamDetailPage, TeamsPage } from './features/teams/TeamsPages';
import { AINotesPage } from './features/ai/AINotesPage';

function Workspace() {
  const session = useSession();
  const location = useLocation();
  const [logoutError, setLogoutError] = useState<unknown>();
  const [signingOut, setSigningOut] = useState(false);
  useEffect(() => { document.getElementById('main-content')?.focus(); }, [location.pathname]);
  if (session.status === 'loading') return <div className="auth-shell"><Loading /></div>;
  if (session.status === 'error') return <div className="auth-shell"><div className="auth-panel"><h1>З’єднання перервано</h1><ErrorNotice error={null} retry={() => void session.refresh()} /></div></div>;
  if (!session.user) return <Navigate replace to={`/login?returnTo=${encodeURIComponent(location.pathname + location.search)}${session.expired ? '&expired=1' : ''}`} />;
  const name = `${session.user.firstName} ${session.user.lastName}`.trim();
  async function logout() {
    setSigningOut(true); setLogoutError(undefined);
    try { await session.logout(); } catch (error) { setLogoutError(error); }
    finally { setSigningOut(false); }
  }
  return <div className="app-shell">
    <a className="skip-link" href="#main-content">Перейти до вмісту</a>
    <aside className="sidebar"><Link className="brand" to="/"><span className="brand-mark" aria-hidden="true">✓</span>TaskManager</Link>
      <div><p className="sidebar-label">Робочий простір</p><nav aria-label="Головна навігація">
        <NavLink to="/" end><span aria-hidden="true">▦</span>Огляд</NavLink>
        <NavLink to="/tasks"><span aria-hidden="true">☷</span>Завдання</NavLink>
        <NavLink to="/projects"><span aria-hidden="true">▱</span>Проєкти</NavLink>
        <NavLink to="/teams"><span aria-hidden="true">♧</span>Команда</NavLink>
        <NavLink to="/ai-notes"><span aria-hidden="true">✦</span>AI нотатки</NavLink>
      </nav></div><div className="sidebar-footer">Ваші завдання.<br />Спільний результат.</div></aside>
    <div className="workspace"><header className="topbar"><span className="topbar-path">Робочий простір / TaskManager</span><div className="user-menu">
      <Link to="/profile" className="actions"><span className="avatar" aria-hidden="true">{name.split(' ').map(part => part[0]).join('').slice(0, 2)}</span>{name || session.user.userName}</Link>
      <button className="button secondary compact" disabled={signingOut} onClick={() => void logout()}>{signingOut ? 'Вихід…' : 'Вийти'}</button></div></header>
      <main className="page" id="main-content" tabIndex={-1}>{!!logoutError && <ErrorNotice error={logoutError} />}<Outlet /></main></div>
  </div>;
}
export function App() {
  return <Routes>
    <Route element={<main className="auth-shell"><Outlet /></main>}>
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
      <Route path="/ai-notes" element={<AINotesPage />} />
      <Route path="*" element={<><h1>Сторінку не знайдено</h1><Link to="/">Повернутися до огляду</Link></>} />
    </Route>
  </Routes>;
}
