import { useEffect, useId, useRef, useState } from 'react';
import type { KeyboardEvent, PointerEvent, RefObject } from 'react';
import { Link } from 'react-router-dom';
import { states } from '../../lib/contracts';
import type { TaskRecord } from '../../lib/contracts';
import { getStateLabels } from '../../lib/domain';
import { formatDate, parseUtc } from '../../lib/dates';
import { useWorkCopy } from './workCopy';

type State = TaskRecord['state'];
type Drag = { id: number; target: State | null; mode: 'pointer' | 'keyboard'; x: number; y: number };
type Gesture = { id: number; pointerId: number; x: number; y: number };

function targetAt(board: HTMLDivElement | null, x: number, y: number): State | null {
  const column = document.elementFromPoint(x, y)?.closest<HTMLElement>('.board-column[data-state]');
  return column && board?.contains(column) ? column.dataset.state as State : null;
}

export function TaskBoard({ items, pending, loading, boardRef, changeState }: {
  items: TaskRecord[]; pending: ReadonlySet<number>; loading: boolean;
  boardRef: RefObject<HTMLDivElement | null>;
  changeState: (task: TaskRecord, state: State, control: HTMLElement) => Promise<void>;
}) {
  const { locale, copy, text } = useWorkCopy();
  const labels = getStateLabels(locale);
  const instructionsId = useId();
  const [drag, setDrag] = useState<Drag | null>(null);
  const active = useRef<Drag | null>(null);
  const gesture = useRef<Gesture | null>(null);
  const [canceled, setCanceled] = useState(false);
  const pointerActive = drag?.mode === 'pointer';
  function update(next: Drag | null) { active.current = next; setDrag(next); }
  function cancel() { gesture.current = null; update(null); setCanceled(true); }
  useEffect(() => {
    if (!pointerActive) return;
    let frame = 0;
    function scroll() {
      const current = active.current; const board = boardRef.current;
      if (!current || current.mode !== 'pointer' || !board) return;
      const bounds = board.getBoundingClientRect();
      const speed = (point: number, low: number, high: number) => point < low + 48
        ? -Math.min(14, (low + 48 - point) / 3) : point > high - 48 ? Math.min(14, (point - high + 48) / 3) : 0;
      if (current.y >= Math.max(0, bounds.top) && current.y <= Math.min(innerHeight, bounds.bottom))
        board.scrollLeft += speed(current.x, bounds.left, Math.min(innerWidth, bounds.right));
      const column = document.elementFromPoint(current.x, current.y)?.closest<HTMLElement>('.board-column[data-state]');
      if (column && board.contains(column)) {
        const box = column.getBoundingClientRect();
        column.scrollTop += speed(current.y, Math.max(0, box.top), Math.min(innerHeight, box.bottom));
      }
      const target = targetAt(board, current.x, current.y);
      if (target !== current.target) { const next = { ...current, target }; active.current = next; setDrag(next); }
      frame = requestAnimationFrame(scroll);
    }
    frame = requestAnimationFrame(scroll);
    return () => cancelAnimationFrame(frame);
  }, [pointerActive, boardRef]);
  useEffect(() => {
    function abort(event: globalThis.KeyboardEvent | FocusEvent) {
      if ('key' in event && event.key !== 'Escape') return;
      if (active.current || gesture.current) { gesture.current = null; active.current = null; setDrag(null); setCanceled(true); }
    }
    window.addEventListener('keydown', abort); window.addEventListener('blur', abort);
    return () => { window.removeEventListener('keydown', abort); window.removeEventListener('blur', abort); };
  }, []);
  function pointerDown(event: PointerEvent<HTMLButtonElement>, task: TaskRecord) {
    if (event.button !== 0 || !event.isPrimary || pending.has(task.id) || loading || active.current) return;
    event.currentTarget.focus(); setCanceled(false);
    gesture.current = { id: task.id, pointerId: event.pointerId, x: event.clientX, y: event.clientY };
    event.currentTarget.setPointerCapture(event.pointerId);
  }
  function pointerMove(event: PointerEvent<HTMLButtonElement>) {
    const start = gesture.current;
    if (!start || start.pointerId !== event.pointerId) return;
    if (loading || pending.has(start.id)) { cancel(); return; }
    if (!active.current && Math.hypot(event.clientX - start.x, event.clientY - start.y) < 6) return;
    event.preventDefault();
    update({ id: start.id, mode: 'pointer', target: targetAt(boardRef.current, event.clientX, event.clientY), x: event.clientX, y: event.clientY });
  }
  function pointerUp(event: PointerEvent<HTMLButtonElement>) {
    if (gesture.current?.pointerId !== event.pointerId) return;
    const current = active.current; const task = items.find(item => item.id === current?.id);
    const target = current ? targetAt(boardRef.current, event.clientX, event.clientY) : null;
    gesture.current = null; update(null);
    if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId);
    if (task && target && !loading) void changeState(task, target, event.currentTarget);
  }
  function activate(task: TaskRecord, control: HTMLButtonElement) {
    if (pending.has(task.id) || loading) return;
    const current = active.current;
    if (!current) {
      setCanceled(false); update({ id: task.id, target: task.state, mode: 'keyboard', x: 0, y: 0 });
    } else if (current.id === task.id && current.mode === 'keyboard') {
      update(null); if (current.target) void changeState(task, current.target, control);
    }
  }
  function keyboard(event: KeyboardEvent<HTMLButtonElement>, task: TaskRecord) {
    if (pending.has(task.id) || loading) return;
    const current = active.current;
    if (!current) {
      if (event.key !== ' ' && event.key !== 'Enter') return;
      event.preventDefault(); activate(task, event.currentTarget);
    } else if (current.id === task.id && current.mode === 'keyboard') {
      if (event.key === 'Escape' || event.key === 'Tab') { if (event.key === 'Escape') event.preventDefault(); cancel(); }
      else if (event.key === ' ' || event.key === 'Enter') {
        event.preventDefault(); activate(task, event.currentTarget);
      } else if (['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) {
        event.preventDefault();
        const direction = event.key === 'ArrowRight' || event.key === 'ArrowDown' ? 1 : -1;
        const index = event.key === 'Home' ? 0 : event.key === 'End' ? states.length - 1
          : Math.max(0, Math.min(states.length - 1, states.indexOf(current.target ?? task.state) + direction));
        const target = states[index]; update({ ...current, target });
        boardRef.current?.querySelector(`[data-state="${target}"]`)?.scrollIntoView?.({ block: 'nearest', inline: 'nearest', behavior: 'instant' });
      }
    }
  }
  const dragged = items.find(item => item.id === drag?.id);
  return <>
    <p id={instructionsId} className="board-instructions">{copy.dragInstructions}</p>
    <div className="sr-only" role="status" aria-live="polite" aria-atomic="true">{dragged && drag
      ? text('dragAnnouncement', { name: dragged.name, state: drag.target ? labels[drag.target] : copy.outsideBoard })
      : canceled ? copy.dragCanceled : ''}</div>
    <div ref={boardRef} className="board" role="region" aria-label={copy.taskBoard} tabIndex={0} aria-busy={loading || pending.size > 0}>
      {states.map(state => <section className="board-column" data-state={state} data-drop-target={drag?.target === state || undefined} key={state}>
        <div className="board-title"><h2>{labels[state]}</h2><span>{items.filter(task => task.state === state).length}</span></div>
        {items.filter(task => task.state === state).map(task => <article className="task-card" key={task.id} data-task-id={task.id} data-dragging={drag?.id === task.id || undefined} aria-busy={pending.has(task.id)}>
          <div className="task-card-heading"><h3><Link to={`/tasks/${task.id}`}>{task.name}</Link></h3>
            <button type="button" className="task-drag-handle" data-board-drag aria-label={text('dragTask', { name: task.name })}
              aria-describedby={instructionsId} aria-pressed={drag?.id === task.id} title={copy.dragHandle}
              disabled={pending.has(task.id) || loading} onPointerDown={event => pointerDown(event, task)} onPointerMove={pointerMove}
              onPointerUp={pointerUp} onPointerCancel={cancel} onLostPointerCapture={() => { if (gesture.current) cancel(); }}
              onBlur={() => { if (active.current?.mode === 'keyboard' && active.current.id === task.id) cancel(); }}
              onKeyDown={event => keyboard(event, task)} onClick={event => { if (event.detail === 0) activate(task, event.currentTarget); }}>
              <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"><circle cx="7" cy="5" r="1.5"/><circle cx="13" cy="5" r="1.5"/><circle cx="7" cy="10" r="1.5"/><circle cx="13" cy="10" r="1.5"/><circle cx="7" cy="15" r="1.5"/><circle cx="13" cy="15" r="1.5"/></svg>
            </button></div>
          <small className="muted">{task.performer ? `${task.performer.firstName} ${task.performer.lastName}` : text('assigneeNumber', { id: task.performerId })}</small>
          <span className={`due${task.dueDate && parseUtc(task.dueDate).getTime() < Date.now() && !['Done', 'Canceled'].includes(task.state) ? ' overdue' : ''}`}>{formatDate(task.dueDate)}</span>
          <select aria-label={text('statusForTask', { name: task.name })} value={task.state} disabled={pending.has(task.id)} onChange={event => void changeState(task, event.target.value as State, event.currentTarget)}>
            {states.map(value => <option key={value} value={value}>{labels[value]}</option>)}
          </select>
        </article>)}
        {drag && <div className="board-drop-hint" aria-hidden="true">{drag.target === state ? text('dropInColumn', { state: labels[state] }) : copy.dropTaskHere}</div>}
      </section>)}
    </div>
    {dragged && drag?.mode === 'pointer' && <div className="task-drag-preview" aria-hidden="true" style={{ left: Math.max(8, Math.min(drag.x + 14, innerWidth - 280)), top: Math.max(8, Math.min(drag.y + 14, innerHeight - 100)) }}>{dragged.name}</div>}
  </>;
}
