// Existing SQL datetime2 values represent UTC but may serialize without a suffix.
export function parseUtc(value: string): Date {
  return new Date(/(?:Z|[+-]\d\d:\d\d)$/i.test(value) ? value : `${value}Z`);
}
export function formatDate(value: string | null | undefined): string {
  if (!value) return 'Без строку';
  return new Intl.DateTimeFormat('uk-UA', { dateStyle: 'medium', timeStyle: 'short' }).format(parseUtc(value));
}
export function toLocalInput(value: string | null): string {
  if (!value) return '';
  const date = parseUtc(value);
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}
export function toUtc(value: string): string | null { return value ? new Date(value).toISOString() : null; }
