import { Link } from 'react-router-dom';
import { z } from 'zod';
import { request } from '../../lib/api';
import { ProjectSchema, TaskPageSchema } from '../../lib/contracts';
import { formatDate, parseUtc } from '../../lib/dates';
import { stateLabels } from '../../lib/domain';
import { useSession } from '../../lib/session';
import { useLoad } from '../../lib/useLoad';
import { Empty, ErrorNotice, Loading } from '../../components/Feedback';

export function DashboardPage() {
  const { user } = useSession();
  const tasks = useLoad(`dashboard-tasks-${user!.id}`, async signal => {
    const [todo, progress] = await Promise.all(['ToDo', 'InProgress'].map(state => request(`/api/tasks/page?performerId=${user!.id}&state=${state}&sort=due&direction=asc&pageSize=5`, TaskPageSchema, { signal })));
    return { items: [...todo.items, ...progress.items].sort((a, b) => (a.dueDate ? parseUtc(a.dueDate).getTime() : Infinity) - (b.dueDate ? parseUtc(b.dueDate).getTime() : Infinity)).slice(0, 5), todo: todo.totalCount, progress: progress.totalCount };
  });
  const projects = useLoad(`dashboard-projects-${user!.id}`, signal => request('/api/projects', z.array(ProjectSchema), { signal }));
  return <><div className="page-header"><div><p className="eyebrow">Ваш робочий день</p><h1>Вітаємо, {user!.firstName || user!.userName}</h1><p>{new Intl.DateTimeFormat('uk-UA', { weekday: 'long', day: 'numeric', month: 'long' }).format(new Date())}. Почнімо з найближчого строку.</p></div><Link className="button" to="/tasks/new">＋ Створити завдання</Link></div>
    <section className="stats" aria-label="Огляд роботи"><div className="stat"><p>До виконання</p><strong>{tasks.loading ? '…' : tasks.data?.todo ?? '—'}</strong><small>мої завдання</small></div><div className="stat"><p>У роботі</p><strong>{tasks.loading ? '…' : tasks.data?.progress ?? '—'}</strong><small>мої завдання</small></div><div className="stat"><p>Доступні проєкти</p><strong>{projects.loading ? '…' : projects.data?.length ?? '—'}</strong><small>командний простір</small></div></section>
    {!user!.teamId && !user!.roles.some(role => role.toLowerCase() === 'admin') && <div className="alert"><h2>Створіть свою команду</h2><p>Або прийміть запрошення від власника команди, щоб розпочати спільну роботу.</p><Link className="button" to="/teams">Перейти до команд</Link></div>}
    <div className="dashboard-grid"><section className="panel"><div className="panel-heading"><h2>Мої найближчі завдання</h2><Link to={`/tasks?performerId=${user!.id}&sort=due&direction=asc`}>Усі завдання →</Link></div>
      {tasks.loading ? <Loading /> : tasks.error ? <div className="panel-body"><ErrorNotice error={tasks.error} retry={() => void tasks.reload()} /></div> : !tasks.data?.items.length ? <div className="panel-body"><Empty title="Усе під контролем"><p>У вас поки немає активних завдань.</p><Link to="/tasks/new">Створити завдання →</Link></Empty></div> : tasks.data.items.map(task => <div className="task-row" key={task.id}><span className="task-indicator" aria-hidden="true" /><div><h3><Link to={`/tasks/${task.id}`}>{task.name}</Link></h3><p>{stateLabels[task.state]}</p></div><span className={`due ${task.dueDate && parseUtc(task.dueDate).getTime() < Date.now() ? 'overdue' : ''}`}>{formatDate(task.dueDate)}</span></div>)}
    </section><section className="panel"><div className="panel-heading"><h2>Проєкти команди</h2><Link to="/projects">Усі →</Link></div>{projects.loading ? <Loading /> : projects.error ? <div className="panel-body"><ErrorNotice error={projects.error} retry={() => void projects.reload()} /></div> : !projects.data?.length ? <div className="panel-body"><p className="muted">Проєктів поки немає.</p><Link to="/projects/new">Створити проєкт →</Link></div> : projects.data.slice(0, 5).map(project => <div className="task-row" key={project.id}><span className="task-indicator" aria-hidden="true" /><div><h3><Link to={`/projects/${project.id}`}>{project.name}</Link></h3><p>{project.tasks?.length ?? 0} завдань</p></div><span className="due">{formatDate(project.deadline)}</span></div>)}</section></div>
  </>;
}
