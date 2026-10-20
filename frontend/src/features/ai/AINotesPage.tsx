import { useEffect, useId, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { z } from 'zod';
import { Empty, ErrorNotice, Loading } from '../../components/Feedback';
import { ApiError, request } from '../../lib/api';
import { AIDraftSchema, AIStatusSchema, AICommitReceiptSchema, ProjectSchema, TeamSchema } from '../../lib/contracts';
import type { AIDraft, AIDraftItem, AICommitReceipt, TeamMember } from '../../lib/contracts';
import { formatDate, toLocalInput, toUtc } from '../../lib/dates';
import { browserTimeZone, canonicalTimeZone } from '../../lib/timeZone';
import { translateCopyText, useLocale } from '../../lib/i18n';
import { useLoad } from '../../lib/useLoad';
import { useSession } from '../../lib/session';
import { aiCopy, explainAiMarker } from './aiCopy';

interface RowEdit {
  selected: boolean; title: string; description: string; assignee: string; due: string;
  reminder: boolean; offset: string; escalation: boolean; delay: string;
}
interface ConfirmedItem {
  draftItemId: string; title: string; description: string; assigneeId: number; dueAtUtc: string | null;
  reminderEnabled: boolean; reminderOffsetMinutes: number | null; escalationEnabled: boolean; escalationDelayMinutes: number | null;
}
interface CommitSnapshot { draftId: string; body: { expectedVersion: string; items: ConfirmedItem[] }; key: string }
type Block = 'stale' | 'expired' | 'forbidden' | 'missing' | null;
type Errors = Record<string, string>;

function initialEdit(item: AIDraftItem): RowEdit {
  return { selected: false, title: item.title, description: item.description, assignee: item.proposedAssigneeId ? String(item.proposedAssigneeId) : '',
    due: toLocalInput(item.proposedDueAtUtc), reminder: false, offset: '', escalation: false, delay: '' };
}
function validMinutes(value: string) { return /^\d+$/.test(value) && Number(value) >= 1 && Number(value) <= 10080; }
function outcomeUnknown(error: unknown) { return !(error instanceof ApiError) || error.status === 0 || error.status >= 500; }

function AIFailure({ error, unknown, block, retry }: { error: unknown; unknown: boolean; block: Block; retry?: () => void }) {
  const { locale: uiLocale } = useLocale(); const m = aiCopy[uiLocale];
  const focus = useRef<HTMLDivElement>(null);
  useEffect(() => { if (error) focus.current?.focus(); }, [error]);
  if (!error) return null;
  const specific = unknown ? m.unknown : block === 'stale' ? m.stale : block === 'expired' ? m.expired : block === 'forbidden' ? m.forbidden
    : block === 'missing' ? m.missing : error instanceof ApiError && error.status === 429 ? m.quota : undefined;
  return <div ref={focus} tabIndex={-1}>
    {specific ? <div className="alert alert-error" role="alert"><p>{specific}</p>
      {error instanceof ApiError && error.traceId && <small>{m.trace}: {error.traceId.slice(0, 128)}</small>}
      {retry && <button className="button secondary" type="button" onClick={retry}>{m.retryExact}</button>}
    </div> : <ErrorNotice error={error} />}
  </div>;
}

function DraftRow({ item, edit, number, members, disabled, errors, timeZone, update }: {
  item: AIDraftItem; edit: RowEdit; number: number; members: TeamMember[]; disabled: boolean; errors: Errors; timeZone: string;
  update: (field: keyof RowEdit, value: string | boolean) => void;
}) {
  const { locale: uiLocale } = useLocale(); const m = aiCopy[uiLocale];
  const id = useId(); const locked = disabled || Boolean(item.materializedTaskId);
  const changed = edit.title !== item.title || edit.description !== item.description || edit.assignee !== (item.proposedAssigneeId ? String(item.proposedAssigneeId) : '') || edit.due !== toLocalInput(item.proposedDueAtUtc);
  const error = (field: string) => errors[field] ? <span id={`${id}-${field}-error`} className="field-error" role="alert">{translateCopyText(errors[field], aiCopy, uiLocale)}</span> : null;
  const fieldProps = (field: string) => ({ 'aria-invalid': Boolean(errors[field]), 'aria-describedby': errors[field] ? `${id}-${field}-error` : undefined });
  return <tr>
    <td><label className="checkbox" htmlFor={`${id}-selected`}><input id={`${id}-selected`} type="checkbox" aria-label={`${m.select} ${m.rowSelection} ${number}`}
      checked={edit.selected && !item.materializedTaskId} disabled={locked} onChange={event => update('selected', event.target.checked)} />{m.select}</label>
      <small>{m.row} {number}</small>{item.isSuggestion && <span className="badge">{m.suggestion}</span>}
      {item.materializedTaskId && <><p className="muted">{m.materialized}</p><Link to={`/tasks/${item.materializedTaskId}`}>{m.openTask}</Link></>}
    </td>
    <td><div className="field"><label htmlFor={`${id}-title`}>{m.titleField}</label><input id={`${id}-title`} aria-label={`${m.titleField} ${m.rowField} ${number}`}
      value={edit.title} disabled={locked} required maxLength={100} onChange={event => update('title', event.target.value)} {...fieldProps('title')} />{error('title')}</div>
      <div className="field"><label htmlFor={`${id}-description`}>{m.description}</label><textarea id={`${id}-description`} aria-label={`${m.description} ${m.rowField} ${number}`}
        value={edit.description} disabled={locked} maxLength={1000} onChange={event => update('description', event.target.value)} {...fieldProps('description')} />{error('description')}</div>
      {changed && <span className="badge">{m.edited}</span>}
    </td>
    <td><div className="field"><label htmlFor={`${id}-assignee`}>{m.assignee}</label><select id={`${id}-assignee`} aria-label={`${m.assignee} ${m.rowField} ${number}`}
      value={edit.assignee} disabled={locked || !members.length} onChange={event => update('assignee', event.target.value)} {...fieldProps('assignee')}>
      <option value="">{m.chooseAssignee}</option>{members.map(member => <option key={member.id} value={member.id}>{member.firstName} {member.lastName} · {member.email ?? member.userName}</option>)}
    </select>{error('assignee')}</div>
      <div className="field"><label htmlFor={`${id}-due`}>{m.deadline}</label><input id={`${id}-due`} type="datetime-local" aria-label={`${m.deadline} ${m.rowField} ${number}`}
        value={edit.due} disabled={locked} onChange={event => update('due', event.target.value)} {...fieldProps('due')} />{error('due')}</div>
      <label className="checkbox"><input type="checkbox" checked={edit.reminder} disabled={locked} onChange={event => update('reminder', event.target.checked)} />{m.reminder}</label>
      <div className="field"><label htmlFor={`${id}-offset`}>{m.offset}</label><input id={`${id}-offset`} type="number" min={1} max={10080} value={edit.offset} disabled={locked || !edit.reminder}
        onChange={event => update('offset', event.target.value)} {...fieldProps('offset')} />{error('offset')}</div>
      <label className="checkbox"><input type="checkbox" checked={edit.escalation} disabled={locked} onChange={event => update('escalation', event.target.checked)} />{m.escalation}</label>
      <div className="field"><label htmlFor={`${id}-delay`}>{m.delay}</label><input id={`${id}-delay`} type="number" min={1} max={10080} value={edit.delay} disabled={locked || !edit.escalation}
        onChange={event => update('delay', event.target.value)} {...fieldProps('delay')} />{error('delay')}</div>
    </td>
    <td><h3>{m.source}</h3>{item.sourceQuote ? <blockquote className="ai-source">{item.sourceQuote}</blockquote> : <p className="muted">{m.sourceMissing}</p>}
      <dl className="ai-source-meta"><div><dt>{m.originalAssignee}</dt><dd>{item.originalAssigneeText ?? m.notSpecified}</dd></div>
        <div><dt>{m.originalDue}</dt><dd>{item.originalDueText ?? m.notSpecified}</dd></div></dl>
      {!!item.interpretations.length && <><h3>{m.interpretations}</h3><ul>{item.interpretations.map((marker, index) => <li key={index}>{explainAiMarker(marker, uiLocale, timeZone)}</li>)}</ul></>}
      {!!item.clarificationReasons.length && <><h3>{m.clarification}</h3><ul>{item.clarificationReasons.map((marker, index) => <li key={index}>{explainAiMarker(marker, uiLocale, timeZone)}</li>)}</ul></>}
    </td>
  </tr>;
}

export function AINotesPage() {
  const { locale: uiLocale, intlLocale } = useLocale(); const m = aiCopy[uiLocale];
  const session = useSession(); const [query, setQuery] = useSearchParams();
  const status = useLoad('ai-status', signal => request('/api/ai/status', AIStatusSchema, { signal }));
  const projects = useLoad(`ai-projects-${session.user?.id}`, signal => request('/api/projects', z.array(ProjectSchema), { signal }));
  const [notes, setNotes] = useState(''); const [projectId, setProjectId] = useState(''); const [noteDate, setNoteDate] = useState('');
  const browserZone = browserTimeZone();
  // Response language is evidence/request state, independent of UI preference.
  const [zone, setZone] = useState(browserZone); const [locale, setLocale] = useState<'uk' | 'en'>('uk');
  const [draft, setDraft] = useState<AIDraft | null>(null); const [edits, setEdits] = useState<Record<string, RowEdit>>({});
  const [inputErrors, setInputErrors] = useState<Errors>({}); const [rowErrors, setRowErrors] = useState<Record<string, Errors>>({});
  const [failure, setFailure] = useState<unknown>(); const [block, setBlock] = useState<Block>(null);
  const [pending, setPending] = useState(false); const [restoring, setRestoring] = useState(false); const [message, setMessage] = useState('');
  const [unknown, setUnknown] = useState<CommitSnapshot | null>(null); const [restored, setRestored] = useState(false);
  const busy = useRef(false); const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  useEffect(() => {
    if (Object.keys(inputErrors).length || Object.values(rowErrors).some(errors => Object.keys(errors).length))
      document.querySelector<HTMLInputElement>('.ai-notes [aria-invalid="true"], .ai-review [aria-invalid="true"]')?.focus();
  }, [inputErrors, rowErrors, failure]);
  const selectedProject = projects.data?.find(project => project.id === Number(projectId));
  const draftProject = projects.data?.find(project => project.id === draft?.projectId);
  const directoryBlocked = block === 'forbidden' || block === 'missing';
  const teamId = directoryBlocked ? undefined : (draftProject ?? selectedProject)?.teamId;
  const team = useLoad(`ai-assignees-${teamId ?? 'none'}`, signal => teamId ? request(`/api/teams/${teamId}`, TeamSchema, { signal }) : Promise.resolve(null));
  const members = directoryBlocked ? [] : team.data?.users ?? [];
  const routeId = query.get('draftId');

  function adopt(value: AIDraft, preserve = false) {
    if (value.ownerId !== session.user?.id) throw new ApiError(404);
    setDraft(value); setProjectId(String(value.projectId)); setZone(canonicalTimeZone(value.timeZoneId)); setLocale(value.locale);
    setEdits(previous => Object.fromEntries(value.items.map(item => [item.draftItemId,
      preserve && previous[item.draftItemId] ? { ...previous[item.draftItemId], selected: !item.materializedTaskId && previous[item.draftItemId].selected } : initialEdit(item)])));
  }
  useEffect(() => {
    if (!routeId || routeId === draft?.id) return;
    const controller = new AbortController();
    if (!z.string().uuid().safeParse(routeId).success) { setFailure(new ApiError(400)); setBlock('missing'); return; }
    setRestoring(true);
    void request(`/api/ai/task-extractions/${routeId}`, AIDraftSchema, { signal: controller.signal }).then(value => {
      if (!controller.signal.aborted) {
        if (value.ownerId !== session.user?.id) { setFailure(new ApiError(404)); setBlock('missing'); return; }
        setDraft(value); setProjectId(String(value.projectId)); setZone(canonicalTimeZone(value.timeZoneId)); setLocale(value.locale);
        setEdits(Object.fromEntries(value.items.map(item => [item.draftItemId, initialEdit(item)]))); setRestored(true); setRestoring(false);
      }
    }).catch(error => { if (!controller.signal.aborted) { setFailure(error); setBlock(error instanceof ApiError && error.status === 410 ? 'expired' : 'missing'); } })
      .finally(() => { if (!controller.signal.aborted) setRestoring(false); });
    return () => controller.abort();
  }, [routeId, draft?.id, session.user?.id]);

  async function start(action: () => Promise<void>) {
    if (busy.current) return;
    busy.current = true; setPending(true); setFailure(undefined); setMessage('');
    try { await action(); }
    catch (error) {
      if (alive.current) {
        setFailure(error);
        if (error instanceof ApiError && error.status === 403) { setBlock('forbidden'); await session.refresh(); }
      }
    }
    finally { busy.current = false; if (alive.current) setPending(false); }
  }
  async function extract(event: FormEvent) {
    event.preventDefault(); if (unknown || busy.current) return;
    const errors: Errors = {};
    if (!notes.trim()) errors.notes = m.notesRequired;
    if (notes.length > (status.data?.maxInputCharacters ?? 20_000)) errors.notes = m.notesLimit;
    if (!selectedProject) errors.project = m.projectRequired;
    try { new Intl.DateTimeFormat(intlLocale, { timeZone: zone }).format(new Date()); } catch { errors.zone = m.zoneInvalid; }
    if (noteDate && !z.iso.date().safeParse(noteDate).success) errors.noteDate = m.noteDateInvalid;
    setInputErrors(errors); if (Object.keys(errors).length || !status.data?.available) return;
    await start(async () => {
      const value = await request('/api/ai/task-extractions', AIDraftSchema, { method: 'POST', body: {
        notesText: notes, projectId: selectedProject!.id, ...(noteDate ? { noteDate } : {}), timeZoneId: zone, locale,
      } });
      adopt(value); setBlock(null); setUnknown(null); setRowErrors({}); setRestored(false); setQuery({ draftId: value.id }, { replace: true });
    });
  }

  function update(itemId: string, field: keyof RowEdit, value: string | boolean) {
    if (unknown || busy.current) return;
    setEdits(previous => ({ ...previous, [itemId]: { ...previous[itemId], [field]: value } }));
    setRowErrors(previous => ({ ...previous, [itemId]: {} })); setMessage('');
  }
  function confirmedItems(): ConfirmedItem[] | null {
    if (!draft) return null;
    const errors: Record<string, Errors> = {}; const confirmed: ConfirmedItem[] = [];
    for (const item of draft.items) {
      const edit = edits[item.draftItemId]; if (!edit?.selected || item.materializedTaskId) continue;
      const issues: Errors = {};
      if (!edit.title.trim() || edit.title.trim().length > 100) issues.title = m.requiredTitle;
      if (edit.description.length > 1000) issues.description = m.descriptionLimit;
      if (!members.some(member => String(member.id) === edit.assignee)) issues.assignee = m.assigneeRequired;
      let dueAtUtc: string | null = null;
      try { dueAtUtc = toUtc(edit.due); } catch { issues.due = m.dueInvalid; }
      if ((edit.reminder || edit.escalation) && !dueAtUtc) issues.due = m.dueRequired;
      if (edit.reminder && !validMinutes(edit.offset)) issues.offset = m.minutesInvalid;
      if (edit.escalation && !validMinutes(edit.delay)) issues.delay = m.minutesInvalid;
      if (Object.keys(issues).length) errors[item.draftItemId] = issues;
      confirmed.push({ draftItemId: item.draftItemId, title: edit.title.trim(), description: edit.description, assigneeId: Number(edit.assignee), dueAtUtc,
        reminderEnabled: edit.reminder, reminderOffsetMinutes: edit.reminder ? Number(edit.offset) : null,
        escalationEnabled: edit.escalation, escalationDelayMinutes: edit.escalation ? Number(edit.delay) : null });
    }
    setRowErrors(errors);
    if (Object.keys(errors).length) {
      return null;
    }
    return confirmed.length ? confirmed : null;
  }
  function applyReceipt(receipt: AICommitReceipt, snapshot: CommitSnapshot) {
    const wanted = new Set(snapshot.body.items.map(item => item.draftItemId));
    if (receipt.draftId !== snapshot.draftId || receipt.items.length !== wanted.size || receipt.items.some(item => !wanted.delete(item.draftItemId)))
      throw new ApiError(502);
    const materialized = new Map(receipt.items.map(item => [item.draftItemId, item.taskId]));
    setDraft(previous => previous ? { ...previous, version: receipt.draftVersion, items: previous.items.map(item => materialized.has(item.draftItemId) ? { ...item, materializedTaskId: materialized.get(item.draftItemId)! } : item) } : null);
    setEdits(previous => Object.fromEntries(Object.entries(previous).map(([id, edit]) => [id, { ...edit, selected: materialized.has(id) ? false : edit.selected }])));
    setUnknown(null); setBlock(null); setFailure(undefined); setRowErrors({}); setMessage(m.saved);
  }
  async function sendCommit(snapshot: CommitSnapshot) {
    await start(async () => {
      try {
        const receipt = await request(`/api/ai/task-extractions/${snapshot.draftId}/commit`, AICommitReceiptSchema,
          { method: 'POST', body: snapshot.body, headers: { 'Idempotency-Key': snapshot.key } });
        applyReceipt(receipt, snapshot);
      } catch (error) {
        if (outcomeUnknown(error)) setUnknown(snapshot);
        else {
          setUnknown(null);
          if (error instanceof ApiError) {
            const needsReview = error.status === 409 || error.status === 428 || (error.status === 400 && !Object.keys(error.fieldErrors).length);
            setBlock(needsReview ? 'stale' : error.status === 410 ? 'expired' : error.status === 403 ? 'forbidden' : error.status === 404 ? 'missing' : null);
            if (error.status === 400) {
              const errors: Record<string, Errors> = {};
              for (const field of Object.keys(error.fieldErrors)) {
                const match = /^items\[(\d+)\][.](title|description|assigneeId|dueAtUtc|reminderOffsetMinutes|escalationDelayMinutes)$/i.exec(field);
                if (!match) continue;
                const item = snapshot.body.items[Number(match[1])]; if (!item) continue;
                const name = ({ assigneeid: 'assignee', dueatutc: 'due', reminderoffsetminutes: 'offset', escalationdelayminutes: 'delay' } as Record<string, string>)[match[2].toLowerCase()] ?? match[2].toLowerCase();
                errors[item.draftItemId] = { ...errors[item.draftItemId], [name]: m.serverFieldError };
              }
              setRowErrors(errors);
            }
          }
        }
        throw error;
      }
    });
  }
  async function commit() {
    if (!draft || unknown || block || busy.current) return;
    const items = confirmedItems(); if (!items) return;
    await sendCommit({ draftId: draft.id, body: { expectedVersion: draft.version, items }, key: crypto.randomUUID() });
  }
  async function reviewCurrent() {
    if (!draft || unknown) return;
    await start(async () => {
      try { const value = await request(`/api/ai/task-extractions/${draft.id}`, AIDraftSchema); adopt(value, true); setBlock(null); setMessage(m.refreshed); await projects.reload(); await team.reload(); }
      catch (error) { if (error instanceof ApiError && error.status === 410) setBlock('expired'); if (error instanceof ApiError && error.status === 403) setBlock('forbidden'); throw error; }
    });
  }
  async function deleteDraft() {
    if (!draft || unknown || busy.current) return;
    await start(async () => {
      await request(`/api/ai/task-extractions/${draft.id}`, undefined, { method: 'DELETE' });
      setDraft(null); setEdits({}); setNotes(''); setRowErrors({}); setBlock(null); setMessage(m.deleted); setQuery({}, { replace: true });
    });
  }
  const inputLocked = pending || Boolean(unknown);
  const rowLocked = inputLocked || block === 'forbidden' || block === 'expired' || block === 'missing' || team.loading || Boolean(team.error);
  const selectedCount = draft?.items.filter(item => !item.materializedTaskId && edits[item.draftItemId]?.selected).length ?? 0;
  const inputId = useId();
  const inputError = (field: string) => inputErrors[field] ? <span id={`${inputId}-${field}-error`} className="field-error" role="alert">{translateCopyText(inputErrors[field], aiCopy, uiLocale)}</span> : null;
  const inputProps = (field: string) => ({ 'aria-invalid': Boolean(inputErrors[field]), 'aria-describedby': inputErrors[field] ? `${inputId}-${field}-error` : undefined });
  return <>
    <header className="page-header"><div><p className="eyebrow">{m.eyebrow}</p><h1>{m.title}</h1><p>{m.subtitle}</p></div>
      <Link className="button secondary" to={projectId ? `/tasks/new?projectId=${projectId}` : '/tasks/new'}>{m.manual}</Link></header>
    {status.loading || projects.loading ? <Loading /> : null}
    {status.error && <ErrorNotice error={status.error} retry={() => void status.reload()} />}
    {projects.error && <ErrorNotice error={projects.error} retry={() => void projects.reload()} />}
    {status.data && !status.data.available && <p className="alert alert-warning">{m.unavailable}</p>}
    {status.data?.provider === 'fake' && <p className="alert alert-warning">{m.fake}</p>}
    <AIFailure error={failure} unknown={Boolean(unknown)} block={block} />
    {message && <p className="alert" role="status">{translateCopyText(message, aiCopy, uiLocale)}</p>}
    {!projects.loading && <section className="form-panel ai-notes" aria-label={m.inputRegion}><form noValidate onSubmit={event => void extract(event)} aria-busy={pending}>
      <div className="form-grid">
        <div className="field span-full"><label htmlFor={`${inputId}-notes`}>{m.notes}</label><textarea id={`${inputId}-notes`} value={notes} disabled={inputLocked}
          maxLength={status.data?.maxInputCharacters ?? 20_000} onChange={event => { setNotes(event.target.value); setInputErrors({}); }} {...inputProps('notes')} />{inputError('notes')}
          <p className="muted">{notes.length}/{status.data?.maxInputCharacters ?? '—'} {m.chars}. {m.storageNotice}</p></div>
        <div className="field"><label htmlFor={`${inputId}-project`}>{m.project}</label><select id={`${inputId}-project`} value={projectId} disabled={inputLocked || directoryBlocked}
          onChange={event => { setProjectId(event.target.value); setInputErrors({}); }} {...inputProps('project')}>
          <option value="">{m.chooseProject}</option>{!directoryBlocked && projects.data?.map(project => <option value={project.id} key={project.id}>{project.name}</option>)}
        </select>{inputError('project')}</div>
        <div className="field"><label htmlFor={`${inputId}-noteDate`}>{m.noteDate}</label><input id={`${inputId}-noteDate`} type="date" value={noteDate} disabled={inputLocked}
          onChange={event => { setNoteDate(event.target.value); setInputErrors({}); }} {...inputProps('noteDate')} />{inputError('noteDate')}</div>
        <div className="field"><label htmlFor={`${inputId}-zone`}>{m.zone}</label><input id={`${inputId}-zone`} value={zone} disabled={inputLocked} maxLength={100}
          onChange={event => { setZone(event.target.value); setInputErrors({}); }} {...inputProps('zone')} /><small className="muted">{m.zoneHelp}</small>{inputError('zone')}</div>
        <div className="field"><label htmlFor={`${inputId}-locale`}>{m.locale}</label><select id={`${inputId}-locale`} value={locale} disabled={inputLocked} onChange={event => setLocale(event.target.value as 'uk' | 'en')}>
          <option value="uk">{m.uk}</option><option value="en">{m.en}</option></select></div>
      </div><div className="form-actions"><button className="button" type="submit" disabled={inputLocked || !status.data?.available || !projects.data?.length || directoryBlocked}>
        {pending ? m.pending : m.extract}</button></div>
    </form></section>}
    {restoring && <Loading />}
    {draft && <section className="ai-review" aria-labelledby="ai-review-heading"><header className="page-header"><div><h2 id="ai-review-heading">{m.review}</h2><p>{m.reviewHelp}</p></div>
      <button className="button danger" type="button" disabled={inputLocked} onClick={() => void deleteDraft()}>{m.delete}</button></header>
      <p className="muted">{m.reference}: {draft.referenceDate} · {m.sourceZone}: {canonicalTimeZone(draft.timeZoneId)} · {m.expires}: {formatDate(draft.expiresAtUtc)}</p>
      <p className="muted">{m.browserZone}: {browserZone}. {m.maxItems}: {status.data?.maxItems ?? '—'}.</p>
      {restored && <p className="muted">{m.noWholeNotes}</p>}
      {!!team.error && !directoryBlocked && <ErrorNotice error={team.error} retry={() => void team.reload()} />}
      {block === 'stale' && <button className="button secondary" type="button" disabled={pending} onClick={() => void reviewCurrent()}>{m.reviewLatest}</button>}
      {block === 'forbidden' && <button className="button secondary" type="button" disabled={pending} onClick={() => void reviewCurrent()}>{m.reviewAccess}</button>}
      {unknown && <button className="button secondary" type="button" disabled={pending} onClick={() => void sendCommit(unknown)}>{pending ? m.pending : m.retryExact}</button>}
      {!draft.items.length ? <Empty title={m.empty}><p>{m.emptyHelp}</p></Empty> : <>
        <div className="panel table-wrap" role="region" aria-label={m.proposalTable} tabIndex={0}><table className="ai-review-table"><thead><tr><th scope="col">{m.selection}</th><th scope="col">{m.task}</th><th scope="col">{m.assignee} / {m.deadline}</th><th scope="col">{m.evidence}</th></tr></thead>
          <tbody>{draft.items.map((item, index) => <DraftRow key={item.draftItemId} item={item} edit={edits[item.draftItemId] ?? initialEdit(item)} number={index + 1}
            members={members} disabled={rowLocked} errors={rowErrors[item.draftItemId] ?? {}} timeZone={canonicalTimeZone(draft.timeZoneId)} update={(field, value) => update(item.draftItemId, field, value)} />)}</tbody></table></div>
        <div className="form-actions"><button className="button" type="button" disabled={rowLocked || Boolean(block) || !selectedCount} onClick={() => void commit()}>{pending ? m.pending : m.commit}</button>
          <p className="muted">{m.selected}: {selectedCount}</p></div>
      </>}
    </section>}
  </>;
}
