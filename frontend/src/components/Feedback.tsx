import { ApiError } from '../lib/api';
import type { ReactNode } from 'react';

export function ErrorNotice({ error, retry }: { error: unknown; retry?: () => void }) {
  return <div className="alert alert-error" role="alert"><p>{error instanceof ApiError ? error.message : 'Не вдалося завантажити дані. Повторіть спробу.'}</p>
    {error instanceof ApiError && error.traceId && <small>Код звернення: {error.traceId}</small>}
    {retry && <button className="button secondary" onClick={retry}>Повторити</button>}</div>;
}
export function Loading({ text = 'Завантаження…' }: { text?: string }) { return <p className="loading" role="status">{text}</p>; }
export function Empty({ title, children }: { title: string; children?: ReactNode }) {
  return <div className="empty"><span aria-hidden="true" className="empty-mark">＋</span><h2>{title}</h2>{children}</div>;
}
export function Pagination({ page, pageSize, total, change }: { page: number; pageSize: number; total: number; change: (value: number) => void }) {
  const last = Math.max(1, Math.ceil(total / pageSize));
  return <nav className="pagination" aria-label="Сторінки результатів"><span>{total} результатів · Сторінка {page} з {last}</span>
    <div><button className="button secondary" disabled={page <= 1} onClick={() => change(page - 1)}>Назад</button><button className="button secondary" disabled={page >= last} onClick={() => change(page + 1)}>Далі</button></div></nav>;
}
