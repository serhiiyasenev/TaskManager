import { useId } from 'react';
import { commonCopy } from '../lib/commonCopy';
import { useLocale } from '../lib/i18n';
import type { Locale } from '../lib/i18n';

export function LanguageSwitcher() {
  const { locale, setLocale } = useLocale();
  const id = useId();
  return <div className="language-switcher">
    <label htmlFor={id}>{commonCopy[locale].language}</label>
    <select id={id} value={locale} onChange={event => setLocale(event.target.value as Locale)}>
      <option value="uk">Українська</option><option value="en">English</option>
    </select>
  </div>;
}
