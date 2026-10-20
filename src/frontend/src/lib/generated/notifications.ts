// Generated from contracts/notifications.openapi.json. Run the repository generator.
import { z } from 'zod';

export const NotificationInboxItemSchema = z.strictObject({
  id: z.string().uuid(),
  messageId: z.string().uuid(),
  kind: z.union([z.literal(0), z.literal(1), z.literal(2), z.literal(3)]),
  taskId: z.number().int().min(1).max(2147483647),
  projectId: z.number().int().min(1).max(2147483647),
  taskName: z.string().min(1).max(100),
  projectName: z.string().min(1).max(100),
  occurredAtUtc: z.iso.datetime({ offset: true }),
  readAtUtc: z.iso.datetime({ offset: true }).nullable().optional().default(null),
});
export type NotificationInboxItem = z.infer<typeof NotificationInboxItemSchema>;

export const NotificationInboxPageSchema = z.strictObject({
  items: z.array(NotificationInboxItemSchema).max(100),
  page: z.number().int().min(1).max(100000),
  pageSize: z.number().int().min(1).max(100),
  totalCount: z.number().int().min(0).max(2147483647),
  unreadCount: z.number().int().min(0).max(2147483647),
});
export type NotificationInboxPage = z.infer<typeof NotificationInboxPageSchema>;

export const NotificationHintSchema = z.strictObject({
  notificationId: z.string().uuid(),
});
export type NotificationHint = z.infer<typeof NotificationHintSchema>;

export const NotificationAdminInboxItemSchema = z.strictObject({
  id: z.string().uuid(),
  messageId: z.string().uuid(),
  kind: z.union([z.literal(0), z.literal(1), z.literal(2), z.literal(3)]),
  taskId: z.number().int().min(1).max(2147483647),
  projectId: z.number().int().min(1).max(2147483647),
  taskName: z.string().min(1).max(100),
  projectName: z.string().min(1).max(100),
  occurredAtUtc: z.iso.datetime({ offset: true }),
  recipientId: z.number().int().min(1).max(2147483647),
  recipientName: z.string().min(1),
  readAtUtc: z.iso.datetime({ offset: true }).nullable().optional().default(null),
});
export type NotificationAdminInboxItem = z.infer<typeof NotificationAdminInboxItemSchema>;

export const NotificationAdminInboxPageSchema = z.strictObject({
  items: z.array(NotificationAdminInboxItemSchema).max(100),
  page: z.number().int().min(1).max(100000),
  pageSize: z.number().int().min(1).max(100),
  totalCount: z.number().int().min(0).max(2147483647),
  unreadCount: z.number().int().min(0).max(2147483647),
});
export type NotificationAdminInboxPage = z.infer<typeof NotificationAdminInboxPageSchema>;
