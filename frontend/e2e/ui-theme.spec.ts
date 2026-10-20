import { expect, test } from '@playwright/test';
import type { Locator, Page } from '@playwright/test';
import { longTaskName, syntheticApi } from './syntheticApi';
import { revealTaskFilters } from './taskFilterControls';

// Theme/accessibility evidence uses invented UI fixtures only. These tests do
// not establish real Identity, SQL, notification transport or AI availability.
test.use({ trace: 'off', screenshot: 'off', video: 'off' });

test('workspace language stays in the sidebar footer and persists with the brand favicon available', async ({ page, request }) => {
  await syntheticApi(page); await page.goto('/');
  const footer = page.locator('.sidebar-footer');
  const selector = footer.getByLabel('Мова інтерфейсу', { exact: true });
  await expect(selector).toBeVisible();
  await expect(page.locator('.topbar select')).toHaveCount(0);
  if (page.viewportSize()!.width > 760) {
    const bounds = await footer.boundingBox();
    expect(bounds!.x).toBeLessThan(248);
    expect(bounds!.y + bounds!.height).toBeGreaterThan(page.viewportSize()!.height - 40);
  }
  await selector.selectOption('en');
  await expect(footer.getByLabel('Interface language', { exact: true })).toHaveValue('en');
  await page.reload();
  await expect(footer.getByLabel('Interface language', { exact: true })).toHaveValue('en');
  await footer.getByLabel('Interface language', { exact: true }).selectOption('uk');
  await expect(selector).toHaveValue('uk');
  await fits(page);
  const iconPath = await page.locator('link[rel="icon"]').getAttribute('href');
  expect(iconPath).toBe('/favicon.svg');
  const icon = await request.get(iconPath!);
  expect(icon.ok()).toBe(true);
  expect(icon.headers()['content-type']).toContain('image/svg+xml');
});

async function paint(locator: Locator, placeholder = false) {
  return locator.evaluate((element, isPlaceholder) => {
    type Color = [number, number, number, number];
    const context = document.createElement('canvas').getContext('2d', { willReadFrequently: true });
    if (!context) throw new Error('Computed-color verification requires a canvas color resolver.');
    // Browser color parsing also supports modern CSS color syntax; a numeric
    // regex would incorrectly treat oklch()/color() values as sRGB channels.
    function color(value: string): Color {
      context!.clearRect(0, 0, 1, 1);
      context!.fillStyle = value; context!.fillRect(0, 0, 1, 1);
      const data = context!.getImageData(0, 0, 1, 1).data;
      return [data[0], data[1], data[2], data[3] / 255];
    }
    function over(top: Color, bottom: Color): Color {
      const alpha = top[3] + bottom[3] * (1 - top[3]);
      if (!alpha) return [0, 0, 0, 0];
      return [0, 1, 2].map(index => (top[index] * top[3] + bottom[index] * bottom[3] * (1 - top[3])) / alpha)
        .concat(alpha) as Color;
    }
    function luminance(value: Color) {
      return value.slice(0, 3).map(channel => {
        const scaled = channel / 255;
        return scaled <= .04045 ? scaled / 12.92 : ((scaled + .055) / 1.055) ** 2.4;
      }).reduce((sum, channel, index) => sum + channel * [.2126, .7152, .0722][index], 0);
    }
    function contrast(a: Color, b: Color) {
      const first = luminance(a); const second = luminance(b);
      return (Math.max(first, second) + .05) / (Math.min(first, second) + .05);
    }
    function layers(target: Element | null) {
      const ancestors: Element[] = [];
      for (let current = target; current; current = current.parentElement) ancestors.unshift(current);
      let background: Color = [255, 255, 255, 1];
      const frames = ancestors.map(ancestor => {
        const style = getComputedStyle(ancestor); const before = background;
        background = over(color(style.backgroundColor), background);
        return { before, opacity: Number(style.opacity) };
      });
      return { background, frames };
    }
    function opacityGroups(value: Color, frames: ReturnType<typeof layers>['frames']) {
      for (const frame of [...frames].reverse())
        if (frame.opacity < 1) value = over([value[0], value[1], value[2], value[3] * frame.opacity], frame.before);
      return value;
    }
    const style = getComputedStyle(element);
    const textStyle = isPlaceholder ? getComputedStyle(element, '::placeholder') : style;
    const own = layers(element); const parent = layers(element.parentElement);
    const background = opacityGroups(own.background, own.frames);
    const text = color(textStyle.color);
    if (isPlaceholder) text[3] *= Number(textStyle.opacity);
    const foreground = opacityGroups(over(text, own.background), own.frames);
    const surround = opacityGroups(parent.background, parent.frames);
    const outline = over(color(style.outlineColor), surround);
    const border = over(color(style.borderTopColor), background);
    const large = Number.parseFloat(textStyle.fontSize) >= 24
      || Number.parseFloat(textStyle.fontSize) >= 18.6667 && Number(textStyle.fontWeight) >= 700;
    return { background: background.slice(0, 3), luminance: luminance(background),
      neutralSpread: Math.max(...background.slice(0, 3)) - Math.min(...background.slice(0, 3)),
      textContrast: contrast(foreground, background), minimumTextContrast: large ? 3 : 4.5,
      borderContrast: Math.min(contrast(border, background), contrast(border, surround)),
      outlineContrast: contrast(outline, surround), outlineWidth: Number.parseFloat(style.outlineWidth),
      outlineStyle: style.outlineStyle, fontFamily: style.fontFamily, colorScheme: style.colorScheme };
  }, placeholder);
}

async function charcoal(locator: Locator, description: string) {
  const result = await paint(locator);
  expect(result.luminance, `${description} must use a dark surface.`).toBeLessThan(.12);
  expect(result.neutralSpread, `${description} must retain the restrained charcoal palette.`).toBeLessThan(48);
}
async function textContrast(locator: Locator) {
  const result = await paint(locator);
  expect(result.textContrast, 'Rendered text must meet its measured WCAG contrast threshold.').toBeGreaterThanOrEqual(result.minimumTextContrast);
}
async function textGroup(page: Page, selector: string) {
  const elements = page.locator(selector); let checked = 0;
  for (let index = 0; index < await elements.count(); index++) {
    const item = elements.nth(index);
    if (!await item.isVisible() || !(await item.textContent())?.trim()) continue;
    await textContrast(item); checked++;
  }
  expect(checked, 'A contrast group must contain rendered text rather than pass vacuously.').toBeGreaterThan(0);
}
async function amber(locator: Locator) {
  const result = await paint(locator); const [red, green, blue] = result.background;
  expect(red >= 180 && green >= 120 && blue <= 140 && red >= green + 10 && green >= blue + 25,
    'The primary action must use the amber accent, with a readable foreground.').toBe(true);
  await textContrast(locator);
}
async function controls(page: Page, selector: string) {
  const elements = page.locator(selector); let checked = 0;
  for (let index = 0; index < await elements.count(); index++) {
    const item = elements.nth(index);
    // Inactive controls are exempt from minimum text/non-text contrast.
    if (!await item.isVisible() || !await item.isEnabled()) continue;
    await charcoal(item, 'An enabled form control'); await textContrast(item);
    expect((await paint(item)).borderContrast, 'An enabled control boundary must contrast with its fill and surroundings.').toBeGreaterThanOrEqual(3);
    checked++;
  }
  expect(checked).toBeGreaterThan(0);
}
async function focusRing(page: Page, locator: Locator) {
  await page.keyboard.press('Tab'); await locator.focus();
  await expect(locator).toBeFocused();
  const result = await paint(locator);
  expect(result.outlineStyle).not.toBe('none');
  expect(result.outlineWidth).toBeGreaterThanOrEqual(2);
  expect(result.outlineContrast, 'A keyboard focus ring must contrast with the surrounding surface.').toBeGreaterThanOrEqual(3);
}
async function fits(page: Page) {
  const widths = await page.evaluate(() => ({ viewport: document.documentElement.clientWidth,
    document: document.documentElement.scrollWidth }));
  const overflow = widths.document > widths.viewport + 1 ? await page.evaluate(() => [...document.querySelectorAll('body *')]
    .filter(element => element.getBoundingClientRect().right > document.documentElement.clientWidth + 1
      && !element.closest('nav, .board, .table-wrap'))
    .map(element => ({ tag: element.tagName, className: element.className, right: element.getBoundingClientRect().right }))) : [];
  expect(widths.document, `The document must fit while named work regions own horizontal scrolling. ${JSON.stringify(overflow)}`).toBeLessThanOrEqual(widths.viewport + 1);
}
async function glyphsFit(page: Page, selector: string) {
  const readable = await page.locator(selector).evaluateAll(elements => {
    const context = document.createElement('canvas').getContext('2d');
    if (!context || !elements.length) return false;
    return elements.every(element => {
      const style = getComputedStyle(element);
      context.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
      const metrics = context.measureText('Égjpq');
      const available = element.clientHeight - Number.parseFloat(style.paddingTop) - Number.parseFloat(style.paddingBottom);
      return available + 1 >= metrics.actualBoundingBoxAscent + metrics.actualBoundingBoxDescent;
    });
  });
  expect(readable, 'Enlarged text must fit the actual input/select content boxes.').toBe(true);
}

test('charcoal dashboard and Board retain amber actions and accessible normal status error and focus colors', async ({ page }) => {
  const api = await syntheticApi(page, { longNames: true }); await page.goto('/');
  await expect(page.getByRole('link', { name: longTaskName, exact: true })).toBeVisible();
  const date = page.locator('.dashboard-date');
  await expect(date).toContainText(/січня|лютого|березня|квітня|травня|червня|липня|серпня|вересня|жовтня|листопада|грудня/);
  expect(await date.evaluate(element => getComputedStyle(element).textTransform)).toBe('none');
  const statisticHeights = await page.locator('.dashboard-stats .stat').evaluateAll(elements => elements.map(element => element.getBoundingClientRect().height));
  expect(Math.max(...statisticHeights)).toBeLessThan(page.viewportSize()!.width > 1100 ? 160 : 205);
  expect(await page.locator('.dashboard-row-deadline').first().evaluate(element => Number.parseFloat(getComputedStyle(element).fontSize))).toBeGreaterThanOrEqual(14);
  await charcoal(page.locator('html'), 'The application canvas');
  expect((await paint(page.locator('html'))).colorScheme).toContain('dark');
  await charcoal(page.locator('.panel').first(), 'A dashboard panel');
  await charcoal(page.locator('.stat').first(), 'A dashboard statistic');
  await textGroup(page, '.page-header h1, .page-header p, .stat p, .stat strong, .stat small, .panel h2, .task-row h3 a, .task-row p, .due, .sidebar nav a');
  expect((await paint(page.locator('.brand'))).fontFamily).toMatch(/monospace|consolas|courier|menlo|sfmono|cascadia/i);
  const skip = page.getByRole('link', { name: 'Перейти до вмісту', exact: true });
  await focusRing(page, skip); await textContrast(skip);
  const primary = page.locator('.page-header').getByRole('link', { name: '＋ Створити завдання', exact: true });
  await amber(primary); await focusRing(page, primary); await fits(page);

  await page.goto('/tasks');
  await expect(page.getByRole('region', { name: 'Список завдань', exact: true })).toBeVisible();
  await expect(page.locator('.tasks-table .badge')).toHaveCount(4);
  await revealTaskFilters(page);
  await textGroup(page, '.tasks-table .badge, .tasks-table .due, .tasks-table th, .task-filters .field');
  await controls(page, '.task-filters input:not([type=checkbox]), .task-filters select');
  const placeholder = await paint(page.getByLabel('Пошук', { exact: true }), true);
  expect(placeholder.textContrast, 'Visible search placeholder text must remain readable on charcoal controls.').toBeGreaterThanOrEqual(4.5);
  await page.getByRole('button', { name: 'Дошка', exact: true }).click();
  const board = page.getByRole('region', { name: 'Дошка завдань поточної сторінки', exact: true });
  await expect(board).toBeVisible(); await focusRing(page, board);
  await charcoal(board.locator('.board-column').first(), 'A Board column');
  await charcoal(board.locator('.task-card').first(), 'A Board card');
  await textGroup(page, '.board-title h2, .board-title > span, .task-card h3 a, .task-card small, .task-card .due');
  await controls(page, '.task-card select'); await fits(page);

  api.settings.taskFailure = 503; await page.goto('/tasks');
  await expect(page.getByRole('alert')).toContainText('Сервіс тимчасово недоступний');
  await textGroup(page, '.alert-error, .alert-error p, .alert-error small, .alert-error button');
  await fits(page);
});

test('charcoal authentication preserves labelled keyboard controls amber submit and readable 200 percent text', async ({ page }) => {
  const api = await syntheticApi(page, { anonymous: true }); await page.goto('/login');
  const email = page.getByLabel('Email', { exact: true });
  await expect(email).toBeVisible();
  await charcoal(page.locator('html'), 'The anonymous canvas');
  await charcoal(page.locator('.auth-panel'), 'The authentication panel');
  await controls(page, '.auth-panel input, .auth-language select');
  await amber(page.getByRole('button', { name: 'Увійти', exact: true }));
  await textGroup(page, '.auth-panel h1, .auth-panel .muted, .auth-panel label, .auth-panel a, .auth-language label');
  await focusRing(page, email); await email.fill('theme-fixture@example.test');
  await page.keyboard.press('Tab');
  await expect(page.getByLabel('Пароль', { exact: true })).toBeFocused();
  await fits(page);

  await page.addStyleTag({ content: 'html { font-size: 200% !important; }' });
  await page.getByLabel('Мова інтерфейсу', { exact: true }).selectOption('en');
  await expect(page.getByRole('heading', { name: 'Sign in to TaskManager', exact: true })).toBeVisible();
  await expect(email).toHaveValue('theme-fixture@example.test');
  await glyphsFit(page, '.auth-panel input, .auth-language select');
  await controls(page, '.auth-panel input, .auth-language select');
  await textGroup(page, '.auth-panel h1, .auth-panel .muted, .auth-panel label, .auth-panel a');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.locator('.field-error').first()).toBeVisible();
  await textGroup(page, '.field-error');
  expect(api.captured.some(call => call.path === '/api/auth/login' && call.method === 'POST')).toBe(false);
  await fits(page);
});

test('charcoal dashboard and Board reflow with 200 percent text while preserving bilingual labels and user content', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 900 });
  await syntheticApi(page, { longNames: true }); await page.goto('/');
  await expect(page.getByRole('link', { name: longTaskName, exact: true })).toBeVisible();
  // Text enlargement is intentionally distinct from native browser chrome zoom.
  await page.addStyleTag({ content: 'html { font-size: 200% !important; }' });
  await textGroup(page, '.page-header h1, .page-header p, .stat p, .stat strong, .task-row h3 a, .due');
  await fits(page);
  await page.getByRole('navigation', { name: 'Головна навігація', exact: true }).getByRole('link', { name: 'Завдання', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Список завдань', exact: true })).toBeVisible();
  await revealTaskFilters(page);
  await glyphsFit(page, '.task-filters input:not([type=checkbox]), .task-filters select');
  await page.getByRole('button', { name: 'Дошка', exact: true }).click();
  await page.getByLabel('Мова інтерфейсу', { exact: true }).selectOption('en');
  const board = page.getByRole('region', { name: 'Task board for the current page', exact: true });
  await expect(board.getByRole('link', { name: longTaskName, exact: true })).toBeVisible();
  await expect(board.getByLabel(`Status: ${longTaskName}`, { exact: true })).toBeVisible();
  await glyphsFit(page, '.task-filters input:not([type=checkbox]), .task-filters select, .task-card select');
  await textGroup(page, '.board-title h2, .board-title > span, .task-card h3 a, .task-card small, .task-card .due');
  await focusRing(page, board);
  const before = await board.evaluate(element => ({ left: element.scrollLeft, content: element.scrollWidth, width: element.clientWidth }));
  if (before.content > before.width + 1) {
    await page.keyboard.press('ArrowRight');
    await expect.poll(() => board.evaluate(element => element.scrollLeft)).toBeGreaterThan(before.left);
  }
  await expect(board).toBeFocused(); await fits(page);
  const navigation = page.getByRole('navigation', { name: 'Main navigation', exact: true });
  await navigation.getByRole('link', { name: 'Notifications', exact: true }).focus();
  const lastItem = await navigation.getByRole('link', { name: 'Notifications', exact: true }).boundingBox();
  const navigationBox = await navigation.boundingBox();
  expect(lastItem!.x).toBeGreaterThanOrEqual(navigationBox!.x);
  expect(lastItem!.x + lastItem!.width).toBeLessThanOrEqual(navigationBox!.x + navigationBox!.width + 1);
  await fits(page);
});

test('enlarged dashboard fits with portable fallback font metrics', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 900 });
  await syntheticApi(page, { longNames: true }); await page.goto('/');
  await expect(page.getByRole('link', { name: longTaskName, exact: true })).toBeVisible();
  for (const family of ['Arial, sans-serif', 'Verdana, sans-serif']) {
    await page.addStyleTag({ content: `html { font-size: 200% !important; font-family: ${family} !important; --mono: monospace !important; }` });
    const layout = await page.evaluate(() => ({ width: document.documentElement.scrollWidth,
    overflow: [...document.querySelectorAll('body *')].filter(element => {
      const box = element.getBoundingClientRect();
      return box.right > 321 && !element.closest('nav');
    }).map(element => ({ tag: element.tagName, className: element.className, width: element.getBoundingClientRect().width, right: element.getBoundingClientRect().right })) }));
    expect(layout.width, `${family}: ${JSON.stringify(layout.overflow)}`).toBeLessThanOrEqual(321);
  }
});
