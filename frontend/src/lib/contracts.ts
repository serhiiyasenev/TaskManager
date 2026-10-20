import { z } from 'zod';

export const SessionSchema = z.object({
  id: z.number().int().positive(), userName: z.string().nullish().transform(value => value ?? null), email: z.string().nullish().transform(value => value ?? null),
  firstName: z.string(), lastName: z.string(), teamId: z.number().int().nullish().transform(value => value ?? null),
  emailConfirmed: z.boolean(), roles: z.array(z.string())
});
export type SessionProfile = z.infer<typeof SessionSchema>;
export const UserSchema = z.object({
  id: z.number().int().positive(), userName: z.string(), email: z.string(),
  teamId: z.number().int().nullish().transform(value => value ?? null), firstName: z.string(), lastName: z.string(),
  birthDay: z.string().optional(), registeredAt: z.string().optional()
});
export type TeamMember = z.infer<typeof UserSchema>;
export const TeamSchema = z.object({ id: z.number().int(), name: z.string(), createdAt: z.string(), ownerId: z.number().int().nullable().optional(), users: z.array(UserSchema) });
export type Team = z.infer<typeof TeamSchema>;
// Shared wire contract used by the owner invitation list and its request-boundary tests.
export const TeamInvitationSchema = z.object({
  id: z.string().uuid(), email: z.string(), createdAtUtc: z.string(), expiresAtUtc: z.string(),
  acceptedAtUtc: z.string().nullish().transform(value => value ?? null),
  revokedAtUtc: z.string().nullish().transform(value => value ?? null),
});
export const states = ['ToDo', 'InProgress', 'Done', 'Canceled'] as const;
export const TaskSchema = z.object({
  id: z.number().int(), projectId: z.number().int(), performerId: z.number().int(),
  name: z.string(), description: z.string(), state: z.enum(states), createdAt: z.string(),
  finishedAt: z.string().nullish().transform(value => value ?? null), dueDate: z.string().nullish().transform(value => value ?? null),
  reminderEnabled: z.boolean(), reminderOffsetMinutes: z.number().int().nullish().transform(value => value ?? null),
  escalationEnabled: z.boolean(), escalationDelayMinutes: z.number().int().nullish().transform(value => value ?? null),
  reminderSentAt: z.string().nullish().transform(value => value ?? null), escalationSentAt: z.string().nullish().transform(value => value ?? null),
  performer: UserSchema.optional(), version: z.string().uuid().refine(value => value !== '00000000-0000-0000-0000-000000000000')
});
export type TaskRecord = z.infer<typeof TaskSchema>;
export const ProjectSchema = z.object({
  id: z.number().int(), authorId: z.number().int(), teamId: z.number().int(),
  name: z.string(), description: z.string(), deadline: z.string(), createdAt: z.string(),
  team: TeamSchema.optional(), tasks: z.array(TaskSchema).optional()
});
export type Project = z.infer<typeof ProjectSchema>;
export const TaskPageSchema = z.object({ items: z.array(TaskSchema), page: z.number().int(), pageSize: z.number().int(), totalCount: z.number().int() });

const aiVersion = z.string().uuid().refine(value => value !== '00000000-0000-0000-0000-000000000000');
const nullableText = z.string().nullish().transform(value => value ?? null);
const nullableId = z.number().int().positive().nullish().transform(value => value ?? null);
const nullableInstant = z.iso.datetime({ offset: true }).nullish().transform(value => value ?? null);
export const AIStatusSchema = z.object({ available: z.boolean(), provider: z.enum(['azure', 'disabled', 'fake']),
  reasonCode: z.string().max(100).nullish().transform(value => value ?? null),
  maxInputCharacters: z.number().int().positive(), maxItems: z.number().int().positive() });
export const AIDraftItemSchema = z.object({ draftItemId: aiVersion, title: z.string().max(100), description: z.string().max(1000),
  proposedAssigneeId: nullableId, originalAssigneeText: nullableText, proposedDueAtUtc: nullableInstant, originalDueText: nullableText,
  sourceQuote: z.string(), sourceStart: z.number().int().nonnegative(), sourceLength: z.number().int().nonnegative(), isSuggestion: z.boolean(),
  interpretations: z.array(z.string()), clarificationReasons: z.array(z.string()), materializedTaskId: nullableId,
}).refine(item => item.sourceQuote.length === item.sourceLength, { message: 'The source excerpt length is invalid.' });
export const AIDraftSchema = z.object({ id: aiVersion, ownerId: z.number().int().positive(), projectId: z.number().int().positive(), version: aiVersion,
  createdAtUtc: z.iso.datetime({ offset: true }), expiresAtUtc: z.iso.datetime({ offset: true }), locale: z.enum(['uk', 'en']), timeZoneId: z.string().min(1),
  referenceDate: z.iso.date(), items: z.array(AIDraftItemSchema) });
export const AICommitReceiptSchema = z.object({ draftId: aiVersion, draftVersion: aiVersion,
  items: z.array(z.object({ draftItemId: aiVersion, taskId: z.number().int().positive(), taskVersion: aiVersion, outboxMessageId: aiVersion })) });
export type AIDraft = z.infer<typeof AIDraftSchema>;
export type AIDraftItem = z.infer<typeof AIDraftItemSchema>;
export type AICommitReceipt = z.infer<typeof AICommitReceiptSchema>;
