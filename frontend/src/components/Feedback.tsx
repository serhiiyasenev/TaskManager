import { ApiError } from '../lib/api';
import type { ReactNode } from 'react';
import { commonCopy, localizedApiMessage } from '../lib/commonCopy';
import { useLocale } from '../lib/i18n';

export function ErrorNotice({ error, retry }: { error: unknown; retry?: () => void }) {
  const { locale } = useLocale(); const m = commonCopy[locale];
  return <div className="alert alert-error" role="alert"><p>{error instanceof ApiError ? localizedApiMessage(error.status, locale) : m.loadFailed}</p>
    {error instanceof ApiError && error.traceId && <small>{m.trace}: {error.traceId}</small>}
    {retry && <button className="button secondary" onClick={retry}>{m.retry}</button>}</div>;
}
export function Loading({ text }: { text?: string }) { const { locale } = useLocale(); return <p className="loading" role="status">{text ?? commonCopy[locale].loading}</p>; }
export function Empty({ title, children }: { title: string; children?: ReactNode }) {
  return <div className="empty"><span aria-hidden="true" className="empty-mark">＋</span><h2>{title}</h2>{children}</div>;
}
export function Pagination({ page, pageSize, total, change }: { page: number; pageSize: number; total: number; change: (value: number) => void }) {
  const { locale } = useLocale(); const m = commonCopy[locale];
  const last = Math.max(1, Math.ceil(total / pageSize));
  return <nav className="pagination" aria-label={m.pagination}><span>{m.results.replace('{total}', String(total)).replace('{page}', String(page)).replace('{last}', String(last))}</span>
    <div><button className="button secondary" disabled={page <= 1} onClick={() => change(page - 1)}>{m.back}</button><button className="button secondary" disabled={page >= last} onClick={() => change(page + 1)}>{m.next}</button></div></nav>;
}
