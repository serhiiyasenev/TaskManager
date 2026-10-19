import { useSyncExternalStore } from 'react';

export type Locale = 'uk' | 'en';
export const localeStorageKey = 'taskmanager.ui-locale';
export const intlLocales = { uk: 'uk-UA', en: 'en-GB' } as const;
type Catalog = Readonly<Record<Locale, Readonly<Record<string, unknown>>>>;
const listeners = new Set<() => void>();
function validLocale(value: unknown): value is Locale { return value === 'uk' || value === 'en'; }
function storedLocale(): Locale {
  try { const value = localStorage.getItem(localeStorageKey); return validLocale(value) ? value : 'uk'; }
  catch { return 'uk'; }
}
let locale: Locale = storedLocale();
function applyLocale(next: Locale) {
  const changed = locale !== next;
  locale = next;
  if (typeof document !== 'undefined') document.documentElement.lang = next;
  if (changed) for (const listener of listeners) listener();
}
applyLocale(locale);
export function getLocale(): Locale { return locale; }
export function setLocale(next: Locale): void {
  if (!validLocale(next)) return;
  try { localStorage.setItem(localeStorageKey, next); } catch { /* The current UI still switches when storage is unavailable. */ }
  applyLocale(next);
}
if (typeof window !== 'undefined') window.addEventListener('storage', event => {
  if (event.key === localeStorageKey || event.key === null) applyLocale(validLocale(event.newValue) ? event.newValue : 'uk');
});
function subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; }
export function useLocale() {
  const selected = useSyncExternalStore(subscribe, getLocale, () => 'uk' as Locale);
  return { locale: selected, intlLocale: intlLocales[selected], setLocale };
}
export function translateCopyText(value: string, catalog: Catalog, selected: Locale = getLocale()): string {
  function match(source: unknown, target: unknown): string | undefined {
    if (typeof source === 'string') return source === value && typeof target === 'string' ? target : undefined;
    if (source && target && typeof source === 'object' && typeof target === 'object') {
      const targetValues = target as Record<string, unknown>;
      for (const [key, entry] of Object.entries(source)) {
        const result = match(entry, targetValues[key]);
        if (result !== undefined) return result;
      }
    }
    return undefined;
  }
  return match(catalog.uk, catalog[selected]) ?? match(catalog.en, catalog[selected]) ?? value;
}
