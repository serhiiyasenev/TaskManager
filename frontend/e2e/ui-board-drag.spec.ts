import { expect, test } from '@playwright/test';
import type { Locator, Page } from '@playwright/test';
import { syntheticApi, versionTwo } from './syntheticApi';

// Invented API fixtures only: moving these cards never writes to local SQL.
test.use({ trace: 'off', screenshot: 'off', video: 'off' });
const taskName = 'Підготувати перевірку доступу';
const handleName = `Перетягнути: ${taskName}`;

async function startMouseDrag(page: Page, handle: Locator) {
  await handle.scrollIntoViewIfNeeded(); const box = await handle.boundingBox();
  expect(box).not.toBeNull();
  const x = box!.x + box!.width / 2; const y = box!.y + box!.height / 2;
  await page.mouse.move(x, y); await page.mouse.down();
  await page.mouse.move(x + 12, y, { steps: 3 });
  return y;
}

test('pointer drag scrolls to a distant column and saves exactly one versioned status command', async ({ page }, testInfo) => {
  const api = await syntheticApi(page); await page.goto('/tasks?view=board');
  const handle = page.getByRole('button', { name: handleName, exact: true });
  await expect(handle).toBeVisible();
  const y = await startMouseDrag(page, handle);
  const board = page.getByRole('region', { name: 'Дошка завдань поточної сторінки', exact: true });
  const bounds = await board.boundingBox(); const viewport = page.viewportSize()!;
  const right = Math.min(bounds!.x + bounds!.width, viewport.width);
  const target = page.locator('.board-column[data-state="Canceled"]');
  await page.mouse.move(right - 8, y, { steps: 12 });
  await expect.poll(async () => {
    const box = await target.boundingBox();
    return box!.x < right - 40;
  }).toBe(true);
  const destination = await target.boundingBox();
  await page.mouse.move(Math.min(right - 24, destination!.x + destination!.width / 2), y, { steps: 6 });
  await expect(target).toHaveAttribute('data-drop-target', 'true');
  await page.screenshot({ path: `../.local/board-drag-20261019/drag-${testInfo.project.name}.png` });
  await page.mouse.up();
  await expect(target.locator('[data-task-id="101"]')).toBeVisible();
  const writes = api.captured.filter(call => call.method === 'PATCH');
  expect(writes).toHaveLength(1);
  expect(writes[0]).toMatchObject({ path: '/api/tasks/101/state', body: { state: 'Canceled' } });
  expect(Object.keys(writes[0].body!).sort()).toEqual(['expectedVersion', 'state']);
  expect(writes[0].body!.expectedVersion).not.toBe(versionTwo);
  expect(writes[0].csrf).toMatch(/^synthetic-csrf-/);
});

test('keyboard drag cancels without a write and a confirmed drop keeps focus on the moved card', async ({ page }) => {
  const api = await syntheticApi(page); await page.goto('/tasks?view=board');
  const handle = page.getByRole('button', { name: handleName, exact: true });
  await handle.focus(); await page.keyboard.press('Space');
  await page.keyboard.press('ArrowRight'); await page.keyboard.press('Escape');
  expect(api.captured.filter(call => call.method === 'PATCH')).toHaveLength(0);
  await expect(page.locator('.board-column[data-state="ToDo"] [data-task-id="101"]')).toBeVisible();
  await handle.focus(); await page.keyboard.press('Space');
  await page.keyboard.press('ArrowRight'); await page.keyboard.press('Enter');
  await expect(page.locator('.board-column[data-state="InProgress"] [data-task-id="101"]')).toBeVisible();
  await expect(handle).toBeFocused();
  expect(api.captured.filter(call => call.method === 'PATCH')).toHaveLength(1);
});

test('pointer click below the drag threshold and release outside the board do not move a card', async ({ page }) => {
  const api = await syntheticApi(page); await page.goto('/tasks?view=board');
  const handle = page.getByRole('button', { name: handleName, exact: true });
  await handle.click();
  expect(api.captured.filter(call => call.method === 'PATCH')).toHaveLength(0);
  await startMouseDrag(page, handle);
  await page.mouse.move(4, 4, { steps: 8 }); await page.mouse.up();
  expect(api.captured.filter(call => call.method === 'PATCH')).toHaveLength(0);
  await expect(page.locator('.board-column[data-state="ToDo"] [data-task-id="101"]')).toBeVisible();
});

test('touch drag from the handle changes a column without navigating to task details', async ({ page }) => {
  const api = await syntheticApi(page); await page.goto('/tasks?view=board');
  const handle = page.getByRole('button', { name: handleName, exact: true });
  await handle.scrollIntoViewIfNeeded(); const start = await handle.boundingBox();
  const board = await page.locator('.board').boundingBox();
  const target = page.locator('.board-column[data-state="InProgress"]');
  const right = Math.min(board!.x + board!.width, page.viewportSize()!.width);
  const y = start!.y + start!.height / 2;
  const cdp = await page.context().newCDPSession(page);
  const point = (x: number) => [{ x, y, id: 1, radiusX: 3, radiusY: 3, force: 1 }];
  try {
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: point(start!.x + start!.width / 2) });
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: point(start!.x + start!.width / 2 + 12) });
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: point(right - 8) });
    await expect.poll(async () => (await target.boundingBox())!.x < right - 40).toBe(true);
    const destination = await target.boundingBox();
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: point(Math.min(right - 24, destination!.x + destination!.width / 2)) });
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
    await expect(target.locator('[data-task-id="101"]')).toBeVisible();
    await expect(page).toHaveURL(/\/tasks\?view=board$/);
    expect(api.captured.filter(call => call.method === 'PATCH')).toHaveLength(1);
  } finally { await cdp.detach(); }
});
