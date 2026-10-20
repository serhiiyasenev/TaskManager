import { expect, test } from '@playwright/test';
import type { Page } from '@playwright/test';
import { longTaskName, syntheticApi } from './syntheticApi';

// Rendered UI only: these cases deliberately use the existing invented API.
// They provide no SQL, cookie authorization or model-provider evidence.
test.use({ trace: 'off', screenshot: 'off', video: 'off' });

async function documentFits(page: Page) {
  const widths = await page.evaluate(() => ({ viewport: document.documentElement.clientWidth,
    document: document.documentElement.scrollWidth, pageScroll: window.scrollX }));
  expect(widths.document, 'Nested work regions must own horizontal overflow.').toBeLessThanOrEqual(widths.viewport + 1);
  expect(widths.pageScroll).toBe(0);
}

test('synthetic narrow and tablet Board scrolls by keyboard without page overflow', async ({ page }) => {
  await syntheticApi(page, { longNames: true });
  for (const width of [320, 820]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/tasks?view=board');
    const board = page.getByRole('region', { name: 'Дошка завдань поточної сторінки', exact: true });
    await expect(board.getByRole('link', { name: longTaskName, exact: true })).toBeVisible();
    await documentFits(page);
    const bounds = await board.evaluate(element => ({ width: element.clientWidth, content: element.scrollWidth,
      left: element.getBoundingClientRect().left, right: element.getBoundingClientRect().right }));
    expect(bounds.content).toBeGreaterThan(bounds.width);
    expect(bounds.left).toBeGreaterThanOrEqual(0);
    expect(bounds.right).toBeLessThanOrEqual(width + 1);
    // The view toggle is the last filter control; Tab must enter the actual
    // named scroll region rather than trapping users in a pointer-only board.
    await page.getByRole('button', { name: 'Дошка', exact: true }).focus();
    await page.keyboard.press('Tab');
    await expect(board).toBeFocused();
    const initial = await board.evaluate(element => element.scrollLeft);
    await page.keyboard.press('ArrowRight');
    await expect.poll(() => board.evaluate(element => element.scrollLeft)).toBeGreaterThan(initial);
    await documentFits(page);
    const moved = await board.evaluate(element => element.scrollLeft);
    await page.keyboard.press('ArrowLeft');
    await expect.poll(() => board.evaluate(element => element.scrollLeft)).toBeLessThan(moved);
    await expect(board).toBeFocused();
    await documentFits(page);
  }
});

test('synthetic 200 percent text enlargement keeps controls readable and forms usable', async ({ page }) => {
  const api = await syntheticApi(page);
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto('/tasks/new');
  await page.getByLabel('Назва', { exact: true }).fill('Незбережений український текст');
  // This is actual text enlargement, not a claim about browser chrome zoom.
  await page.addStyleTag({ content: 'html { font-size: 200% !important; }' });
  await page.getByLabel('Мова інтерфейсу', { exact: true }).selectOption('en');
  await expect(page.getByRole('heading', { name: 'New task', exact: true })).toBeVisible();
  await expect(page.getByLabel('Name', { exact: true })).toHaveValue('Незбережений український текст');
  const readable = await page.locator('.form-panel input:not([type=checkbox]), .form-panel select').evaluateAll(elements => {
    const canvas = document.createElement('canvas'); const context = canvas.getContext('2d');
    if (!context) return false;
    return elements.every(element => {
      const style = getComputedStyle(element);
      context.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
      const metrics = context.measureText('Égjpq');
      const glyphHeight = metrics.actualBoundingBoxAscent + metrics.actualBoundingBoxDescent;
      const available = element.clientHeight - Number.parseFloat(style.paddingTop) - Number.parseFloat(style.paddingBottom);
      return available + 1 >= glyphHeight;
    });
  });
  expect(readable, 'At 200% text size, input/select content boxes must fit actual ascenders and descenders.').toBe(true);
  await page.getByLabel('Remind before the deadline', { exact: true }).check();
  await page.getByRole('button', { name: 'Create task', exact: true }).click();
  await expect(page.getByText('A deadline is required for reminders.', { exact: true })).toBeVisible();
  await page.getByLabel('Interface language', { exact: true }).selectOption('uk');
  await expect(page.getByText('Для нагадування потрібен строк виконання.', { exact: true })).toBeVisible();
  await expect(page.getByLabel('Назва', { exact: true })).toHaveValue('Незбережений український текст');
  expect(api.captured.some(call => call.path === '/api/tasks' && call.method === 'POST')).toBe(false);
  await documentFits(page);
});
