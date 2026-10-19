import { act, renderHook } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import { setLocale } from '../../lib/i18n';
import { useWorkCopy, workCopy } from './workCopy';

afterEach(() => setLocale('uk'));

it('inflects Ukrainian and English task counts, including teens and fractions, after a locale switch', () => {
  setLocale('uk');
  const { result } = renderHook(() => useWorkCopy());
  const counts = [0, 1, 2, 3, 5, 11, 21, 22, 111, 1.5, 5.0001, 1e-21];
  const uk = ['завдань', 'завдання', 'завдання', 'завдання', 'завдань', 'завдань', 'завдання', 'завдання', 'завдань', 'завдання', 'завдання', 'завдання'];
  expect(counts.map(count => result.current.text('taskCount', { count }))).toEqual(counts.map((count, index) => `${count} ${uk[index]}`));
  act(() => setLocale('en'));
  expect(counts.map(count => result.current.text('taskCount', { count }))).toEqual(counts.map(count => `${count} ${count === 1 ? 'task' : 'tasks'}`));
  expect(result.current.text('welcome', { name: 'User {count} $&' })).toBe('Welcome, User {count} $&');
});

it('keeps filter labels, catalog keys and interpolation placeholders aligned', () => {
  expect(Object.keys(workCopy.en).sort()).toEqual(Object.keys(workCopy.uk).sort());
  for (const key of Object.keys(workCopy.uk) as Array<keyof typeof workCopy.uk>) {
    const placeholders = (value: string) => [...value.matchAll(/\{(\w+)\}/g)].map(match => match[1]).sort();
    expect(placeholders(workCopy.en[key]), key).toEqual(placeholders(workCopy.uk[key]));
  }
  const { result } = renderHook(() => useWorkCopy());
  expect(result.current.copy).toMatchObject({ filters: 'Фільтри', showFilters: 'Показати фільтри', hideFilters: 'Сховати фільтри' });
  act(() => setLocale('en'));
  expect(result.current.copy).toMatchObject({ filters: 'Filters', showFilters: 'Show filters', hideFilters: 'Hide filters' });
});
