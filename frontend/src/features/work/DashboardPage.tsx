import { Link } from 'react-router-dom';
import { z } from 'zod';
import { request } from '../../lib/api';
import { ProjectSchema, TaskPageSchema } from '../../lib/contracts';
import { formatDate, parseUtc } from '../../lib/dates';
import { getStateLabels } from '../../lib/domain';
import { useWorkCopy } from './workCopy';
import { useSession } from '../../lib/session';
import { useLoad } from '../../lib/useLoad';
import { Empty, ErrorNotice, Loading } from '../../components/Feedback';
import { NotificationSummary } from '../notifications/InboxPage';

export function DashboardPage() {
  const { user } = useSession();
  const { locale, intlLocale, copy, text } = useWorkCopy();
  const stateLabels = getStateLabels(locale);
  const admin = user!.roles.some(role => role.toLowerCase() === 'admin');
  const scope = `${user!.id}-${user!.teamId ?? 'none'}-${admin ? 'system' : 'personal'}`;
  const performerFilter = admin ? '' : `performerId=${user!.id}&`;
  const tasks = useLoad(`dashboard-tasks-${scope}`, async signal => {
    const [todo, progress] = await Promise.all(['ToDo', 'InProgress'].map(state => request(`/api/tasks/page?${performerFilter}state=${state}&sort=due&direction=asc&pageSize=5`, TaskPageSchema, { signal })));
    return { items: [...todo.items, ...progress.items].sort((a, b) => (a.dueDate ? parseUtc(a.dueDate).getTime() : Infinity) - (b.dueDate ? parseUtc(b.dueDate).getTime() : Infinity)).slice(0, 5), todo: todo.totalCount, progress: progress.totalCount };
  });
  const projects = useLoad(`dashboard-projects-${scope}`, signal => request('/api/projects', z.array(ProjectSchema), { signal }));
  const date = new Intl.DateTimeFormat(intlLocale, { weekday: 'long', day: 'numeric', month: 'long' }).format(new Date());
  const displayDate = date.charAt(0).toLocaleUpperCase(intlLocale) + date.slice(1);
  return <div className="dashboard-page">
    <header className="page-header dashboard-header">
      <div className="dashboard-intro">
        <p className="eyebrow">{admin ? copy.adminOverview : copy.workingDay}</p>
        <h1>{text('welcome', { name: user!.firstName || user!.userName || '' })}</h1>
        <p className="dashboard-subtitle"><span className="dashboard-date">{displayDate}</span>. {admin ? copy.adminIntro : copy.personalIntro}</p>
      </div>
      <div className="actions dashboard-actions">
        {admin && <>
          <Link className="button secondary" to="/teams">{copy.allTeams}</Link>
          <Link className="button secondary" to="/projects">{copy.allProjects}</Link>
        </>}
        <Link className="button" to="/tasks/new">＋ {copy.createTask}</Link>
      </div>
    </header>

    <section className="stats dashboard-stats" aria-label={copy.workOverview}>
      <article className="stat dashboard-stat dashboard-stat-todo" aria-busy={tasks.loading}>
        <p className="stat-label dashboard-stat-label">{copy.todo}</p>
        <strong className="stat-value dashboard-stat-value">{tasks.loading ? '…' : tasks.data?.todo ?? '—'}</strong>
        <small className="stat-caption dashboard-stat-caption">{admin ? copy.allMemberTasks : copy.myTasks}</small>
      </article>
      <article className="stat dashboard-stat dashboard-stat-progress" aria-busy={tasks.loading}>
        <p className="stat-label dashboard-stat-label">{copy.inProgress}</p>
        <strong className="stat-value dashboard-stat-value">{tasks.loading ? '…' : tasks.data?.progress ?? '—'}</strong>
        <small className="stat-caption dashboard-stat-caption">{admin ? copy.allMemberTasks : copy.myTasks}</small>
      </article>
      <article className="stat dashboard-stat dashboard-stat-projects" aria-busy={projects.loading}>
        <p className="stat-label dashboard-stat-label">{admin ? copy.allProjects : copy.availableProjects}</p>
        <strong className="stat-value dashboard-stat-value">{projects.loading ? '…' : projects.data?.length ?? '—'}</strong>
        <small className="stat-caption dashboard-stat-caption">{admin ? copy.allTeamsCaption : copy.teamWorkspace}</small>
      </article>
      <NotificationSummary />
    </section>

    {!user!.teamId && !admin && <section className="alert dashboard-onboarding" aria-labelledby="dashboard-team-heading">
      <div>
        <h2 id="dashboard-team-heading">{copy.createYourTeam}</h2>
        <p>{copy.acceptInviteIntro}</p>
      </div>
      <Link className="button" to="/teams">{copy.goToTeams}</Link>
    </section>}

    <div className="dashboard-grid">
      <section className="panel dashboard-section dashboard-tasks" aria-labelledby="dashboard-tasks-heading">
        <header className="panel-heading dashboard-section-heading">
          <h2 id="dashboard-tasks-heading">{admin ? copy.upcomingSystemTasks : copy.upcomingMyTasks}</h2>
          <Link className="dashboard-section-link" to={`/tasks?${performerFilter}sort=due&direction=asc`}>{copy.allTasks}</Link>
        </header>
        {tasks.loading ? <div className="panel-body dashboard-section-state"><Loading /></div>
          : tasks.error ? <div className="panel-body dashboard-section-state"><ErrorNotice error={tasks.error} retry={() => void tasks.reload()} /></div>
          : !tasks.data?.items.length ? <div className="panel-body dashboard-section-state">
            <Empty title={copy.underControl}>
              <p>{admin ? copy.noSystemTasks : copy.noMyTasks}</p>
              <Link to="/tasks/new">{copy.createTask} →</Link>
            </Empty>
          </div>
          : <ul className="dashboard-list dashboard-task-list">{tasks.data.items.map(task => <li className="task-row dashboard-task-row" data-state={task.state} key={task.id}>
            <span className="task-indicator dashboard-row-indicator" aria-hidden="true" />
            <div className="dashboard-row-main">
              <h3 className="dashboard-row-title"><Link to={`/tasks/${task.id}`}>{task.name}</Link></h3>
              <p className="dashboard-row-meta"><span className={`badge dashboard-task-state ${task.state}`}>{stateLabels[task.state]}</span></p>
            </div>
            <DashboardDeadline value={task.dueDate} overdue={Boolean(task.dueDate && parseUtc(task.dueDate).getTime() < Date.now())} />
          </li>)}</ul>}
      </section>

      <section className="panel dashboard-section dashboard-projects" aria-labelledby="dashboard-projects-heading">
        <header className="panel-heading dashboard-section-heading">
          <h2 id="dashboard-projects-heading">{admin ? copy.systemProjects : copy.teamProjects}</h2>
          <Link className="dashboard-section-link" to="/projects">{copy.all}</Link>
        </header>
        {projects.loading ? <div className="panel-body dashboard-section-state"><Loading /></div>
          : projects.error ? <div className="panel-body dashboard-section-state"><ErrorNotice error={projects.error} retry={() => void projects.reload()} /></div>
          : !projects.data?.length ? <div className="panel-body dashboard-section-state">
            <p className="muted">{copy.noProjects}</p>
            <Link to="/projects/new">{copy.createProject} →</Link>
          </div>
          : <ul className="dashboard-list dashboard-project-list">{projects.data.slice(0, 5).map(project => <li className="task-row dashboard-project-row" key={project.id}>
            <span className="task-indicator dashboard-row-indicator" aria-hidden="true" />
            <div className="dashboard-row-main">
              <h3 className="dashboard-row-title"><Link to={`/projects/${project.id}`}>{project.name}</Link></h3>
              <p className="dashboard-row-meta">{text('taskCount', { count: project.tasks?.length ?? 0 })}</p>
            </div>
            <DashboardDeadline value={project.deadline} />
          </li>)}</ul>}
      </section>
    </div>
  </div>;
}

function DashboardDeadline({ value, overdue = false }: { value: string | null | undefined; overdue?: boolean }) {
  const date = value ? parseUtc(value) : null;
  const iso = date && !Number.isNaN(date.getTime()) ? date.toISOString() : undefined;
  const className = `due dashboard-row-deadline${overdue ? ' overdue' : ''}`;
  return iso ? <time className={className} dateTime={iso}>{formatDate(value)}</time>
    : <span className={className}>{formatDate(value)}</span>;
}
