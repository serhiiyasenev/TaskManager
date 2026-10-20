export function canonicalTimeZone(timeZone: string): string {
  return timeZone === 'Europe/Kiev' ? 'Europe/Kyiv' : timeZone;
}

export function browserTimeZone(): string {
  return canonicalTimeZone(Intl.DateTimeFormat().resolvedOptions().timeZone);
}
