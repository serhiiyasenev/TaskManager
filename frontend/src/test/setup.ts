import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, vi } from 'vitest';
import { localeStorageKey, setLocale } from '../lib/i18n';
afterEach(() => {
  cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals();
  setLocale('uk');
  try { localStorage.removeItem(localeStorageKey); } catch { /* Tests may model unavailable browser storage. */ }
});
