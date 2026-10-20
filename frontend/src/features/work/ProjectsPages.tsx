import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { z } from 'zod';
import { ApiError, request } from '../../lib/api';
import { ProjectSchema, TeamSchema } from '../../lib/contracts';
import type { Project, Team } from '../../lib/contracts';
import { formatDate, toLocalInput, toUtc } from '../../lib/dates';
import { getStateLabels } from '../../lib/domain';
import { useWorkCopy } from './workCopy';
import { useSession } from '../../lib/session';
import { useLoad } from '../../lib/useLoad';
import { Empty, ErrorNotice, Loading, Pagination } from '../../components/Feedback';
const SavedProject = ProjectSchema.omit({ team: true, tasks: true });

export function ProjectsPage() {
  const { copy, text, intlLocale } = useWorkCopy();
  const load = useLoad('projects', signal => request('/api/projects', z.array(ProjectSchema), { signal }));
  const [search, setSearch] = useState(''); const [page, setPage] = useState(1);
  const filtered = load.data?.filter(project => `${project.name} ${project.description}`.toLocaleLowerCase(intlLocale).includes(search.toLocaleLowerCase(intlLocale))) ?? [];
  return <><div className="page-header"><div><p className="eyebrow">{copy.sharedGoals}</p><h1>{copy.projects}</h1><p>{copy.projectIntro}</p></div><Link className="button" to="/projects/new">＋ {copy.createProject}</Link></div>
    <label className="field" style={{ maxWidth: 400, marginBottom: 24 }}>{copy.findProject}<input value={search} maxLength={100} placeholder={copy.nameOrDescription} onChange={event => { setSearch(event.target.value); setPage(1); }} /></label>
    {load.loading ? <Loading /> : load.error ? <ErrorNotice error={load.error} retry={() => void load.reload()} /> : !filtered.length ? <Empty title={search ? copy.noMatchingProjects : copy.firstProjectGoal}><p>{search ? copy.tryAnotherName : copy.createTeamAndProject}</p><Link className="button" to="/projects/new">{copy.createProject}</Link></Empty> : <><div className="project-grid">{filtered.slice((page - 1) * 20, page * 20).map(project => <article className="project-card" key={project.id}><p className="eyebrow">{project.team?.name ?? copy.teamProject}</p><h2><Link to={`/projects/${project.id}`}>{project.name}</Link></h2><p>{project.description || copy.noDescription}</p><div className="actions"><span className="badge">{text('taskCount', { count: project.tasks?.length ?? 0 })}</span><span className="due">{formatDate(project.deadline)}</span></div></article>)}</div><Pagination page={page} pageSize={20} total={filtered.length} change={setPage} /></>}
  </>;
}
export function ProjectDetailPage() {
  const { locale, copy } = useWorkCopy();
  const stateLabels = getStateLabels(locale);
  const { id } = useParams(); const navigate = useNavigate(); const { user } = useSession();
  const load = useLoad(`project-${id}`, signal => request(`/api/projects/${id}`, ProjectSchema, { signal }));
  const [error, setError] = useState<unknown>(); const [pending, setPending] = useState(false);
  async function remove() {
    if (!window.confirm(copy.projectDeletePrompt)) return;
    setError(undefined); setPending(true);
    try { await request(`/api/projects/${id}`, undefined, { method: 'DELETE' }); navigate('/projects'); }
    catch (failure) { setError(failure); } finally { setPending(false); }
  }
  if (load.loading) return <Loading />;
  if (load.error) return <ErrorNotice error={load.error} retry={() => void load.reload()} />;
  const project = load.data!; const canManage = project.authorId === user!.id || user!.roles.some(role => role.toLowerCase() === 'admin');
  return <><div className="page-header"><div><p className="eyebrow"><Link to="/projects">{copy.projects}</Link> / {project.team?.name}</p><h1>{project.name}</h1><p>{copy.deadline}: {formatDate(project.deadline)}</p></div><div className="actions"><Link className="button" to={`/tasks/new?projectId=${id}`}>＋ {copy.tasks}</Link>{canManage && <><Link className="button secondary" to={`/projects/${id}/edit`}>{copy.edit}</Link><button className="button danger" disabled={pending} onClick={() => void remove()}>{copy.remove}</button></>}</div></div>
    {!!error && <ErrorNotice error={error} retry={() => void load.reload()} />}
    <div className="details-grid"><section><div className="panel"><div className="panel-body"><h2>{copy.projectGoal}</h2><p style={{ whiteSpace: 'pre-wrap' }}>{project.description || copy.noDescription}</p></div></div>
      <div className="panel"><div className="panel-heading"><h2>{copy.projectTasks}</h2><Link to={`/tasks?projectId=${id}`}>{copy.openList}</Link></div>{project.tasks?.length ? project.tasks.slice(0, 10).map(task => <div className="task-row" key={task.id}><span className="task-indicator" aria-hidden="true" /><div><h3><Link to={`/tasks/${task.id}`}>{task.name}</Link></h3><p>{stateLabels[task.state]}</p></div><span className="due">{formatDate(task.dueDate)}</span></div>) : <div className="panel-body"><p className="muted">{copy.noProjectTasks}</p><Link to={`/tasks/new?projectId=${id}`}>{copy.createFirstTask}</Link></div>}</div></section>
      <section className="panel"><div className="panel-heading"><h2>{copy.teamMembers}</h2></div><div className="panel-body member-list">{project.team?.users.map(member => <div className="member" key={member.id}><p>{member.firstName} {member.lastName}<br /><small>{member.email}</small></p></div>)}<Link to={`/teams/${project.teamId}`}>{copy.openTeam}</Link></div></section></div></>;
}
export function ProjectFormPage() {
  const { copy } = useWorkCopy();
  const { id } = useParams(); const { user } = useSession();
  const teams = useLoad('project-teams', signal => request('/api/teams', z.array(TeamSchema), { signal }));
  const existing = useLoad(`project-edit-${id}`, async signal => id ? request(`/api/projects/${id}`, ProjectSchema, { signal }) : null);
  if (teams.loading || existing.loading) return <Loading />;
  if (teams.error || existing.error) return <ErrorNotice error={teams.error ?? existing.error} retry={() => { void teams.reload(); void existing.reload(); }} />;
  if (!teams.data?.length) return <Empty title={copy.teamRequired}><p>{copy.projectTeamIntro}</p><Link className="button" to="/teams">{copy.openTeams}</Link></Empty>;
  if (existing.data && existing.data.authorId !== user!.id && !user!.roles.some(role => role.toLowerCase() === 'admin')) return <ErrorNotice error={new ApiError(403)} />;
  return <ProjectEditor key={id ?? 'new'} project={existing.data ?? undefined} teams={teams.data} />;
}
function ProjectEditor({ project, teams }: { project?: Project; teams: Team[] }) {
  const navigate = useNavigate();
  const { copy } = useWorkCopy();
  const [name, setName] = useState(project?.name ?? ''); const [description, setDescription] = useState(project?.description ?? '');
  const [teamId, setTeamId] = useState(project?.teamId ?? teams[0].id); const [deadline, setDeadline] = useState(toLocalInput(project?.deadline ?? null));
  const [error, setError] = useState<unknown>(); const [pending, setPending] = useState(false);
  async function submit(event: FormEvent) {
    event.preventDefault(); if (!name.trim() || !deadline) return;
    setError(undefined); setPending(true);
    try { const saved = await request(project ? `/api/projects/${project.id}` : '/api/projects', SavedProject, { method: project ? 'PUT' : 'POST', body: { teamId, name: name.trim(), description, deadline: toUtc(deadline) } }); navigate(`/projects/${saved.id}`); }
    catch (failure) { setError(failure); } finally { setPending(false); }
  }
  return <><div className="page-header"><div><p className="eyebrow"><Link to="/projects">{copy.projects}</Link></p><h1>{project ? copy.editProject : copy.newProject}</h1><p>{copy.projectFormIntro}</p></div></div>{!!error && <ErrorNotice error={error} />}
    <form className="form-panel" onSubmit={event => void submit(event)}><div className="form-grid"><label className="field span-full">{copy.name}<input required maxLength={100} value={name} onChange={event => setName(event.target.value)} /></label><label className="field span-full">{copy.description}<textarea maxLength={1000} value={description} onChange={event => setDescription(event.target.value)} /></label><label className="field">{copy.team}<select value={teamId} onChange={event => setTeamId(Number(event.target.value))}>{teams.map(team => <option value={team.id} key={team.id}>{team.name}</option>)}</select></label><label className="field">{copy.projectDeadline}<input required type="datetime-local" value={deadline} onChange={event => setDeadline(event.target.value)} /></label></div><div className="form-actions"><button className="button" disabled={pending}>{pending ? copy.saving : project ? copy.saveChanges : copy.createProject}</button><Link className="button secondary" to={project ? `/projects/${project.id}` : '/projects'}>{copy.cancel}</Link></div></form></>;
}
