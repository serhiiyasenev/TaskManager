import { useEffect, useId, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { z } from 'zod';
import { ErrorNotice, Empty, Loading } from '../../components/Feedback';
import { ApiError, request } from '../../lib/api';
import { TeamSchema } from '../../lib/contracts';
import type { Team, TeamMember } from '../../lib/contracts';
import { useLoad } from '../../lib/useLoad';
import { useSession } from '../../lib/session';
import { copy as m } from './teamsCopy';

const TeamBasicSchema = TeamSchema.omit({ users: true });
const InvitationSchema = z.object({
  id: z.string().uuid(), email: z.string(), createdAtUtc: z.string(), expiresAtUtc: z.string(),
  acceptedAtUtc: z.string().nullable(), revokedAtUtc: z.string().nullable(),
});
const CreatedInvitationSchema = z.object({
  id: z.string().uuid(), email: z.string(), token: z.string().min(32).max(128).regex(/^[A-Za-z0-9_-]+$/), expiresAtUtc: z.string(),
});
type CreatedInvitation = z.infer<typeof CreatedInvitationSchema>;
type MutationKind = 'rename' | 'invite' | 'revoke' | 'remove' | 'leave' | 'disband' | 'copy';

function useMutation(onForbidden?: () => Promise<void>) {
  const [pending, setPending] = useState(false);
  const [failure, setFailure] = useState<unknown>();
  const [kind, setKind] = useState<MutationKind>();
  const [message, setMessage] = useState('');
  const busy = useRef(false); const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  async function run(actionKind: MutationKind, action: () => Promise<void>, success = '') {
    if (busy.current) return;
    busy.current = true; setPending(true); setFailure(undefined); setMessage(''); setKind(actionKind);
    try { await action(); if (alive.current) setMessage(success); }
    catch (error) {
      if (alive.current) setFailure(error);
      if (error instanceof ApiError && error.status === 403) await onForbidden?.();
    } finally { busy.current = false; if (alive.current) setPending(false); }
  }
  return { pending, failure, kind, message, run };
}

function MutationError({ error, kind }: { error: unknown; kind?: MutationKind }) {
  const focus = useRef<HTMLDivElement>(null);
  useEffect(() => { if (error) focus.current?.focus(); }, [error]);
  if (!error) return null;
  const special = kind === 'disband' && error instanceof ApiError && error.status === 409 ? m.disbandConflict
    : kind === 'copy' ? m.copyFailed : undefined;
  return <div tabIndex={-1} ref={focus}>
    {special ? <div className="alert alert-error" role="alert"><p>{special}</p>
      {error instanceof ApiError && error.traceId && <small>Код звернення: {error.traceId}</small>}</div> : <ErrorNotice error={error} />}
  </div>;
}

function NameField({ value, set, error }: { value: string; set: (value: string) => void; error: string }) {
  const id = useId();
  return <div className="field"><label htmlFor={id}>{m.name}</label>
    <input id={id} value={value} onChange={event => set(event.target.value)} required maxLength={100} aria-invalid={Boolean(error)} aria-describedby={error ? `${id}-error` : undefined} />
    {error && <span id={`${id}-error`} className="field-error" role="alert">{error}</span>}
  </div>;
}

export function TeamsPage() {
  const session = useSession(); const navigate = useNavigate();
  const teams = useLoad('teams', signal => request('/api/teams', z.array(TeamSchema), { signal }));
  const mutation = useMutation(async () => { await session.refresh(); });
  const [name, setName] = useState(''); const [nameError, setNameError] = useState('');
  const eligible = session.user?.emailConfirmed && (session.user.teamId === null || session.user.roles.includes('admin'));
  async function create(event: FormEvent) {
    event.preventDefault(); if (!name.trim() || name.trim().length > 100) { setNameError(m.invalidName); return; }
    setNameError('');
    await mutation.run('rename', async () => {
      const created = await request('/api/teams', TeamBasicSchema, { method: 'POST', body: { name: name.trim() } });
      await session.refresh(); navigate(`/teams/${created.id}`);
    }, m.created);
  }
  return <>
    <header className="page-header"><div><p className="eyebrow">Спільна робота</p><h1>{m.title}</h1><p>{m.subtitle}</p></div></header>
    <MutationError error={mutation.failure} />
    {teams.loading ? <Loading /> : teams.error ? <ErrorNotice error={teams.error} retry={() => void teams.reload()} />
      : teams.data?.length ? <div className="panel"><div className="panel-body"><ul className="member-list">
        {teams.data.map(team => <li className="member" key={team.id}><div><Link to={`/teams/${team.id}`}>{team.name}</Link><p className="muted">{m.memberCount}: {team.users.length}</p></div></li>)}
      </ul></div></div> : <Empty title={m.empty}><p>{m.emptyText}</p></Empty>}
    {eligible ? <section className="form-panel" aria-labelledby="create-team-heading"><h2 id="create-team-heading">{m.create}</h2>
      <form className="form-grid" noValidate onSubmit={event => void create(event)} aria-busy={mutation.pending}>
        <NameField value={name} set={value => { setName(value); setNameError(''); }} error={nameError} />
        <div className="form-actions"><button className="button" type="submit" disabled={mutation.pending}>{mutation.pending ? m.pending : m.create}</button></div>
      </form>
    </section> : <p className="muted">{session.user?.emailConfirmed ? m.currentTeam : m.confirmed}</p>}
  </>;
}

function ConfirmAction({ kind, member, pending, cancel, confirm }: {
  kind: 'remove' | 'leave' | 'disband'; member?: TeamMember; pending: boolean; cancel: () => void; confirm: () => void;
}) {
  const id = useId();
  const title = kind === 'remove' ? `${m.remove}: ${member?.firstName} ${member?.lastName}` : kind === 'leave' ? m.leave : m.disband;
  const warning = kind === 'remove' ? m.removeWarning : kind === 'leave' ? m.leaveWarning : m.disbandWarning;
  const label = kind === 'remove' ? m.removeConfirm : kind === 'leave' ? m.leaveConfirm : m.disbandConfirm;
  const focus = useRef<HTMLButtonElement>(null);
  useEffect(() => { focus.current?.focus(); }, []);
  return <section className="alert alert-warning" aria-labelledby={id}><h3 id={id}>{title}</h3><p>{warning}</p><div className="actions">
    <button className="button secondary" ref={focus} type="button" disabled={pending} onClick={cancel}>{m.cancel}</button>
    <button className="button danger" type="button" disabled={pending} onClick={confirm}>{pending ? m.pending : label}</button>
  </div></section>;
}

function utcDate(value: string) { return new Date(/(?:Z|[+-]\d{2}:\d{2})$/i.test(value) ? value : `${value}Z`); }
function formatDate(value: string) {
  const date = utcDate(value);
  return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat('uk-UA', { dateStyle: 'medium', timeStyle: 'short' }).format(date);
}

function InvitationManagement({ teamId, mutation, fresh, setFresh }: {
  teamId: number; mutation: ReturnType<typeof useMutation>; fresh: CreatedInvitation | null; setFresh: (value: CreatedInvitation | null) => void;
}) {
  const invitations = useLoad(`invitations-${teamId}`, signal => request(`/api/teams/${teamId}/invitations`, z.array(InvitationSchema), { signal }));
  const [email, setEmail] = useState(''); const [emailError, setEmailError] = useState(''); const emailId = useId();
  async function invite(event: FormEvent) {
    event.preventDefault(); if (!z.email().safeParse(email.trim()).success) { setEmailError(m.invalidEmail); return; }
    setEmailError('');
    await mutation.run('invite', async () => {
      const invitation = await request(`/api/teams/${teamId}/invitations`, CreatedInvitationSchema, { method: 'POST', body: { email: email.trim() } });
      setFresh(invitation); setEmail(''); await invitations.reload();
    });
  }
  const link = fresh ? `${window.location.origin}/invite?token=${encodeURIComponent(fresh.token)}` : '';
  return <section className="panel" aria-labelledby="team-invitations-heading"><div className="panel-heading"><h2 id="team-invitations-heading">{m.invitations}</h2></div><div className="panel-body">
    <p className="muted">{m.inviteHelp}</p>
    <form className="form-grid" noValidate onSubmit={event => void invite(event)} aria-busy={mutation.pending}>
      <div className="field"><label htmlFor={emailId}>{m.email}</label>
        <input id={emailId} type="email" value={email} onChange={event => { setEmail(event.target.value); setEmailError(''); }} required maxLength={256} autoComplete="off"
          aria-invalid={Boolean(emailError)} aria-describedby={emailError ? `${emailId}-error` : undefined} />
        {emailError && <span id={`${emailId}-error`} className="field-error" role="alert">{emailError}</span>}
      </div><div className="form-actions"><button className="button" type="submit" disabled={mutation.pending}>{mutation.pending ? m.pending : m.invite}</button></div>
    </form>
    {fresh && <section className="alert" aria-labelledby="new-invitation-heading"><h3 id="new-invitation-heading">{m.privateLink}</h3><p>{m.oneTime}</p>
      <p>{fresh.email} · {m.expires}: {formatDate(fresh.expiresAtUtc)}</p>
      <div className="field"><label htmlFor="fresh-invitation-link">Посилання для запрошеної людини</label>
        <input id="fresh-invitation-link" readOnly value={link} onFocus={event => event.target.select()} /></div>
      <div className="form-actions"><button className="button" type="button" disabled={mutation.pending} onClick={() => { void mutation.run('copy', async () => { await navigator.clipboard.writeText(link); }, m.copied); }}>{m.copy}</button>
        <button className="button secondary" type="button" disabled={mutation.pending} onClick={() => setFresh(null)}>{m.hideLink}</button></div>
    </section>}
    {invitations.loading ? <Loading /> : invitations.error ? <ErrorNotice error={invitations.error} retry={() => void invitations.reload()} />
      : !invitations.data?.length ? <Empty title={m.noInvitations} /> : <ul className="member-list">
        {invitations.data.map(invitation => {
          const expired = utcDate(invitation.expiresAtUtc).getTime() <= Date.now();
          const status = invitation.revokedAtUtc ? m.revoked : invitation.acceptedAtUtc ? m.accepted : expired ? m.expired : m.active;
          return <li className="member" key={invitation.id}><div><strong>{invitation.email}</strong><p className="muted">{m.expires}: {formatDate(invitation.expiresAtUtc)}</p><span className="badge">{status}</span></div>
            {!invitation.revokedAtUtc && !invitation.acceptedAtUtc && !expired && <button className="button secondary compact" type="button" disabled={mutation.pending}
              aria-label={`${m.revoke} ${invitation.email}`} onClick={() => { void mutation.run('revoke', async () => {
                await request(`/api/teams/${teamId}/invitations/${invitation.id}`, undefined, { method: 'DELETE' });
                if (fresh?.id === invitation.id) setFresh(null); await invitations.reload();
              }, m.revokedSuccess); }}>{m.revoke}</button>}
          </li>;
        })}
      </ul>}
  </div></section>;
}

function TeamContent({ team, reload }: { team: Team; reload: () => Promise<void> }) {
  const session = useSession(); const navigate = useNavigate();
  const [denied, setDenied] = useState(false); const [fresh, setFresh] = useState<CreatedInvitation | null>(null);
  const [name, setName] = useState(team.name); const [nameError, setNameError] = useState('');
  const [confirmation, setConfirmation] = useState<{ kind: 'remove' | 'leave' | 'disband'; member?: TeamMember } | null>(null);
  const owner = session.user?.id === team.ownerId;
  const manages = !denied && Boolean(session.user && (session.user.roles.includes('admin') || (owner && session.user.teamId === team.id)));
  const member = session.user?.teamId === team.id;
  const mutation = useMutation(async () => { setDenied(true); setFresh(null); setConfirmation(null); await session.refresh(); await reload(); });
  async function rename(event: FormEvent) {
    event.preventDefault(); if (!name.trim() || name.trim().length > 100) { setNameError(m.invalidName); return; }
    setNameError(''); await mutation.run('rename', async () => {
      await request(`/api/teams/${team.id}`, TeamBasicSchema, { method: 'PUT', body: { name: name.trim() } }); await reload();
    }, m.renamed);
  }
  async function confirm() {
    if (!confirmation) return;
    const { kind, member: target } = confirmation;
    await mutation.run(kind, async () => {
      if (kind === 'leave') await request('/api/teams/leave', undefined, { method: 'POST' });
      else if (kind === 'disband') await request(`/api/teams/${team.id}`, undefined, { method: 'DELETE' });
      else if (target) await request(`/api/teams/${team.id}/members/${target.id}`, undefined, { method: 'DELETE' });
      setConfirmation(null); await session.refresh();
      if (kind === 'leave' || kind === 'disband') navigate('/teams'); else await reload();
    }, confirmation.kind === 'remove' ? m.removed : '');
  }
  return <>
    <header className="page-header"><div><p className="eyebrow">Спільна робота</p><h1>{team.name}</h1><p>{m.memberCount}: {team.users.length}</p></div><Link className="button secondary" to="/teams">{m.back}</Link></header>
    <MutationError error={mutation.failure} kind={mutation.kind} />{mutation.message && <p className="alert" role="status">{mutation.message}</p>}
    {denied && <p className="muted">{m.rightsChanged}</p>}
    {confirmation && <ConfirmAction {...confirmation} pending={mutation.pending} cancel={() => setConfirmation(null)} confirm={() => void confirm()} />}
    <section className="panel" aria-labelledby="team-members-heading"><div className="panel-heading"><h2 id="team-members-heading">{m.members}</h2></div><div className="panel-body"><ul className="member-list">
      {team.users.map(user => <li className="member" key={user.id}><div><strong>{`${user.firstName} ${user.lastName}`.trim() || user.userName}</strong><p className="muted">{user.email}</p>
        {team.ownerId === user.id && <span className="badge">{m.owner}</span>} {session.user?.id === user.id && <span className="badge">{m.you}</span>}</div>
        {manages && user.id !== team.ownerId && user.id !== session.user?.id && <button className="button danger compact" type="button" disabled={mutation.pending} aria-label={`${m.remove} ${user.firstName} ${user.lastName}`}
          onClick={() => setConfirmation({ kind: 'remove', member: user })}>{m.remove}</button>}
      </li>)}
    </ul></div></section>
    {manages && <>
      <section className="form-panel" aria-labelledby="team-management-heading"><h2 id="team-management-heading">{m.ownerTools}</h2>
        <form className="form-grid" noValidate onSubmit={event => void rename(event)} aria-busy={mutation.pending}>
          <NameField value={name} set={value => { setName(value); setNameError(''); }} error={nameError} />
          <div className="form-actions"><button className="button" type="submit" disabled={mutation.pending}>{mutation.pending ? m.pending : m.saveName}</button></div>
        </form>
      </section>
      <InvitationManagement teamId={team.id} mutation={mutation} fresh={fresh} setFresh={setFresh} />
      <button className="button danger" type="button" disabled={mutation.pending} onClick={() => setConfirmation({ kind: 'disband' })}>{m.disband}</button>
    </>}
    {member && !owner && !denied && <button className="button secondary" type="button" disabled={mutation.pending} onClick={() => setConfirmation({ kind: 'leave' })}>{m.leave}</button>}
    {member && owner && <p className="muted">{m.ownerLeave}</p>}
  </>;
}

export function TeamDetailPage() {
  const { id } = useParams(); const number = Number(id); const valid = Number.isSafeInteger(number) && number > 0 && number <= 2147483647;
  const team = useLoad(`team-${id}`, signal => valid ? request(`/api/teams/${number}`, TeamSchema, { signal }) : Promise.reject(new ApiError(400)));
  if (!valid) return <><h1>{m.invalidRoute}</h1><Link to="/teams">{m.back}</Link></>;
  if (team.loading && !team.data) return <Loading />;
  if (team.error) return <ErrorNotice error={team.error} retry={() => void team.reload()} />;
  if (!team.data) return <Loading />;
  return <TeamContent key={number} team={team.data} reload={team.reload} />;
}
