import { describe, expect, it } from 'vitest';
import { AIDraftSchema, AICommitReceiptSchema, AIStatusSchema, SessionSchema, TaskSchema, UserSchema } from './contracts';

// API JsonOptions ignore null values. These are legitimate serialized shapes,
// including a newly confirmed member with no team and an undated open task.
describe('nullable ASP.NET response contracts', () => {
  it('accepts a confirmed session before team onboarding', () => {
    const result = SessionSchema.safeParse({ id: 1, userName: 'olena', email: 'olena@example.test', firstName: 'Олена', lastName: 'Коваль', emailConfirmed: true, roles: [] });
    expect(result.success).toBe(true);
    if (result.success) expect(result.data.teamId).toBeNull();
  });
  it('accepts a scoped user whose team membership is absent', () => {
    const result = UserSchema.safeParse({ id: 1, userName: 'olena', email: 'olena@example.test', firstName: 'Олена', lastName: 'Коваль' });
    expect(result.success).toBe(true);
    if (result.success) expect(result.data.teamId).toBeNull();
  });
  it('accepts an open task with no deadline or sent reminders', () => {
    const result = TaskSchema.safeParse({ id: 1, projectId: 2, performerId: 1, name: 'Перевірити API', description: '', state: 'ToDo', createdAt: '2026-10-12T09:00:00', reminderEnabled: false, escalationEnabled: false, version: 'da12341e-1a80-4ef6-a311-3f4de4fce8bb' });
    expect(result.success).toBe(true);
    if (result.success) { expect(result.data.dueDate).toBeNull(); expect(result.data.finishedAt).toBeNull(); expect(result.data.reminderSentAt).toBeNull(); }
  });
});

describe('AI response trust boundary', () => {
  const id = 'da12341e-1a80-4ef6-a311-3f4de4fce8bb';
  const item = { draftItemId: id, title: 'Перевірити API', description: '', sourceQuote: 'Олена перевірить API.', sourceStart: 0,
    sourceLength: 'Олена перевірить API.'.length, isSuggestion: false, interpretations: [], clarificationReasons: ['due_date_missing'] };
  const draft = { id, ownerId: 1, projectId: 2, version: id, createdAtUtc: '2026-10-12T09:00:00Z', expiresAtUtc: '2026-10-13T09:00:00Z', locale: 'uk', timeZoneId: 'Europe/Kyiv', referenceDate: '2026-10-12', items: [item] };
  it('normalizes omitted unresolved proposal fields without inventing people or dates', () => {
    const parsed = AIDraftSchema.parse(draft);
    expect(parsed.items[0]).toMatchObject({ proposedAssigneeId: null, proposedDueAtUtc: null, originalAssigneeText: null, originalDueText: null, materializedTaskId: null });
    expect(AIStatusSchema.parse({ available: false, provider: 'disabled', maxInputCharacters: 20000, maxItems: 25 }).reasonCode).toBeNull();
  });
  it('rejects malformed source spans, identifiers and instants', () => {
    expect(AIDraftSchema.safeParse({ ...draft, items: [{ ...item, sourceLength: 999 }] }).success).toBe(false);
    expect(AIDraftSchema.safeParse({ ...draft, items: [{ ...item, proposedAssigneeId: -1 }] }).success).toBe(false);
    expect(AIDraftSchema.safeParse({ ...draft, createdAtUtc: 'not-a-date' }).success).toBe(false);
  });
  it('validates only real receipt identifiers for task links', () => {
    const receipt = { draftId: id, draftVersion: id, items: [{ draftItemId: id, taskId: 3, taskVersion: id, outboxMessageId: id }] };
    expect(AICommitReceiptSchema.parse(receipt).items[0].taskId).toBe(3);
    expect(AICommitReceiptSchema.safeParse({ ...receipt, items: [{ ...receipt.items[0], taskId: 0 }] }).success).toBe(false);
  });
});
