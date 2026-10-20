import type { Page, Route } from '@playwright/test';
import { longTaskName } from './syntheticApi';

// Synthetic browser fixtures only. This boundary proves rendered React flows
// and transport handling, not live Azure inference, Identity or SQL behavior.
export const syntheticAiNotes = `Олена до 2030-10-12 перевірить API оплати. За дизайн ще не домовилися.`;
export const syntheticAiQuote = 'Олена до 2030-10-12 перевірить API оплати.';
const suggestionQuote = 'За дизайн ще не домовилися.';
export const syntheticDraftId = '7a4f8cd9-cd7f-4b36-b9e0-b473f5dff64b';
export const syntheticItemOne = 'cb17b7a3-e6b3-4203-9830-8f8a84644348';
export const syntheticItemTwo = 'b9dadff4-ef3e-4181-9b17-80f3f933088a';
export const syntheticDraftVersion = '76d8c079-d82c-46c8-a184-58327b8ae6a0';
export const syntheticReceiptVersion = 'c5d112b2-22b3-4a6f-8c96-0a98bf832909';

export interface AiCapturedRequest { path: string; method: string; body?: Record<string, unknown>; key?: string; csrf?: string }
export interface SyntheticAiOptions {
  holdExtraction?: boolean; empty?: boolean; unavailable?: boolean;
  extractionFailure?: number; commitFailure?: number; malformedDraft?: boolean; unknownCommitOnce?: boolean;
}

export async function syntheticAiApi(page: Page, options: SyntheticAiOptions = {}) {
  const settings = { ...options };
  const captured: AiCapturedRequest[] = [];
  let releaseExtraction: () => void = () => {};
  const extractionGate = new Promise<void>(resolve => { releaseExtraction = resolve; });
  const draft = { id: syntheticDraftId, ownerId: 42, projectId: 71, version: syntheticDraftVersion,
    createdAtUtc: new Date().toISOString(), expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
    locale: 'uk', timeZoneId: 'Europe/Kyiv', referenceDate: '2026-10-12', items: [
      { draftItemId: syntheticItemOne, title: longTaskName, description: 'Контрольована пропозиція для перевірки довгого тексту.',
        proposedAssigneeId: 42, originalAssigneeText: 'Олена', proposedDueAtUtc: '2030-10-12T20:59:00Z', originalDueText: 'до 2030-10-12',
        sourceQuote: syntheticAiQuote, sourceStart: 0, sourceLength: syntheticAiQuote.length, isSuggestion: false,
        interpretations: ['title_interpretation', 'date_only_end_of_day'], clarificationReasons: [], materializedTaskId: null as number | null },
      { draftItemId: syntheticItemTwo, title: 'Узгодити наступний крок щодо дизайну', description: '',
        proposedAssigneeId: null, originalAssigneeText: null, proposedDueAtUtc: null, originalDueText: null,
        sourceQuote: suggestionQuote, sourceStart: syntheticAiNotes.indexOf(suggestionQuote), sourceLength: suggestionQuote.length, isSuggestion: true,
        interpretations: [], clarificationReasons: ['commitment_unresolved', 'assignee_missing', 'due_date_missing'], materializedTaskId: null as number | null },
    ] };
  const receipts = new Map<string, { body: string; value: unknown }>();
  let failedCommit = false;
  let nextTaskId = 501;
  const json = (route: Route, body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body), headers: { 'Cache-Control': 'no-store' } });
  const problem = (route: Route, status: number) => route.fulfill({ status, contentType: 'application/problem+json', headers: { 'X-Correlation-ID': 'synthetic-ai-trace' },
    body: JSON.stringify({ status, title: 'Controlled synthetic outcome', traceId: 'synthetic-ai-trace' }) });

  await page.route('**/api/ai/**', async route => {
    const incoming = route.request(); const path = new URL(incoming.url()).pathname; const method = incoming.method();
    const body = incoming.postData() ? incoming.postDataJSON() as Record<string, unknown> : undefined;
    const headers = await incoming.allHeaders(); const key = headers['idempotency-key'];
    captured.push({ path, method, body, key, csrf: headers['x-csrf-token'] });
    if (path === '/api/ai/status') return json(route, { available: !settings.unavailable, provider: settings.unavailable ? 'disabled' : 'fake', maxInputCharacters: 20_000, maxItems: 25 });
    if (path === '/api/ai/task-extractions' && method === 'POST') {
      if (settings.holdExtraction) await extractionGate;
      if (settings.extractionFailure) return problem(route, settings.extractionFailure);
      if (settings.malformedDraft) return json(route, { ...draft, items: [{ ...draft.items[0], sourceLength: syntheticAiQuote.length + 1 }] }, 201);
      return json(route, { ...draft, items: settings.empty ? [] : draft.items }, 201);
    }
    if (path === `/api/ai/task-extractions/${syntheticDraftId}/commit` && method === 'POST') {
      if (settings.commitFailure) return problem(route, settings.commitFailure);
      if (!key) return problem(route, 400);
      const serialized = JSON.stringify(body);
      const previous = receipts.get(key);
      if (previous) return previous.body === serialized ? json(route, previous.value) : problem(route, 409);
      if (body?.expectedVersion !== draft.version) return problem(route, 409);
      const selected = (body.items ?? []) as Array<{ draftItemId: string }>;
      const materialized = selected.map(item => ({ draftItemId: item.draftItemId, taskId: nextTaskId++,
        taskVersion: 'fb12a4c4-d45d-4085-8360-0eebd935bafc', outboxMessageId: 'c7f9c2cb-d941-40cf-ab90-3fb4079c31b5' }));
      draft.version = syntheticReceiptVersion;
      for (const item of draft.items) item.materializedTaskId = materialized.find(result => result.draftItemId === item.draftItemId)?.taskId ?? item.materializedTaskId;
      const receipt = { draftId: draft.id, draftVersion: draft.version, items: materialized };
      receipts.set(key, { body: serialized, value: receipt });
      // Simulate a committed response being lost. Retrying must recover the exact
      // receipt above with the same key/body instead of generating another task.
      if (settings.unknownCommitOnce && !failedCommit) { failedCommit = true; return route.abort('failed'); }
      return json(route, receipt);
    }
    if (path === `/api/ai/task-extractions/${syntheticDraftId}`) return method === 'DELETE' ? route.fulfill({ status: 204 }) : json(route, draft);
    return problem(route, 501);
  });
  return { captured, settings, releaseExtraction: () => { settings.holdExtraction = false; releaseExtraction(); } };
}
