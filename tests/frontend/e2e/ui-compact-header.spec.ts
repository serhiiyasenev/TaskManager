import { expect, test } from '@playwright/test';
import { syntheticApi, syntheticUser } from './syntheticApi';

test.use({ trace: 'off', screenshot: 'off', video: 'off' });

test('compact narrow dashboard preserves language access and readable two-column statistics', async ({ page }) => {
  await syntheticApi(page);
  await page.route('**/api/auth/me', route => route.fulfill({ json: {
    ...syntheticUser, firstName: 'System', lastName: 'Admin', roles: ['admin'],
  } }));
  await page.goto('/');
  for (const width of [390, 684, 760]) {
    await page.setViewportSize({ width, height: 900 });
    for (const locale of ['uk', 'en']) {
      const language = page.locator('.sidebar-footer select');
      await language.selectOption(locale);
      await expect(language).toBeVisible();
      const layout = await page.evaluate(() => {
        const box = (selector: string) => document.querySelector(selector)!.getBoundingClientRect();
        const cards = [...document.querySelectorAll('.dashboard-stats .stat')].map(card => card.getBoundingClientRect());
        return { headerHeight: box('.sidebar').height + box('.topbar').height,
          brandTop: box('.brand').top, languageTop: box('.sidebar-footer select').top,
          columns: new Set(cards.map(card => Math.round(card.left))).size,
          rows: new Set(cards.map(card => Math.round(card.top))).size,
          fits: document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1 };
      });
      expect(layout.headerHeight, `${width}/${locale}`).toBeLessThanOrEqual(200);
      expect(Math.abs(layout.brandTop - layout.languageTop)).toBeLessThan(12);
      expect(layout.columns).toBe(2);
      expect(layout.rows).toBe(2);
      expect(layout.fits).toBe(true);
    }
  }
  await page.setViewportSize({ width: 684, height: 900 });
  await page.locator('.sidebar-footer select').selectOption('uk');
  await page.screenshot({ path: test.info().outputPath('compact-tablet.png'), fullPage: true });
  await page.setViewportSize({ width: 320, height: 900 });
  await page.addStyleTag({ content: 'html { font-size: 200% !important; }' });
  await expect(page.locator('.sidebar-footer select')).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
  expect(await page.locator('.dashboard-stats').evaluate(element =>
    new Set([...element.children].map(card => Math.round(card.getBoundingClientRect().left))).size)).toBe(1);
});
