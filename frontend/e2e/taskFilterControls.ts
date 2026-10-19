import { expect } from '@playwright/test';
import type { Page } from '@playwright/test';

// Tests that use advanced task fields follow the same explicit mobile action
// as a person. Desktop fields remain visible without opening a disclosure.
export async function revealTaskFilters(page: Page) {
  if (!await page.evaluate(() => matchMedia('(max-width: 760px)').matches)) return;
  const toggle = page.locator('.task-filters button[aria-controls][aria-expanded]');
  await expect(toggle).toBeVisible();
  if (await toggle.getAttribute('aria-expanded') === 'false') {
    await expect(toggle).toHaveAccessibleName(/^(Показати фільтри|Show filters)$/);
    await toggle.click();
  }
  await expect(toggle).toHaveAttribute('aria-expanded', 'true');
}
