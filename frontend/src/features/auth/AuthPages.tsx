import { useEffect, useId, useRef, useState } from 'react';
import type { FormEvent, InputHTMLAttributes, ReactNode } from 'react';
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { z } from 'zod';
import { ApiError, request } from '../../lib/api';
import { useSession } from '../../lib/session';
import { copy as m } from './authCopy';

const MessageSchema = z.object({ message: z.string() });
const PreviewSchema = z.object({ teamName: z.string(), expired: z.boolean() });
const AcceptedSchema = z.object({ teamId: z.number().int().positive() });
const ProfileDetailsSchema = z.object({
  id: z.number().int().positive(), userName: z.string(), email: z.string(), teamId: z.number().int().nullish().transform(value => value ?? null),
  firstName: z.string(), lastName: z.string(), birthDay: z.string(),
});
const ProfileUpdatedSchema = z.object({ id: z.number().int().positive(), firstName: z.string(), lastName: z.string(), email: z.string() });
type Fields = Record<string, string>;
type FormErrors = Record<string, string>;

function errorText(error: unknown, context?: 'login' | 'invite'): string {
  if (!(error instanceof ApiError)) return m.error;
  if (error.status === 401) return context === 'login' ? m.invalidLogin : m.sessionExpired;
  if (error.status === 403) return context === 'invite' ? m.wrongInvitationEmail : m.forbidden;
  if (error.status === 409) return context === 'invite' ? m.invitationConflict : 'Дані або права змінилися. Оновіть сторінку й перевірте зміни.';
  if (error.status === 410 || error.status === 404) return context === 'invite' ? m.expiredInvite : m.invalidLink;
  if (error.status === 429) return m.rateLimit;
  if (error.status === 400) return m.validationError;
  if (error.status === 0 || error.status >= 500) return m.unavailable;
  return m.error;
}

function ErrorNotice({ error, context }: { error: unknown; context?: 'login' | 'invite' }) {
  const target = useRef<HTMLDivElement>(null);
  useEffect(() => { if (error) target.current?.focus(); }, [error]);
  if (!error) return null;
  return <div className="alert alert-error" role="alert" tabIndex={-1} ref={target}>
    <p>{errorText(error, context)}</p>
    {error instanceof ApiError && error.traceId && <small>{m.trace}: {error.traceId.slice(0, 128)}</small>}
  </div>;
}

function useAction() {
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [message, setMessage] = useState('');
  const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  async function run(action: () => Promise<void>, success = '') {
    if (pending) return;
    setPending(true); setError(null); setMessage('');
    try { await action(); if (alive.current) setMessage(success); }
    catch (failure) { if (alive.current) setError(failure); }
    finally { if (alive.current) setPending(false); }
  }
  return { pending, error, message, run, setError };
}

function useFields(initial: Fields) {
  const [values, setValues] = useState(initial);
  const [errors, setErrors] = useState<FormErrors>({});
  function update(field: string, value: string) {
    setValues(current => ({ ...current, [field]: value }));
    setErrors(current => ({ ...current, [field]: '' }));
  }
  function validate(required: string[], passwords?: [string, string], minimumPassword?: string) {
    const issues: FormErrors = {};
    for (const field of required) if (!values[field]?.trim()) issues[field] = m.required;
    if (values.email && !z.email().safeParse(values.email.trim()).success) issues.email = m.invalidEmail;
    if (minimumPassword && values[minimumPassword].length < 8) issues[minimumPassword] = m.passwordLength;
    if (passwords && values[passwords[0]] !== values[passwords[1]]) issues[passwords[1]] = m.passwordMismatch;
    for (const field of ['firstName', 'lastName', 'userName']) if (values[field]?.trim().length > 100) issues[field] = 'Використайте не більше 100 символів.';
    setErrors(issues);
    return Object.keys(issues).length === 0;
  }
  return { values, errors, update, validate, setValues };
}

function Field({ label, name, fields, actionError, ...input }: InputHTMLAttributes<HTMLInputElement> & {
  label: string; name: string; fields: ReturnType<typeof useFields>; actionError?: unknown;
}) {
  const id = useId();
  const serverError = actionError instanceof ApiError && Object.keys(actionError.fieldErrors).some(key => key.toLowerCase() === name.toLowerCase());
  const error = fields.errors[name] || (serverError ? m.validationError : '');
  return <div className="field">
    <label htmlFor={id}>{label}</label>
    <input {...input} id={id} name={name} value={fields.values[name] ?? ''} onChange={event => fields.update(name, event.target.value)}
      aria-invalid={Boolean(error)} aria-describedby={error ? `${id}-error` : undefined} />
    {error && <span id={`${id}-error`} className="field-error" role="alert">{error}</span>}
  </div>;
}

function Panel({ title, children }: { title: string; children: ReactNode }) {
  const heading = useId();
  return <section className="auth-panel" aria-labelledby={heading}>
    <header className="page-header"><p className="muted">TaskManager</p><h1 id={heading}>{title}</h1></header>{children}
  </section>;
}

function Submit({ pending, children }: { pending: boolean; children: ReactNode }) {
  return <button className="button" type="submit" disabled={pending}>{pending ? m.pending : children}</button>;
}

function safeReturnTo(value: string | null): string {
  if (!value?.startsWith('/') || value.startsWith('//') || value.includes('\\')) return '/';
  return value;
}

export function LoginPage() {
  const session = useSession(); const action = useAction(); const navigate = useNavigate(); const [query] = useSearchParams();
  const fields = useFields({ email: '', password: '' });
  function submit(event: FormEvent) {
    event.preventDefault(); if (!fields.validate(['email', 'password'])) return;
    void action.run(async () => { await session.login(fields.values.email.trim(), fields.values.password); navigate(safeReturnTo(query.get('returnTo')), { replace: true }); });
  }
  return <Panel title={m.loginTitle}>
    {query.get('expired') === '1' && <p className="alert alert-warning" role="status">{m.sessionExpired}</p>}
    <ErrorNotice error={action.error} context="login" />
    <form className="form-grid" noValidate onSubmit={submit} aria-busy={action.pending}>
      <Field fields={fields} actionError={action.error} name="email" label={m.email} type="email" autoComplete="email" required maxLength={256} />
      <Field fields={fields} actionError={action.error} name="password" label={m.password} type="password" autoComplete="current-password" required maxLength={256} />
      <Submit pending={action.pending}>{m.login}</Submit>
    </form>
    <p><Link to="/forgot-password">Забули пароль?</Link></p><p><Link to="/register">Створити обліковий запис</Link></p>
    <ResendConfirmation initialEmail={fields.values.email} />
  </Panel>;
}

function ResendConfirmation({ initialEmail }: { initialEmail: string }) {
  const action = useAction();
  return <div>
    <button className="button secondary" type="button" disabled={action.pending || !z.email().safeParse(initialEmail.trim()).success}
      onClick={() => { void action.run(async () => { await request('/api/auth/resend-confirmation', MessageSchema, { method: 'POST', body: { email: initialEmail.trim() } }); }, m.resent); }}>
      {action.pending ? m.pending : m.resend}
    </button>
    {action.message && <p role="status">{action.message}</p>}<ErrorNotice error={action.error} />
  </div>;
}

export function RegisterPage() {
  const action = useAction(); const [registeredEmail, setRegisteredEmail] = useState('');
  const fields = useFields({ userName: '', firstName: '', lastName: '', email: '', password: '', repeatPassword: '' });
  function submit(event: FormEvent) {
    event.preventDefault(); if (!fields.validate(Object.keys(fields.values), ['password', 'repeatPassword'], 'password')) return;
    void action.run(async () => {
      const body = { userName: fields.values.userName.trim(), firstName: fields.values.firstName.trim(), lastName: fields.values.lastName.trim(), email: fields.values.email.trim(), password: fields.values.password };
      await request('/api/auth/register', MessageSchema, { method: 'POST', body }); setRegisteredEmail(body.email);
      fields.update('password', ''); fields.update('repeatPassword', '');
    }, m.registered);
  }
  return <Panel title={m.registerTitle}>
    <ErrorNotice error={action.error} />
    {registeredEmail ? <><p role="status">{m.registered}</p><ResendConfirmation initialEmail={registeredEmail} /><p><Link to="/login">{m.login}</Link></p></>
      : <form className="form-grid" noValidate onSubmit={submit} aria-busy={action.pending}>
        <Field fields={fields} actionError={action.error} name="userName" label={m.userName} autoComplete="username" required maxLength={100} />
        <Field fields={fields} actionError={action.error} name="firstName" label={m.firstName} autoComplete="given-name" required maxLength={100} />
        <Field fields={fields} actionError={action.error} name="lastName" label={m.lastName} autoComplete="family-name" required maxLength={100} />
        <Field fields={fields} actionError={action.error} name="email" label={m.email} type="email" autoComplete="email" required maxLength={256} />
        <Field fields={fields} actionError={action.error} name="password" label={m.password} type="password" autoComplete="new-password" required maxLength={256} />
        <Field fields={fields} name="repeatPassword" label={m.repeatPassword} type="password" autoComplete="new-password" required maxLength={256} />
        <p className="muted">{m.passwordLength}</p><Submit pending={action.pending}>{m.register}</Submit>
      </form>}
    <p><Link to="/login">Вже маєте обліковий запис?</Link></p>
  </Panel>;
}

export function ConfirmEmailPage() {
  const [query] = useSearchParams(); const action = useAction();
  const userId = query.get('userId') ?? ''; const token = query.get('token') ?? '';
  const valid = /^[1-9]\d*$/.test(userId) && Number(userId) <= 2147483647 && token.length > 0 && token.length <= 4096;
  return <Panel title="Підтвердження email">
    {!valid ? <p role="alert">{m.invalidLink}</p> : <>
      <p>Підтвердьте адресу, до якої надійшов цей лист.</p>
      <ErrorNotice error={action.error} />
      {action.message ? <p role="status">{action.message}</p> : <button className="button" type="button" disabled={action.pending}
        onClick={() => { void action.run(async () => { await request('/api/auth/confirm-email', MessageSchema, { method: 'POST', body: { userId, token } }); }, m.confirmed); }}>{action.pending ? m.pending : m.confirm}</button>}
    </>}
    <p><Link to="/login">{m.login}</Link></p><p><Link to="/forgot-password">{m.forgotTitle}</Link></p>
  </Panel>;
}

export function ForgotPasswordPage() {
  const action = useAction(); const fields = useFields({ email: '' });
  function submit(event: FormEvent) {
    event.preventDefault(); if (!fields.validate(['email'])) return;
    void action.run(async () => { await request('/api/auth/forgot-password', MessageSchema, { method: 'POST', body: { email: fields.values.email.trim() } }); }, m.recoverySent);
  }
  return <Panel title={m.forgotTitle}><ErrorNotice error={action.error} />
    {action.message && <p role="status">{action.message}</p>}
    <form className="form-grid" noValidate onSubmit={submit} aria-busy={action.pending}>
      <Field fields={fields} name="email" label={m.email} type="email" autoComplete="email" required maxLength={256} />
      <Submit pending={action.pending}>{m.forgot}</Submit>
    </form><p><Link to="/login">{m.login}</Link></p>
  </Panel>;
}

export function ResetPasswordPage() {
  const [query] = useSearchParams(); const action = useAction();
  const email = query.get('email') ?? ''; const token = query.get('token') ?? '';
  const valid = z.email().safeParse(email).success && token.length > 0 && token.length <= 4096;
  const fields = useFields({ newPassword: '', repeatNewPassword: '' });
  function submit(event: FormEvent) {
    event.preventDefault(); if (!valid || !fields.validate(['newPassword', 'repeatNewPassword'], ['newPassword', 'repeatNewPassword'], 'newPassword')) return;
    void action.run(async () => { await request('/api/auth/reset-password', MessageSchema, { method: 'POST', body: { email, token, newPassword: fields.values.newPassword } }); fields.update('newPassword', ''); fields.update('repeatNewPassword', ''); }, m.passwordChanged);
  }
  return <Panel title="Відновлення пароля"><ErrorNotice error={action.error} />
    {!valid ? <p role="alert">{m.invalidLink}</p> : action.message ? <p role="status">{action.message}</p> : <form className="form-grid" noValidate onSubmit={submit} aria-busy={action.pending}>
      <Field fields={fields} actionError={action.error} name="newPassword" label={m.newPassword} type="password" autoComplete="new-password" required maxLength={256} />
      <Field fields={fields} name="repeatNewPassword" label={m.repeatNewPassword} type="password" autoComplete="new-password" required maxLength={256} />
      <Submit pending={action.pending}>{m.changePassword}</Submit>
    </form>}<p><Link to="/login">{m.login}</Link></p><p><Link to="/forgot-password">Запитати нове посилання</Link></p>
  </Panel>;
}

function ChangePasswordForm() {
  const session = useSession(); const action = useAction();
  const fields = useFields({ currentPassword: '', newPassword: '', repeatNewPassword: '' });
  function submit(event: FormEvent) {
    event.preventDefault(); if (!fields.validate(Object.keys(fields.values), ['newPassword', 'repeatNewPassword'], 'newPassword')) return;
    void action.run(async () => {
      await request('/api/auth/change-password', MessageSchema, { method: 'POST', body: { currentPassword: fields.values.currentPassword, newPassword: fields.values.newPassword } });
      fields.setValues({ currentPassword: '', newPassword: '', repeatNewPassword: '' }); await session.refresh();
    }, m.profilePasswordChanged);
  }
  return <section aria-labelledby="change-password-heading"><h2 id="change-password-heading">Зміна пароля</h2>
    <ErrorNotice error={action.error} />{action.message && <p role="status">{action.message}</p>}
    <form className="form-grid" noValidate onSubmit={submit} aria-busy={action.pending}>
      <Field fields={fields} actionError={action.error} name="currentPassword" label={m.currentPassword} type="password" autoComplete="current-password" required maxLength={256} />
      <Field fields={fields} actionError={action.error} name="newPassword" label={m.newPassword} type="password" autoComplete="new-password" required maxLength={256} />
      <Field fields={fields} name="repeatNewPassword" label={m.repeatNewPassword} type="password" autoComplete="new-password" required maxLength={256} />
      <Submit pending={action.pending}>{m.changePassword}</Submit>
    </form>
  </section>;
}

export function ProfilePage() {
  const session = useSession(); const action = useAction(); const [reload, setReload] = useState(0);
  const [details, setDetails] = useState<z.infer<typeof ProfileDetailsSchema> | null>(null);
  const [loadError, setLoadError] = useState<unknown>(null);
  const fields = useFields({ firstName: '', lastName: '', birthDay: '' }); const setValues = fields.setValues;
  const userId = session.user?.id;
  useEffect(() => {
    if (!userId) return;
    const controller = new AbortController(); setDetails(null); setLoadError(null);
    void request(`/api/users/${userId}`, ProfileDetailsSchema, { signal: controller.signal }).then(value => {
      if (!controller.signal.aborted) {
        setDetails(value); setValues({ firstName: value.firstName, lastName: value.lastName, birthDay: value.birthDay.startsWith('0001-') ? '' : value.birthDay.slice(0, 10) });
      }
    }).catch(error => { if (!controller.signal.aborted) setLoadError(error); });
    return () => controller.abort();
  }, [userId, reload, setValues]);
  function submit(event: FormEvent) {
    event.preventDefault(); if (!details || !fields.validate(['firstName', 'lastName'])) return;
    void action.run(async () => {
      await request(`/api/users/${details.id}`, ProfileUpdatedSchema, { method: 'PUT', body: {
        firstName: fields.values.firstName.trim(), lastName: fields.values.lastName.trim(),
        email: details.email, userName: details.userName, teamId: details.teamId,
        birthDay: fields.values.birthDay ? `${fields.values.birthDay}T00:00:00` : details.birthDay,
      } }); await session.refresh();
    }, m.profileSaved);
  }
  if (!session.user) return <Panel title={m.profile}><p>{m.sessionExpired}</p><Link to="/login">{m.login}</Link></Panel>;
  return <Panel title={m.profile}>
    <p className="muted">{session.user.email} · {session.user.emailConfirmed ? 'Email підтверджено' : 'Email потребує підтвердження'}</p>
    {loadError ? <><ErrorNotice error={loadError} /><button className="button secondary" type="button" onClick={() => setReload(value => value + 1)}>{m.retry}</button></>
      : !details ? <p role="status">{m.loading}</p> : <>
        <ErrorNotice error={action.error} />{action.message && <p role="status">{action.message}</p>}
        <form className="form-grid" noValidate onSubmit={submit} aria-busy={action.pending}>
          <Field fields={fields} actionError={action.error} name="firstName" label={m.firstName} autoComplete="given-name" required maxLength={100} />
          <Field fields={fields} actionError={action.error} name="lastName" label={m.lastName} autoComplete="family-name" required maxLength={100} />
          <Field fields={fields} name="birthDay" label="Дата народження (необов’язково)" type="date" autoComplete="bday" />
          <Submit pending={action.pending}>{m.saveProfile}</Submit>
        </form>
      </>}
    <ChangePasswordForm />
    <button className="button secondary" type="button" disabled={action.pending} onClick={() => { void action.run(async () => { await session.logout(); }); }}>Вийти з облікового запису</button>
  </Panel>;
}

export function InvitationPage() {
  const [query] = useSearchParams(); const location = useLocation(); const session = useSession();
  const token = query.get('token') ?? ''; const valid = /^[A-Za-z0-9_-]{32,128}$/.test(token);
  const [preview, setPreview] = useState<z.infer<typeof PreviewSchema> | null>(null);
  const [loadError, setLoadError] = useState<unknown>(null); const [loading, setLoading] = useState(false); const [reload, setReload] = useState(0);
  const [joinedTeam, setJoinedTeam] = useState<number | null>(null); const [copyNotice, setCopyNotice] = useState('');
  const action = useAction();
  useEffect(() => {
    if (!valid) return;
    const controller = new AbortController(); setPreview(null); setLoadError(null); setLoading(true);
    void request(`/api/team-invitations/preview?token=${encodeURIComponent(token)}`, PreviewSchema, { signal: controller.signal })
      .then(value => { if (!controller.signal.aborted) setPreview(value); })
      .catch(error => { if (!controller.signal.aborted) setLoadError(error); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [token, valid, reload]);
  const returnTo = `/login?returnTo=${encodeURIComponent(location.pathname + location.search)}`;
  return <Panel title={m.invitation}>
    {!valid ? <p role="alert">{m.invalidLink}</p> : loading ? <p role="status">{m.loading}</p> : loadError ? <>
      <ErrorNotice error={loadError} context="invite" /><button className="button secondary" type="button" onClick={() => setReload(value => value + 1)}>{m.retry}</button>
    </> : preview && <>
      <h2>{preview.teamName}</h2>
      <ErrorNotice error={action.error} context="invite" />
      {preview.expired ? <p role="alert">{m.expiredInvite}</p> : joinedTeam ? <><p role="status">{m.joined}</p><Link to={`/teams/${joinedTeam}`}>Перейти до команди</Link></>
        : !session.user ? <><p>Перегляньте запрошення після входу з підтвердженою email-адресою.</p><Link to={returnTo}>{m.invitationLogin}</Link></>
          : <>
            <p>Ви увійшли як {session.user.email}. Приєднання змінить вашу поточну команду лише після підтвердження.</p>
            <button className="button" type="button" disabled={action.pending} onClick={() => { void action.run(async () => {
              const result = await request('/api/team-invitations/accept', AcceptedSchema, { method: 'POST', body: { token } });
              setJoinedTeam(result.teamId); await session.refresh();
            }); }}>{action.pending ? m.pending : m.join}</button>
            {action.error instanceof ApiError && action.error.status === 403 && <p><button className="button secondary" type="button" disabled={action.pending} onClick={() => { void action.run(async () => { await session.logout(); }); }}>Увійти з іншою адресою</button></p>}
          </>}
      <p><button className="button secondary" type="button" disabled={action.pending} onClick={() => { void action.run(async () => {
        await navigator.clipboard.writeText(`${window.location.origin}/invite?token=${encodeURIComponent(token)}`); setCopyNotice(m.copied);
      }); }}>{m.copy}</button></p>
      {copyNotice && <p role="status">{copyNotice}</p>}
    </>}
  </Panel>;
}
