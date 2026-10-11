import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { z } from 'zod';
import { ApiError, request } from '../../lib/api';
import { ProjectSchema, states, TaskPageSchema, TaskSchema, TeamSchema } from '../../lib/contracts';
import type { TaskRecord } from '../../lib/contracts';
import { TaskInputSchema, stateLabels } from '../../lib/domain';
import { formatDate, parseUtc, toLocalInput, toUtc } from '../../lib/dates';
import { useLoad } from '../../lib/useLoad';
import { useSession } from '../../lib/session';
import { Empty, ErrorNotice, Loading, Pagination } from '../../components/Feedback';

function writeBody(task: TaskRecord, state = task.state) {
  return { projectId: task.projectId, performerId: task.performerId, name: task.name, description: task.description, state,
    dueDate: task.dueDate ? parseUtc(task.dueDate).toISOString() : null, reminderEnabled: task.reminderEnabled, reminderOffsetMinutes: task.reminderOffsetMinutes,
    escalationEnabled: task.escalationEnabled, escalationDelayMinutes: task.escalationDelayMinutes, expectedVersion: task.version };
}
export function TasksPage() {
  const { user } = useSession();
  const [params, setParams] = useSearchParams();
  const [filters, setFilters] = useState({ search: params.get('search') ?? '', projectId: params.get('projectId') ?? '', state: params.get('state') ?? '', performerId: params.get('performerId') ?? '', mine: params.get('performerId') === String(user!.id), overdue: params.get('overdue') === 'true', sort: params.get('sort') ?? 'due' });
  const [actionError, setActionError] = useState<unknown>();
  const [pending, setPending] = useState<number>();
  const query = new URLSearchParams(params); query.delete('view');
  if (!query.has('pageSize')) query.set('pageSize', '20');
  if (!query.has('sort')) { query.set('sort', 'due'); query.set('direction', 'asc'); }
  const load = useLoad(query.toString(), signal => request(`/api/tasks/page?${query}`, TaskPageSchema, { signal }));
  const projects = useLoad('task-projects', signal => request('/api/projects', z.array(ProjectSchema), { signal }));
  const teamId = projects.data?.find(project => String(project.id) === filters.projectId)?.teamId ?? user!.teamId;
  const assignees = useLoad(`filter-assignees-${teamId}`, async signal => teamId ? request(`/api/teams/${teamId}`, TeamSchema, { signal }) : null);
  const board = params.get('view') === 'board';
  function changeQuery(changes: Record<string, string>) { const next = new URLSearchParams(params); for (const [key, value] of Object.entries(changes)) { if (value) next.set(key, value); else next.delete(key); } setParams(next); }
  function filter(event: FormEvent) { event.preventDefault(); changeQuery({ ...filters, mine: '', overdue: filters.overdue ? 'true' : '', performerId: filters.mine ? String(user!.id) : filters.performerId, page: '1', direction: filters.sort === 'created' ? 'desc' : 'asc' }); }
  async function changeState(task: TaskRecord, state: TaskRecord['state']) {
    setPending(task.id); setActionError(undefined);
    try { if (!task.version) throw new ApiError(428); await request(`/api/tasks/${task.id}`, TaskSchema, { method: 'PUT', body: writeBody(task, state) }); await load.reload(); }
    catch (error) { setActionError(error); } finally { setPending(undefined); }
  }
  return <>
    <div className="page-header"><div><p className="eyebrow">Командна робота</p><h1>Завдання</h1><p>Плануйте наступний крок і стежте за строками.</p></div><Link className="button" to="/tasks/new">＋ Створити завдання</Link></div>
    <form className="filters" onSubmit={filter}>
      <label className="field">Пошук<input value={filters.search} maxLength={100} onChange={event => setFilters({ ...filters, search: event.target.value })} placeholder="Назва або опис" /></label>
      <label className="field">Проєкт<select value={filters.projectId} onChange={event => setFilters({ ...filters, projectId: event.target.value })}><option value="">Усі проєкти</option>{projects.data?.map(project => <option key={project.id} value={project.id}>{project.name}</option>)}</select></label>
      <label className="field">Статус<select value={filters.state} onChange={event => setFilters({ ...filters, state: event.target.value })}><option value="">Усі статуси</option>{states.map(state => <option key={state} value={state}>{stateLabels[state]}</option>)}</select></label>
      <label className="field">Виконавець<select value={filters.mine ? String(user!.id) : filters.performerId} disabled={assignees.loading} onChange={event => setFilters({ ...filters, performerId: event.target.value, mine: false })}><option value="">Усі учасники</option>{assignees.data?.users.map(member => <option key={member.id} value={member.id}>{member.firstName} {member.lastName}</option>)}</select></label>
      <label className="field">Сортування<select value={filters.sort} onChange={event => setFilters({ ...filters, sort: event.target.value })}><option value="due">За строком</option><option value="created">Спочатку нові</option><option value="name">За назвою</option></select></label>
      <label className="checkbox"><input type="checkbox" checked={filters.mine} onChange={event => setFilters({ ...filters, mine: event.target.checked, performerId: '' })} />Мої</label>
      <label className="checkbox"><input type="checkbox" checked={filters.overdue} onChange={event => setFilters({ ...filters, overdue: event.target.checked })} />Прострочені</label>
      <button className="button secondary">Показати</button>
      <div className="view-switch" aria-label="Вигляд завдань"><button type="button" aria-pressed={!board} onClick={() => changeQuery({ view: '' })}>Список</button><button type="button" aria-pressed={board} onClick={() => changeQuery({ view: 'board' })}>Дошка</button></div>
    </form>
    {!!projects.error && <ErrorNotice error={projects.error} retry={() => void projects.reload()} />}
    {!!assignees.error && <ErrorNotice error={assignees.error} retry={() => void assignees.reload()} />}
    {!!actionError && <ErrorNotice error={actionError} retry={() => { setActionError(undefined); void load.reload(); }} />}
    {load.loading ? <Loading /> : load.error ? <ErrorNotice error={load.error} retry={() => void load.reload()} /> : !load.data?.items.length ? <Empty title="Завдань не знайдено"><p>Створіть завдання або змініть фільтри.</p><button className="button secondary" onClick={() => { setFilters({ search: '', projectId: '', state: '', performerId: '', mine: false, overdue: false, sort: 'due' }); setParams({}); }}>Очистити фільтри</button></Empty> : <>
      {board ? <div className="board" aria-label="Дошка завдань поточної сторінки">{states.map(state => <section className="board-column" key={state}><div className="board-title"><h2>{stateLabels[state]}</h2><span>{load.data!.items.filter(task => task.state === state).length}</span></div>{load.data!.items.filter(task => task.state === state).map(task => <article className="task-card" key={task.id}><h3><Link to={`/tasks/${task.id}`}>{task.name}</Link></h3><small className="muted">{task.performer ? `${task.performer.firstName} ${task.performer.lastName}` : `Виконавець #${task.performerId}`}</small><span className="due">{formatDate(task.dueDate)}</span><select aria-label={`Статус: ${task.name}`} value={task.state} disabled={pending === task.id} onChange={event => void changeState(task, event.target.value as TaskRecord['state'])}>{states.map(value => <option key={value} value={value}>{stateLabels[value]}</option>)}</select></article>)}</section>)}</div> : <div className="panel table-wrap" role="region" aria-label="Список завдань" tabIndex={0}><table><thead><tr><th>Завдання</th><th>Виконавець</th><th>Статус</th><th>Строк</th><th>Нагадування</th></tr></thead><tbody>{load.data.items.map(task => <tr key={task.id}><td><Link to={`/tasks/${task.id}`}>{task.name}</Link><small>{projects.data?.find(project => project.id === task.projectId)?.name}</small></td><td>{task.performer ? `${task.performer.firstName} ${task.performer.lastName}` : `#${task.performerId}`}</td><td><span className={`badge ${task.state}`}>{stateLabels[task.state]}</span></td><td className={`due ${task.dueDate && parseUtc(task.dueDate).getTime() < Date.now() && !['Done', 'Canceled'].includes(task.state) ? 'overdue' : ''}`}>{formatDate(task.dueDate)}</td><td>{task.reminderEnabled ? `За ${task.reminderOffsetMinutes ?? 60} хв` : 'Вимкнено'}</td></tr>)}</tbody></table></div>}
      <Pagination page={load.data.page} pageSize={load.data.pageSize} total={load.data.totalCount} change={page => changeQuery({ page: String(page) })} />
    </>}
  </>;
}

export function TaskDetailPage() {
  const { id } = useParams(); const navigate = useNavigate();
  const load = useLoad(`task-${id}`, signal => request(`/api/tasks/${id}`, TaskSchema, { signal }));
  const [error, setError] = useState<unknown>(); const [pending, setPending] = useState(false);
  async function remove() {
    if (!load.data || !window.confirm('Видалити це завдання?')) return;
    setPending(true); setError(undefined);
    try { if (!load.data.version) throw new ApiError(428); await request(`/api/tasks/${id}`, undefined, { method: 'DELETE', headers: { 'If-Match': `"${load.data.version}"` } }); navigate('/tasks'); }
    catch (failure) { setError(failure); } finally { setPending(false); }
  }
  if (load.loading) return <Loading />;
  if (load.error) return <ErrorNotice error={load.error} retry={() => void load.reload()} />;
  const task = load.data!;
  return <><div className="page-header"><div><p className="eyebrow"><Link to="/tasks">Завдання</Link></p><h1>{task.name}</h1><span className={`badge ${task.state}`}>{stateLabels[task.state]}</span></div><div className="actions"><Link className="button" to={`/tasks/${id}/edit`}>Редагувати</Link><button className="button danger" disabled={pending} onClick={() => void remove()}>Видалити</button></div></div>
    {!!error && <ErrorNotice error={error} retry={() => { setError(undefined); void load.reload(); }} />}
    <div className="details-grid"><section className="panel"><div className="panel-heading"><h2>Опис</h2></div><div className="panel-body"><p style={{ whiteSpace: 'pre-wrap' }}>{task.description || 'Опис ще не додано.'}</p><Link to={`/projects/${task.projectId}`}>Перейти до проєкту →</Link></div></section>
      <section className="panel"><div className="panel-body"><dl className="meta-list"><div><dt>Виконавець</dt><dd>{task.performer ? `${task.performer.firstName} ${task.performer.lastName}` : `#${task.performerId}`}</dd></div><div><dt>Строк виконання</dt><dd>{formatDate(task.dueDate)}</dd></div><div><dt>Нагадування</dt><dd>{task.reminderEnabled ? `За ${task.reminderOffsetMinutes ?? 60} хв до строку` : 'Вимкнено'}</dd></div><div><dt>Повторне нагадування</dt><dd>{task.escalationEnabled ? `Через ${task.escalationDelayMinutes ?? 60} хв після строку` : 'Вимкнено'}</dd></div><div><dt>Створено</dt><dd>{formatDate(task.createdAt)}</dd></div></dl></div></section></div></>;
}

export function TaskFormPage() {
  const { id } = useParams();
  const [params] = useSearchParams();
  const projects = useLoad('form-projects', signal => request('/api/projects', z.array(ProjectSchema), { signal }));
  const existing = useLoad(`edit-${id}`, async signal => id ? request(`/api/tasks/${id}`, TaskSchema, { signal }) : null);
  if (projects.loading || existing.loading) return <Loading />;
  if (projects.error || existing.error) return <ErrorNotice error={projects.error ?? existing.error} retry={() => { void projects.reload(); void existing.reload(); }} />;
  if (!projects.data?.length) return <Empty title="Спочатку потрібен проєкт"><p>Завдання створюються у проєкті вашої команди.</p><Link className="button" to="/projects/new">Створити проєкт</Link></Empty>;
  return <TaskEditor key={id ?? 'new'} task={existing.data ?? undefined} projects={projects.data} preferredProject={Number(params.get('projectId')) || projects.data[0].id} />;
}
function TaskEditor({ task, projects, preferredProject }: { task?: TaskRecord; projects: z.infer<typeof ProjectSchema>[]; preferredProject: number }) {
  const navigate = useNavigate(); const { user } = useSession();
  const [values, setValues] = useState({ projectId: task?.projectId ?? preferredProject, performerId: task?.performerId ?? user!.id, name: task?.name ?? '', description: task?.description ?? '', state: task?.state ?? 'ToDo', due: toLocalInput(task?.dueDate ?? null), reminderEnabled: task?.reminderEnabled ?? false, reminderOffset: String(task?.reminderOffsetMinutes ?? 60), escalationEnabled: task?.escalationEnabled ?? false, escalationDelay: String(task?.escalationDelayMinutes ?? 60) });
  const [version, setVersion] = useState(task?.version); const [error, setError] = useState<unknown>();
  const [validation, setValidation] = useState<Record<string, string>>({}); const [pending, setPending] = useState(false);
  const [current, setCurrent] = useState<TaskRecord>();
  const project = projects.find(value => value.id === values.projectId);
  const team = useLoad(`assignees-${project?.teamId}`, async signal => project ? request(`/api/teams/${project.teamId}`, TeamSchema, { signal }) : null);
  async function submit(event: FormEvent) {
    event.preventDefault(); setError(undefined); setValidation({});
    const parsed = TaskInputSchema.safeParse({ ...values, dueDate: toUtc(values.due), reminderOffsetMinutes: values.reminderEnabled ? Number(values.reminderOffset) : null, escalationDelayMinutes: values.escalationEnabled ? Number(values.escalationDelay) : null, expectedVersion: version });
    if (!parsed.success) { setValidation(Object.fromEntries(parsed.error.issues.map(issue => [String(issue.path[0]), issue.message]))); return; }
    if (!team.data?.users.some(member => member.id === values.performerId)) { setValidation({ performerId: 'Оберіть поточного учасника команди.' }); return; }
    setPending(true);
    try { if (task && !version) throw new ApiError(428); const saved = await request(task ? `/api/tasks/${task.id}` : '/api/tasks', TaskSchema, { method: task ? 'PUT' : 'POST', body: parsed.data }); navigate(`/tasks/${saved.id}`); }
    catch (failure) { setError(failure); } finally { setPending(false); }
  }
  async function reviewCurrent() { try { const record = await request(`/api/tasks/${task!.id}`, TaskSchema); setCurrent(record); setVersion(record.version); } catch (failure) { setError(failure); } }
  const fieldError = (field: string) => validation[field] ? <small className="field-error">{validation[field]}</small> : null;
  return <><div className="page-header"><div><p className="eyebrow"><Link to="/tasks">Завдання</Link></p><h1>{task ? 'Редагувати завдання' : 'Нове завдання'}</h1><p>Строк задається у вашому часовому поясі: {Intl.DateTimeFormat().resolvedOptions().timeZone}.</p></div></div>
    {!!error && <ErrorNotice error={error} />}
    {error instanceof ApiError && [409, 428].includes(error.status) && <div className="alert alert-warning"><p>Ваші зміни збережено у формі. Перед повторним збереженням перевірте актуальний запис.</p><button className="button secondary" onClick={() => void reviewCurrent()}>Завантажити поточну версію</button>{current && <section aria-label="Актуальні поля завдання"><h2>Поточна версія на сервері</h2><dl className="meta-list"><div><dt>Назва</dt><dd><strong>{current.name}</strong></dd></div><div><dt>Опис</dt><dd style={{ whiteSpace: 'pre-wrap' }}>{current.description || 'Опис ще не додано.'}</dd></div><div><dt>Проєкт</dt><dd>{projects.find(value => value.id === current.projectId)?.name ?? `#${current.projectId}`}</dd></div><div><dt>Виконавець</dt><dd>{current.performer ? `${current.performer.firstName} ${current.performer.lastName}` : `#${current.performerId}`}</dd></div><div><dt>Статус</dt><dd>{stateLabels[current.state]}</dd></div><div><dt>Строк</dt><dd>{formatDate(current.dueDate)}</dd></div><div><dt>Нагадування</dt><dd>{current.reminderEnabled ? `За ${current.reminderOffsetMinutes ?? 60} хв до строку` : 'Вимкнено'}</dd></div><div><dt>Повторне нагадування</dt><dd>{current.escalationEnabled ? `Через ${current.escalationDelayMinutes ?? 60} хв після строку` : 'Вимкнено'}</dd></div></dl><p>Наступне збереження застосує всі поля вашої форми до цієї версії. Перевірте відмінності перед збереженням.</p></section>}</div>}
    <form className="form-panel" onSubmit={event => void submit(event)}><div className="form-grid">
      <label className="field span-full">Назва<input required maxLength={100} value={values.name} onChange={event => setValues({ ...values, name: event.target.value })} aria-invalid={!!validation.name} />{fieldError('name')}</label>
      <label className="field span-full">Опис<textarea maxLength={1000} value={values.description} onChange={event => setValues({ ...values, description: event.target.value })} /></label>
      <label className="field">Проєкт<select value={values.projectId} onChange={event => setValues({ ...values, projectId: Number(event.target.value), performerId: 0 })}>{projects.map(value => <option key={value.id} value={value.id}>{value.name}</option>)}</select>{fieldError('projectId')}</label>
      <label className="field">Виконавець<select value={values.performerId} disabled={team.loading} onChange={event => setValues({ ...values, performerId: Number(event.target.value) })}><option value={0}>Оберіть учасника</option>{team.data?.users.map(member => <option key={member.id} value={member.id}>{member.firstName} {member.lastName}</option>)}</select>{fieldError('performerId')}</label>
      <label className="field">Статус<select value={values.state} onChange={event => setValues({ ...values, state: event.target.value as TaskRecord['state'] })}>{states.map(state => <option key={state} value={state}>{stateLabels[state]}</option>)}</select></label>
      <label className="field">Строк виконання<input type="datetime-local" value={values.due} onChange={event => setValues({ ...values, due: event.target.value })} />{fieldError('dueDate')}</label>
      <label className="checkbox"><input type="checkbox" checked={values.reminderEnabled} onChange={event => setValues({ ...values, reminderEnabled: event.target.checked })} />Нагадати до строку</label>
      <label className="field">За скільки хвилин<input type="number" min={1} max={43200} disabled={!values.reminderEnabled} value={values.reminderOffset} onChange={event => setValues({ ...values, reminderOffset: event.target.value })} /></label>
      <label className="checkbox"><input type="checkbox" checked={values.escalationEnabled} onChange={event => setValues({ ...values, escalationEnabled: event.target.checked })} />Нагадати про прострочення</label>
      <label className="field">Через скільки хвилин<input type="number" min={1} max={43200} disabled={!values.escalationEnabled} value={values.escalationDelay} onChange={event => setValues({ ...values, escalationDelay: event.target.value })} /></label>
    </div>{!!team.error && <ErrorNotice error={team.error} retry={() => void team.reload()} />}<div className="form-actions"><button className="button" disabled={pending || team.loading || !!team.error}>{pending ? 'Збереження…' : task ? 'Зберегти зміни' : 'Створити завдання'}</button><Link className="button secondary" to={task ? `/tasks/${task.id}` : '/tasks'}>Скасувати</Link></div></form></>;
}
