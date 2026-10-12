import { readFileSync } from 'node:fs';
import { isAbsolute } from 'node:path';
import { z } from 'zod';

const StateSchema = z.strictObject({
  schemaVersion: z.literal(1), mode: z.literal('controlled-full-stack'),
  baseUrl: z.string(), controlUrl: z.string(), controlToken: z.string().min(32).max(256),
  databaseName: z.string().regex(/^taskmanager_e2e_test_[a-f0-9-]{32,36}$/i),
  provider: z.literal('fake'), transport: z.enum(['controlled', 'rabbitmq']),
});
export type FullStackState = z.infer<typeof StateSchema>;
let state: FullStackState | undefined;

function loopbackRoot(value: string, protocol: 'https:' | 'http:') {
  const url = new URL(value);
  if (url.protocol !== protocol || !['localhost', '127.0.0.1', '[::1]'].includes(url.hostname) ||
      !url.port || url.username || url.password || url.pathname !== '/' || url.search || url.hash)
    throw new Error('Full-stack host must use explicit loopback listener roots.');
  return url.origin;
}

// Read only the private state supplied by the test host. Never log it or attach
// it to a report: its control token authorizes fixture changes.
export function fullStackState(): FullStackState {
  if (state) return state;
  try {
    const path = process.env.TASKMANAGER_FULLSTACK_STATE;
    if (!path || !isAbsolute(path)) throw new Error();
    const raw = readFileSync(path, 'utf8');
    if (Buffer.byteLength(raw, 'utf8') > 8_192) throw new Error();
    const parsed = StateSchema.parse(JSON.parse(raw));
    state = Object.freeze({ ...parsed,
      baseUrl: loopbackRoot(parsed.baseUrl, 'https:'), controlUrl: loopbackRoot(parsed.controlUrl, 'http:') });
    return state;
  } catch {
    throw new Error('A valid private TASKMANAGER_FULLSTACK_STATE from the real test host is required.');
  }
}

export function fullStackRunId(): string {
  const value = process.env.TASKMANAGER_FULLSTACK_RUN_ID ?? '';
  if (!/^[a-f0-9]{32}$/i.test(value)) throw new Error('A fresh full-stack run ID is required.');
  return value.toLowerCase();
}
