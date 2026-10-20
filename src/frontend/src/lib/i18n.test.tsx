import { act, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { getLocale, localeStorageKey, setLocale, translateCopyText, useLocale } from './i18n';
import { commonCopy, apiMessages } from './commonCopy';
import { ApiError } from './api';
import { ErrorNotice } from '../components/Feedback';

function LocalePreview() {
  const { locale, intlLocale } = useLocale();
  return <p>{locale}:{intlLocale}</p>;
}
afterEach(() => { act(() => setLocale('uk')); localStorage.removeItem(localeStorageKey); });

describe('public interface locale', () => {
  it('defaults to Ukrainian', () => {
    render(<LocalePreview />);
    expect(screen.getByText('uk:uk-UA')).toBeInTheDocument();
  });
  it('reactively switches the UI and document language', () => {
    render(<LocalePreview />);
    act(() => setLocale('en'));
    expect(screen.getByText('en:en-GB')).toBeInTheDocument();
    expect(getLocale()).toBe('en');
    expect(document.documentElement.lang).toBe('en');
  });
  it('persists only the selected public locale', () => {
    act(() => setLocale('en'));
    expect(localStorage.getItem(localeStorageKey)).toBe('en');
  });
  it('still switches when browser storage is unavailable', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('Unavailable', 'SecurityError'); });
    render(<LocalePreview />);
    act(() => setLocale('en'));
    expect(screen.getByText('en:en-GB')).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('en');
  });
  it('updates another tab and rejects an invalid stored language', () => {
    render(<LocalePreview />);
    act(() => window.dispatchEvent(new StorageEvent('storage', { key: localeStorageKey, newValue: 'en' })));
    expect(screen.getByText('en:en-GB')).toBeInTheDocument();
    act(() => window.dispatchEvent(new StorageEvent('storage', { key: localeStorageKey, newValue: 'invalid' })));
    expect(screen.getByText('uk:uk-UA')).toBeInTheDocument();
  });
  it('re-translates an existing safe API error after switching', () => {
    const error = new ApiError(502, 'public-support-reference');
    render(<ErrorNotice error={error} retry={() => undefined} />);
    expect(screen.getByRole('alert')).toHaveTextContent(apiMessages.uk[502]);
    act(() => setLocale('en'));
    expect(screen.getByRole('alert')).toHaveTextContent(apiMessages.en[502]);
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('Support reference: public-support-reference');
  });
  it('has matching shared copy keys and interpolation placeholders', () => {
    for (const catalog of [commonCopy, apiMessages]) {
      expect(Object.keys(catalog.en).sort()).toEqual(Object.keys(catalog.uk).sort());
      const english: Record<string, string> = catalog.en;
      for (const [key, value] of Object.entries(catalog.uk)) {
        expect(english[key].match(/\{\w+\}/g) ?? []).toEqual(value.match(/\{\w+\}/g) ?? []);
      }
    }
  });
  it('translates stored known UI messages without changing unknown user text', () => {
    const catalog = { uk: { required: 'Заповніть поле.', nested: { saved: 'Збережено.' } }, en: { required: 'Complete this field.', nested: { saved: 'Saved.' } } };
    expect(translateCopyText('Заповніть поле.', catalog, 'en')).toBe('Complete this field.');
    expect(translateCopyText('Saved.', catalog, 'uk')).toBe('Збережено.');
    expect(translateCopyText('Назва користувацького проєкту', catalog, 'en')).toBe('Назва користувацького проєкту');
  });
});
