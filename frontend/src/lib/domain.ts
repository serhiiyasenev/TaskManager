import { z } from 'zod';
import { states } from './contracts';
export const TaskInputSchema = z.object({
  projectId: z.number().int().positive('Оберіть проєкт.'),
  performerId: z.number().int().positive('Оберіть виконавця.'),
  name: z.string().trim().min(1, 'Введіть назву.').max(100, 'Не більше 100 символів.'),
  description: z.string().max(1000, 'Не більше 1000 символів.'),
  state: z.enum(states), dueDate: z.iso.datetime({ offset: true }).nullable(),
  reminderEnabled: z.boolean(), reminderOffsetMinutes: z.number().int().min(1).max(43200).nullable(),
  escalationEnabled: z.boolean(), escalationDelayMinutes: z.number().int().min(1).max(43200).nullable(),
  expectedVersion: z.string().uuid().optional()
}).superRefine((task, context) => {
  if ((task.reminderEnabled || task.escalationEnabled) && !task.dueDate)
    context.addIssue({ code: 'custom', path: ['dueDate'], message: 'Для нагадування потрібен строк виконання.' });
});
export const stateLabels: Record<(typeof states)[number], string> = { ToDo: 'До виконання', InProgress: 'У роботі', Done: 'Готово', Canceled: 'Скасовано' };
export const englishStateLabels: typeof stateLabels = { ToDo: 'To do', InProgress: 'In progress', Done: 'Done', Canceled: 'Canceled' };
