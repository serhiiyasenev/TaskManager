import { describe, expect, it, vi } from 'vitest';
import { z } from 'zod';
import { ApiError, request } from './api';

describe('same-origin API boundary', () => {
  it('gets a CSRF token and sends it with a mutation without browser bearer credentials', async () => {
    const fetcher = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ requestToken: 'synthetic-csrf' })))
      .mockResolvedValueOnce(new Response(JSON.stringify({ id: 42 })));
    vi.stubGlobal('fetch', fetcher);
    await expect(request('/api/Tasks', z.object({ id: z.number() }), { method: 'POST', body: { name: 'Перевірити API' } })).resolves.toEqual({ id: 42 });
    expect(fetcher.mock.calls[0][0]).toBe('/api/auth/csrf');
    const options = fetcher.mock.calls[1][1] as RequestInit;
    expect(options.credentials).toBe('same-origin');
    expect(new Headers(options.headers).get('X-CSRF-TOKEN')).toBe('synthetic-csrf');
    expect(new Headers(options.headers).has('Authorization')).toBe(false);
  });
  it('rejects a malformed server response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: 'wrong-type' }))));
    await expect(request('/api/Tasks/42', z.object({ id: z.number() }))).rejects.toMatchObject({ status: 502 });
  });
  it('preserves the conflict status, field errors and correlation without exposing raw HTML', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ title: 'Conflict', errors: { name: ['Required'] } }), { status: 409, headers: { 'X-Correlation-ID': 'test-trace' } })));
    const error = await request('/api/Tasks/42').catch((value: unknown) => value);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 409, traceId: 'test-trace', fieldErrors: { name: ['Required'] } });
  });
  it('rejects external or protocol-relative URLs before fetching', async () => {
    const fetcher = vi.fn(); vi.stubGlobal('fetch', fetcher);
    await expect(request('//foreign.test/api/Tasks')).rejects.toThrow();
    expect(fetcher).not.toHaveBeenCalled();
  });
});
