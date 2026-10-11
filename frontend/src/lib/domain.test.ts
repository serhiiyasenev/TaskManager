import { describe, expect, it } from 'vitest';
import { TaskInputSchema } from './domain';
const task = { projectId: 1, performerId: 2, name: 'Перевірити API', description: '', state: 'ToDo', dueDate: null, reminderEnabled: false, reminderOffsetMinutes: null, escalationEnabled: false, escalationDelayMinutes: null };
describe('task form validation', () => {
  it('requires a deadline when a reminder is enabled', () => expect(TaskInputSchema.safeParse({ ...task, reminderEnabled: true }).success).toBe(false));
  it('rejects empty titles and unselected assignees', () => {
    expect(TaskInputSchema.safeParse({ ...task, name: '  ' }).success).toBe(false);
    expect(TaskInputSchema.safeParse({ ...task, performerId: 0 }).success).toBe(false);
  });
  it('accepts a UTC deadline and trims the confirmed title', () => {
    expect(TaskInputSchema.parse({ ...task, name: '  Перевірити API  ', reminderEnabled: true, dueDate: '2026-10-12T14:00:00Z', reminderOffsetMinutes: 60 }).name).toBe('Перевірити API');
  });
});
