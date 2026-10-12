import type { z } from 'zod';

export class ApiError extends Error {
  constructor(public status: number, public traceId?: string, public fieldErrors: Record<string, string[]> = {}) {
    super(messages[status] ?? 'Не вдалося виконати запит. Спробуйте ще раз.');
    this.name = 'ApiError';
  }
}
const messages: Record<number, string> = {
  0: 'Немає з’єднання із сервером. Перевірте мережу та повторіть запит.',
  400: 'Перевірте введені дані.', 401: 'Увійдіть у свій обліковий запис.',
  403: 'Ви не маєте дозволу на цю дію.', 404: 'Об’єкт недоступний або вже видалений.',
  409: 'Дані або права змінилися. Оновіть запис і перевірте зміни.',
  410: 'Строк дії минув або посилання відкликано.',
  428: 'Завантажте актуальну версію перед збереженням.',
  429: 'Забагато запитів. Зачекайте хвилину та повторіть спробу.',
  502: 'Сервер повернув неочікувану відповідь. Повторіть запит.',
  503: 'Сервіс тимчасово недоступний. Спробуйте пізніше.'
};
export const sessionExpiredEvent = 'taskmanager:session-expired';
export interface RequestOptions { method?: string; body?: unknown; signal?: AbortSignal; headers?: HeadersInit }
export async function request<T = unknown>(path: string, schema?: z.ZodType<T>, options: RequestOptions = {}): Promise<T> {
  if (!/^\/api(?:\/|$)/.test(path) || path.includes('\\') || /(?:^|\/)\.\.(?:\/|$)/.test(path)) throw new ApiError(400);
  const method = (options.method ?? 'GET').toUpperCase();
  const headers = new Headers(options.headers);
  if (headers.has('Authorization') || !['GET', 'HEAD', 'POST', 'PUT', 'PATCH', 'DELETE'].includes(method)) throw new ApiError(400);
  headers.set('Accept', 'application/json');
  if (options.body !== undefined) headers.set('Content-Type', 'application/json');
  if (!['GET', 'HEAD'].includes(method)) {
    const csrf = await send('/api/auth/csrf', { method: 'GET', signal: options.signal });
    if (!csrf || typeof csrf !== 'object' || !('requestToken' in csrf) || typeof csrf.requestToken !== 'string' || !csrf.requestToken) throw new ApiError(502);
    headers.set('X-CSRF-TOKEN', csrf.requestToken);
  }
  const value = await send(path, { method, headers, signal: options.signal, body: options.body === undefined ? undefined : JSON.stringify(options.body) });
  if (!schema) return value as T;
  const parsed = schema.safeParse(value);
  if (!parsed.success) throw new ApiError(502);
  return parsed.data;
}

async function send(path: string, options: RequestInit): Promise<unknown> {
  let response: Response;
  try { response = await fetch(path, { ...options, credentials: 'same-origin', cache: 'no-store' }); }
  catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error;
    throw new ApiError(0);
  }
  let body: unknown;
  const text = await response.text();
  try { body = text ? JSON.parse(text) : undefined; }
  catch { if (response.ok) throw new ApiError(502); }
  if (!response.ok) {
    const details = body && typeof body === 'object' ? body as Record<string, unknown> : {};
    const fieldErrors: Record<string, string[]> = {};
    if (details.errors && typeof details.errors === 'object') {
      for (const [field, errors] of Object.entries(details.errors)) if (Array.isArray(errors) && errors.every(item => typeof item === 'string')) fieldErrors[field] = errors;
    }
    if (response.status === 401 && !/^\/api\/auth\/(me|login|register|csrf|confirm-email|resend-confirmation|forgot-password|reset-password)(?:\?|$)/.test(path)) window.dispatchEvent(new Event(sessionExpiredEvent));
    throw new ApiError(response.status, response.headers.get('X-Correlation-ID') ?? (typeof details.traceId === 'string' ? details.traceId : undefined), fieldErrors);
  }
  return body;
}
