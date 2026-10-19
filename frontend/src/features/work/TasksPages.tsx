import { useEffect, useId, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { z } from 'zod';
import { ApiError, request } from '../../lib/api';
import { ProjectSchema, states, TaskPageSchema, TaskSchema, TeamSchema } from '../../lib/contracts';
import type { TaskRecord } from '../../lib/contracts';
import { getTaskInputSchema, getStateLabels } from '../../lib/domain';
import { translateCopyText } from '../../lib/i18n';
import { commonCopy } from '../../lib/commonCopy';
import { useWorkCopy, workCopy } from './workCopy';
import { formatDate, parseUtc, toLocalInput, toUtc } from '../../lib/dates';
import { browserTimeZone } from '../../lib/timeZone';
import { useLoad } from '../../lib/useLoad';
import { useSession } from '../../lib/session';
import { Empty, ErrorNotice, Loading, Pagination } from '../../components/Feedback';
import { TaskBoard } from './TaskBoard';

function readFilters(params: URLSearchParams, userId: number) {
  return { search: params.get('search') ?? '', projectId: params.get('projectId') ?? '', state: params.get('state') ?? '', performerId: params.get('performerId') ?? '', mine: params.get('performerId') === String(userId), overdue: params.get('overdue') === 'true', sort: params.get('sort') ?? 'due' };
}
function hasFilters(filters: ReturnType<typeof readFilters>) {
  return Boolean(filters.search || filters.projectId || filters.state || filters.performerId || filters.mine || filters.overdue || filters.sort !== 'due');
}

export function TasksPage() {
  const { locale, copy, text } = useWorkCopy();
  const stateLabels = getStateLabels(locale);
  const { user } = useSession();
  const [params, setParams] = useSearchParams();
  const filterQuery = new URLSearchParams([...params].filter(([key]) => ['search', 'projectId', 'state', 'performerId', 'overdue', 'sort'].includes(key))).toString();
  const userId = user!.id;
  const searchId = useId(); const advancedId = useId();
  const [filtersExpanded, setFiltersExpanded] = useState(false);
  const [filters, setFilters] = useState(() => readFilters(params, userId));
  useEffect(() => { setFilters(readFilters(new URLSearchParams(filterQuery), userId)); }, [filterQuery, userId]);
  const [actionError, setActionError] = useState<unknown>();
  const [savedState, setSavedState] = useState<{ name: string; state: TaskRecord['state'] }>();
  const [pending, setPending] = useState<ReadonlySet<number>>(() => new Set());
  const pendingIds = useRef(new Set<number>());
  const boardElement = useRef<HTMLDivElement>(null);
  const saveFeedback = useRef<HTMLDivElement>(null);
  const returnFocus = useRef<{ id: number; control: HTMLElement; drag: boolean } | null>(null);
  const query = new URLSearchParams(params); query.delete('view');
  if (!query.has('pageSize')) query.set('pageSize', '20');
  if (!query.has('sort')) { query.set('sort', 'due'); query.set('direction', 'asc'); }
  const load = useLoad(query.toString(), signal => request(`/api/tasks/page?${query}`, TaskPageSchema, { signal }));
  const projects = useLoad('task-projects', signal => request('/api/projects', z.array(ProjectSchema), { signal }));
  const teamId = projects.data?.find(project => String(project.id) === filters.projectId)?.teamId ?? user!.teamId;
  const assignees = useLoad(`filter-assignees-${teamId}`, async signal => teamId ? request(`/api/teams/${teamId}`, TeamSchema, { signal }) : null);
  const board = params.get('view') === 'board';
  const filtersActive = hasFilters(filters) || hasFilters(readFilters(params, userId));
  const advancedFilterCount = [filters.projectId, filters.state, !filters.mine && filters.performerId, filters.sort !== 'due'].filter(Boolean).length;
  useEffect(() => {
    if (load.loading || load.error || !load.data || load.data.totalCount === 0) return;
    const last = Math.max(1, Math.ceil(load.data.totalCount / load.data.pageSize));
    if (load.data.page <= last) return;
    const next = new URLSearchParams(params); next.set('page', String(last));
    setParams(next, { replace: true });
  }, [load.loading, load.error, load.data, params, setParams]);
  useEffect(() => {
    const target = returnFocus.current;
    if (!target || load.loading || pending.has(target.id)) return;
    returnFocus.current = null;
    if (document.activeElement !== document.body && document.activeElement !== target.control) return;
    const control = boardElement.current?.querySelector<HTMLElement>(`[data-task-id="${target.id}"] ${target.drag ? '[data-board-drag]' : 'select'}`);
    (control ?? boardElement.current ?? saveFeedback.current)?.focus();
  }, [load.loading, load.data, pending]);
  function changeQuery(changes: Record<string, string>) { const next = new URLSearchParams(params); for (const [key, value] of Object.entries(changes)) { if (value) next.set(key, value); else next.delete(key); } setParams(next); }
  function clearFilters() {
    setFilters(readFilters(new URLSearchParams(), userId)); setActionError(undefined); setSavedState(undefined);
    const next = new URLSearchParams();
    if (board) next.set('view', 'board');
    if (params.has('pageSize')) next.set('pageSize', params.get('pageSize')!);
    setParams(next);
  }
  function filter(event: FormEvent) { event.preventDefault(); changeQuery({ ...filters, mine: '', overdue: filters.overdue ? 'true' : '', performerId: filters.mine ? String(user!.id) : filters.performerId, page: '1', direction: filters.sort === 'created' ? 'desc' : 'asc' }); }
  async function changeState(task: TaskRecord, state: TaskRecord['state'], control: HTMLElement) {
    if (pendingIds.current.has(task.id) || task.state === state) return;
    if (document.activeElement === control) returnFocus.current = { id: task.id, control, drag: control.hasAttribute('data-board-drag') };
    pendingIds.current.add(task.id); setPending(new Set(pendingIds.current)); setActionError(undefined); setSavedState(undefined);
    try {
      if (!task.version) throw new ApiError(428);
      await request(`/api/tasks/${task.id}/state`, TaskSchema, { method: 'PATCH', body: { state, expectedVersion: task.version } });
      setSavedState({ name: task.name, state });
      await load.reload();
    } catch (error) { setActionError(error); }
    finally { pendingIds.current.delete(task.id); setPending(new Set(pendingIds.current)); }
  }
  return <>
    <div className="page-header"><div><p className="eyebrow">{copy.teamwork}</p><h1>{copy.tasks}</h1><p>{copy.taskIntro}</p></div><Link className="button" to="/tasks/new">＋ {copy.createTask}</Link></div>
    <form className="filters task-filters" data-expanded={filtersExpanded} onSubmit={filter}>
      <div className="filter-fields">
      <div className="field"><div className="filter-search-heading"><label htmlFor={searchId}>{copy.search}</label>
        <button type="button" className="filter-toggle" aria-controls={advancedId} aria-expanded={filtersExpanded}
          aria-label={filtersExpanded ? copy.hideFilters : copy.showFilters} onClick={() => setFiltersExpanded(value => !value)}>
          {copy.filters}{advancedFilterCount > 0 && <span className="filter-count" aria-hidden="true">{advancedFilterCount}</span>}<span className="filter-chevron" aria-hidden="true" /></button></div>
        <input id={searchId} value={filters.search} maxLength={100} onChange={event => setFilters({ ...filters, search: event.target.value })} placeholder={copy.nameOrDescription} /></div>
      <div id={advancedId} className="filter-advanced">
      <label className="field">{copy.project}<select value={filters.projectId} onChange={event => {
        const nextTeamId = projects.data?.find(project => String(project.id) === event.target.value)?.teamId ?? user!.teamId;
        setFilters({ ...filters, projectId: event.target.value, performerId: nextTeamId !== teamId && !filters.mine ? '' : filters.performerId });
      }}><option value="">{copy.allProjects}</option>{projects.data?.map(project => <option key={project.id} value={project.id}>{project.name}</option>)}</select></label>
      <label className="field">{copy.status}<select value={filters.state} onChange={event => setFilters({ ...filters, state: event.target.value })}><option value="">{copy.allStatuses}</option>{states.map(state => <option key={state} value={state}>{stateLabels[state]}</option>)}</select></label>
      <label className="field">{copy.assignee}<select value={filters.mine ? String(user!.id) : filters.performerId} disabled={assignees.loading} onChange={event => setFilters({ ...filters, performerId: event.target.value, mine: false })}><option value="">{copy.allMembers}</option>{assignees.data?.users.map(member => <option key={member.id} value={member.id}>{member.firstName} {member.lastName}</option>)}</select></label>
      <label className="field">{copy.sorting}<select value={filters.sort} onChange={event => setFilters({ ...filters, sort: event.target.value })}><option value="due">{copy.byDeadline}</option><option value="created">{copy.newestFirst}</option><option value="name">{copy.byName}</option></select></label>
      </div>
      </div><div className="filter-actions">
      <label className="checkbox"><input type="checkbox" checked={filters.mine} onChange={event => setFilters({ ...filters, mine: event.target.checked, performerId: '' })} />{copy.mine}</label>
      <label className="checkbox"><input type="checkbox" checked={filters.overdue} onChange={event => setFilters({ ...filters, overdue: event.target.checked })} />{copy.overdue}</label>
      <button className="button secondary">{copy.apply}</button>
      {filtersActive && <button type="button" className="button secondary clear-filters" onClick={clearFilters}>{copy.clearFilters}</button>}
      <div className="view-switch" aria-label={copy.taskView}><button type="button" aria-pressed={!board} onClick={() => changeQuery({ view: '' })}>{copy.list}</button><button type="button" aria-pressed={board} onClick={() => changeQuery({ view: 'board' })}>{copy.board}</button></div>
      </div>
    </form>
    {!!projects.error && <ErrorNotice error={projects.error} retry={() => void projects.reload()} />}
    {!!assignees.error && <ErrorNotice error={assignees.error} retry={() => void assignees.reload()} />}
    {!!actionError && <ErrorNotice error={actionError} retry={() => { setActionError(undefined); void load.reload(); }} />}
    <div ref={saveFeedback} className="task-save-feedback" tabIndex={-1} aria-live="polite" aria-atomic="true">{savedState && <p className="alert">{text('statusUpdated', { name: savedState.name, state: stateLabels[savedState.state] })}</p>}</div>
    {load.loading && !load.data ? <Loading /> : load.error ? <ErrorNotice error={load.error} retry={() => void load.reload()} /> : !load.data?.items.length ? <Empty title={copy.noTasksFound}><p>{copy.taskEmptyIntro}</p>{!filtersActive && <button className="button secondary" onClick={clearFilters}>{copy.clearFilters}</button>}</Empty> : <>
      {board ? <TaskBoard key={query.toString()} items={load.data.items} pending={pending} loading={load.loading} boardRef={boardElement} changeState={changeState} /> : <div className="panel table-wrap tasks-table" role="region" aria-label={copy.taskList} tabIndex={0}><table><thead><tr><th>{copy.tasks}</th><th>{copy.assignee}</th><th>{copy.status}</th><th>{copy.deadline}</th><th>{copy.reminder}</th></tr></thead><tbody>{load.data.items.map(task => <tr key={task.id}><td><Link to={`/tasks/${task.id}`}>{task.name}</Link><small>{projects.data?.find(project => project.id === task.projectId)?.name}</small></td><td>{task.performer ? `${task.performer.firstName} ${task.performer.lastName}` : `#${task.performerId}`}</td><td><span className={`badge ${task.state}`}>{stateLabels[task.state]}</span></td><td className={`due ${task.dueDate && parseUtc(task.dueDate).getTime() < Date.now() && !['Done', 'Canceled'].includes(task.state) ? 'overdue' : ''}`}>{formatDate(task.dueDate)}</td><td>{task.reminderEnabled ? text('reminderShort', { minutes: task.reminderOffsetMinutes ?? 60 }) : copy.disabled}</td></tr>)}</tbody></table></div>}
      <Pagination page={load.data.page} pageSize={load.data.pageSize} total={load.data.totalCount} change={page => changeQuery({ page: String(page) })} />
    </>}
  </>;
}

export function TaskDetailPage() {
  const { locale, copy, text } = useWorkCopy();
  const stateLabels = getStateLabels(locale);
  const { id } = useParams(); const navigate = useNavigate();
  const load = useLoad(`task-${id}`, signal => request(`/api/tasks/${id}`, TaskSchema, { signal }));
  const [error, setError] = useState<unknown>(); const [pending, setPending] = useState(false);
  async function remove() {
    if (!load.data || !window.confirm(copy.taskDeletePrompt)) return;
    setPending(true); setError(undefined);
    try { if (!load.data.version) throw new ApiError(428); await request(`/api/tasks/${id}`, undefined, { method: 'DELETE', headers: { 'If-Match': `"${load.data.version}"` } }); navigate('/tasks'); }
    catch (failure) { setError(failure); } finally { setPending(false); }
  }
  if (load.loading) return <Loading />;
  if (load.error) return <ErrorNotice error={load.error} retry={() => void load.reload()} />;
  const task = load.data!;
  return <><div className="page-header"><div><p className="eyebrow"><Link to="/tasks">{copy.tasks}</Link></p><h1>{task.name}</h1><span className={`badge ${task.state}`}>{stateLabels[task.state]}</span></div><div className="actions"><Link className="button" to={`/tasks/${id}/edit`}>{copy.edit}</Link><button className="button danger" disabled={pending} onClick={() => void remove()}>{copy.remove}</button></div></div>
    {!!error && <ErrorNotice error={error} retry={() => { setError(undefined); void load.reload(); }} />}
    <div className="details-grid"><section className="panel"><div className="panel-heading"><h2>{copy.description}</h2></div><div className="panel-body"><p style={{ whiteSpace: 'pre-wrap' }}>{task.description || copy.noDescription}</p><Link to={`/projects/${task.projectId}`}>{copy.openProject}</Link></div></section>
      <section className="panel"><div className="panel-body"><dl className="meta-list"><div><dt>{copy.assignee}</dt><dd>{task.performer ? `${task.performer.firstName} ${task.performer.lastName}` : `#${task.performerId}`}</dd></div><div><dt>{copy.taskDeadline}</dt><dd>{formatDate(task.dueDate)}</dd></div><div><dt>{copy.reminder}</dt><dd>{task.reminderEnabled ? text('reminderBefore', { minutes: task.reminderOffsetMinutes ?? 60 }) : copy.disabled}</dd></div><div><dt>{copy.escalation}</dt><dd>{task.escalationEnabled ? text('reminderAfter', { minutes: task.escalationDelayMinutes ?? 60 }) : copy.disabled}</dd></div><div><dt>{copy.created}</dt><dd>{formatDate(task.createdAt)}</dd></div></dl></div></section></div></>;
}

export function TaskFormPage() {
  const { copy } = useWorkCopy();
  const { id } = useParams();
  const [params] = useSearchParams();
  const projects = useLoad('form-projects', signal => request('/api/projects', z.array(ProjectSchema), { signal }));
  const existing = useLoad(`edit-${id}`, async signal => id ? request(`/api/tasks/${id}`, TaskSchema, { signal }) : null);
  if (projects.loading || existing.loading) return <Loading />;
  if (projects.error || existing.error) return <ErrorNotice error={projects.error ?? existing.error} retry={() => { void projects.reload(); void existing.reload(); }} />;
  if (!projects.data?.length) return <Empty title={copy.projectRequired}><p>{copy.taskProjectIntro}</p><Link className="button" to="/projects/new">{copy.createProject}</Link></Empty>;
  return <TaskEditor key={id ?? 'new'} task={existing.data ?? undefined} projects={projects.data} preferredProject={Number(params.get('projectId')) || projects.data[0].id} />;
}
function TaskEditor({ task, projects, preferredProject }: { task?: TaskRecord; projects: z.infer<typeof ProjectSchema>[]; preferredProject: number }) {
  const navigate = useNavigate(); const { user } = useSession();
  const { locale, copy, text } = useWorkCopy();
  const stateLabels = getStateLabels(locale);
  const [values, setValues] = useState({ projectId: task?.projectId ?? preferredProject, performerId: task?.performerId ?? user!.id, name: task?.name ?? '', description: task?.description ?? '', state: task?.state ?? 'ToDo', due: toLocalInput(task?.dueDate ?? null), reminderEnabled: task?.reminderEnabled ?? false, reminderOffset: String(task?.reminderOffsetMinutes ?? 60), escalationEnabled: task?.escalationEnabled ?? false, escalationDelay: String(task?.escalationDelayMinutes ?? 60) });
  const [version, setVersion] = useState(task?.version); const [error, setError] = useState<unknown>();
  const [validation, setValidation] = useState<Record<string, string>>({}); const [pending, setPending] = useState(false);
  const [current, setCurrent] = useState<TaskRecord>();
  const project = projects.find(value => value.id === values.projectId);
  const team = useLoad(`assignees-${project?.teamId}`, async signal => project ? request(`/api/teams/${project.teamId}`, TeamSchema, { signal }) : null);
  async function submit(event: FormEvent) {
    event.preventDefault(); setError(undefined); setValidation({});
    const parsed = getTaskInputSchema(locale).safeParse({ ...values, dueDate: toUtc(values.due), reminderOffsetMinutes: values.reminderEnabled ? Number(values.reminderOffset) : null, escalationDelayMinutes: values.escalationEnabled ? Number(values.escalationDelay) : null, expectedVersion: version });
    if (!parsed.success) { setValidation(Object.fromEntries(parsed.error.issues.map(issue => [String(issue.path[0]), issue.message]))); return; }
    if (!team.data?.users.some(member => member.id === values.performerId)) { setValidation({ performerId: copy.currentMember }); return; }
    setPending(true);
    try { if (task && !version) throw new ApiError(428); const saved = await request(task ? `/api/tasks/${task.id}` : '/api/tasks', TaskSchema, { method: task ? 'PUT' : 'POST', body: parsed.data }); navigate(`/tasks/${saved.id}`); }
    catch (failure) { setError(failure); } finally { setPending(false); }
  }
  async function reviewCurrent() { try { const record = await request(`/api/tasks/${task!.id}`, TaskSchema); setCurrent(record); setVersion(record.version); } catch (failure) { setError(failure); } }
  const fieldError = (field: string) => validation[field] ? <small className="field-error">{translateCopyText(translateCopyText(validation[field], commonCopy, locale), workCopy, locale)}</small> : null;
  return <><div className="page-header"><div><p className="eyebrow"><Link to="/tasks">{copy.tasks}</Link></p><h1>{task ? copy.editTask : copy.newTask}</h1><p>{text('taskTimeZone', { timeZone: browserTimeZone() })}</p></div></div>
    {!!error && <ErrorNotice error={error} />}
    {error instanceof ApiError && [409, 428].includes(error.status) && <div className="alert alert-warning"><p>{copy.conflictIntro}</p><button className="button secondary" onClick={() => void reviewCurrent()}>{copy.loadCurrent}</button>{current && <section aria-label={copy.currentTaskFields}><h2>{copy.currentServerVersion}</h2><dl className="meta-list"><div><dt>{copy.name}</dt><dd><strong>{current.name}</strong></dd></div><div><dt>{copy.description}</dt><dd style={{ whiteSpace: 'pre-wrap' }}>{current.description || copy.noDescription}</dd></div><div><dt>{copy.project}</dt><dd>{projects.find(value => value.id === current.projectId)?.name ?? `#${current.projectId}`}</dd></div><div><dt>{copy.assignee}</dt><dd>{current.performer ? `${current.performer.firstName} ${current.performer.lastName}` : `#${current.performerId}`}</dd></div><div><dt>{copy.status}</dt><dd>{stateLabels[current.state]}</dd></div><div><dt>{copy.deadline}</dt><dd>{formatDate(current.dueDate)}</dd></div><div><dt>{copy.reminder}</dt><dd>{current.reminderEnabled ? text('reminderBefore', { minutes: current.reminderOffsetMinutes ?? 60 }) : copy.disabled}</dd></div><div><dt>{copy.escalation}</dt><dd>{current.escalationEnabled ? text('reminderAfter', { minutes: current.escalationDelayMinutes ?? 60 }) : copy.disabled}</dd></div></dl><p>{copy.conflictWarning}</p></section>}</div>}
    <form className="form-panel" onSubmit={event => void submit(event)}><div className="form-grid">
      <label className="field span-full">{copy.name}<input required maxLength={100} value={values.name} onChange={event => setValues({ ...values, name: event.target.value })} aria-invalid={!!validation.name} />{fieldError('name')}</label>
      <label className="field span-full">{copy.description}<textarea maxLength={1000} value={values.description} onChange={event => setValues({ ...values, description: event.target.value })} /></label>
      <label className="field">{copy.project}<select value={values.projectId} onChange={event => setValues({ ...values, projectId: Number(event.target.value), performerId: 0 })}>{projects.map(value => <option key={value.id} value={value.id}>{value.name}</option>)}</select>{fieldError('projectId')}</label>
      <label className="field">{copy.assignee}<select value={values.performerId} disabled={team.loading} onChange={event => setValues({ ...values, performerId: Number(event.target.value) })}><option value={0}>{copy.chooseMember}</option>{team.data?.users.map(member => <option key={member.id} value={member.id}>{member.firstName} {member.lastName}</option>)}</select>{fieldError('performerId')}</label>
      <label className="field">{copy.status}<select value={values.state} onChange={event => setValues({ ...values, state: event.target.value as TaskRecord['state'] })}>{states.map(state => <option key={state} value={state}>{stateLabels[state]}</option>)}</select></label>
      <label className="field">{copy.taskDeadline}<input type="datetime-local" value={values.due} onChange={event => setValues({ ...values, due: event.target.value })} />{fieldError('dueDate')}</label>
      <label className="checkbox"><input type="checkbox" checked={values.reminderEnabled} onChange={event => setValues({ ...values, reminderEnabled: event.target.checked })} />{copy.remindBefore}</label>
      <label className="field">{copy.minutesBefore}<input type="number" min={1} max={10080} disabled={!values.reminderEnabled} value={values.reminderOffset} onChange={event => setValues({ ...values, reminderOffset: event.target.value })} /></label>
      <label className="checkbox"><input type="checkbox" checked={values.escalationEnabled} onChange={event => setValues({ ...values, escalationEnabled: event.target.checked })} />{copy.remindOverdue}</label>
      <label className="field">{copy.minutesAfter}<input type="number" min={1} max={10080} disabled={!values.escalationEnabled} value={values.escalationDelay} onChange={event => setValues({ ...values, escalationDelay: event.target.value })} /></label>
    </div>{!!team.error && <ErrorNotice error={team.error} retry={() => void team.reload()} />}<div className="form-actions"><button className="button" disabled={pending || team.loading || !!team.error}>{pending ? copy.saving : task ? copy.saveChanges : copy.createTask}</button><Link className="button secondary" to={task ? `/tasks/${task.id}` : '/tasks'}>{copy.cancel}</Link></div></form></>;
}
