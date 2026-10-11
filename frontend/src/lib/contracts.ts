import { z } from 'zod';

export const SessionSchema = z.object({
  id: z.number().int().positive(), userName: z.string().nullable(), email: z.string().nullable(),
  firstName: z.string(), lastName: z.string(), teamId: z.number().int().nullable(),
  emailConfirmed: z.boolean(), roles: z.array(z.string())
});
export type SessionProfile = z.infer<typeof SessionSchema>;
export const UserSchema = z.object({
  id: z.number().int().positive(), userName: z.string(), email: z.string(),
  teamId: z.number().int().nullable(), firstName: z.string(), lastName: z.string(),
  birthDay: z.string().optional(), registeredAt: z.string().optional()
});
export type TeamMember = z.infer<typeof UserSchema>;
export const TeamSchema = z.object({ id: z.number().int(), name: z.string(), createdAt: z.string(), ownerId: z.number().int().nullable().optional(), users: z.array(UserSchema) });
export type Team = z.infer<typeof TeamSchema>;
export const states = ['ToDo', 'InProgress', 'Done', 'Canceled'] as const;
export const TaskSchema = z.object({
  id: z.number().int(), projectId: z.number().int(), performerId: z.number().int(),
  name: z.string(), description: z.string(), state: z.enum(states), createdAt: z.string(),
  finishedAt: z.string().nullable(), dueDate: z.string().nullable(),
  reminderEnabled: z.boolean(), reminderOffsetMinutes: z.number().int().nullable(),
  escalationEnabled: z.boolean(), escalationDelayMinutes: z.number().int().nullable(),
  reminderSentAt: z.string().nullable(), escalationSentAt: z.string().nullable(),
  performer: UserSchema.optional(), version: z.string().uuid().optional()
});
export type TaskRecord = z.infer<typeof TaskSchema>;
export const ProjectSchema = z.object({
  id: z.number().int(), authorId: z.number().int(), teamId: z.number().int(),
  name: z.string(), description: z.string(), deadline: z.string(), createdAt: z.string(),
  team: TeamSchema.optional(), tasks: z.array(TaskSchema).optional()
});
export type Project = z.infer<typeof ProjectSchema>;
export const TaskPageSchema = z.object({ items: z.array(TaskSchema), page: z.number().int(), pageSize: z.number().int(), totalCount: z.number().int() });
