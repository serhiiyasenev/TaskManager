import { z } from 'zod';
import { states } from './contracts';
import { commonCopy } from './commonCopy';
import { getLocale } from './i18n';
import type { Locale } from './i18n';
export function getTaskInputSchema(locale: Locale = getLocale()) {
  const m = commonCopy[locale];
  return z.object({
    projectId: z.number().int().positive(m.chooseProject),
    performerId: z.number().int().positive(m.chooseAssignee),
    name: z.string().trim().min(1, m.titleRequired).max(100, m.titleLimit),
    description: z.string().max(1000, m.descriptionLimit),
    state: z.enum(states, { error: m.invalidState }), dueDate: z.iso.datetime({ offset: true }).nullable(),
    reminderEnabled: z.boolean(), reminderOffsetMinutes: z.number().int(m.minutesRange).min(1, m.minutesRange).max(10080, m.minutesRange).nullable(),
    escalationEnabled: z.boolean(), escalationDelayMinutes: z.number().int(m.minutesRange).min(1, m.minutesRange).max(10080, m.minutesRange).nullable(),
    expectedVersion: z.string().uuid().optional()
  }).superRefine((task, context) => {
    if ((task.reminderEnabled || task.escalationEnabled) && !task.dueDate)
      context.addIssue({ code: 'custom', path: ['dueDate'], message: m.deadlineRequired });
  });
}
export const TaskInputSchema = getTaskInputSchema('uk');
export const stateLabels: Record<(typeof states)[number], string> = { ToDo: 'До виконання', InProgress: 'У роботі', Done: 'Готово', Canceled: 'Скасовано' };
export const englishStateLabels: typeof stateLabels = { ToDo: 'To do', InProgress: 'In progress', Done: 'Done', Canceled: 'Canceled' };
export function getStateLabels(locale: Locale = getLocale()) { return locale === 'en' ? englishStateLabels : stateLabels; }
