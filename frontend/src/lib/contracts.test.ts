import { describe, expect, it } from 'vitest';
import { SessionSchema, TaskSchema, UserSchema } from './contracts';

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
