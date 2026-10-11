import type { z } from 'zod';

export class ApiError extends Error {
  constructor(public status: number, public traceId?: string, public fieldErrors: Record<string, string[]> = {}) {
    super('Не вдалося виконати запит.');
  }
}
export interface RequestOptions { method?: string; body?: unknown; signal?: AbortSignal; headers?: HeadersInit }
export async function request<T = unknown>(_path: string, _schema?: z.ZodType<T>, _options: RequestOptions = {}): Promise<T> {
  throw new Error('API transport is not implemented.');
}
