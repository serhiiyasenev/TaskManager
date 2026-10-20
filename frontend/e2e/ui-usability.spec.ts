import { expect, test } from '@playwright/test';
import type { Page, Route } from '@playwright/test';
import { ProjectSchema, TaskPageSchema, TeamSchema } from '../src/lib/contracts';
import { syntheticApi, versionTwo } from './syntheticApi';
import { syntheticInboxApi } from './syntheticInboxApi';
import { revealTaskFilters } from './taskFilterControls';

// Rendered workflow regressions with invented API data only. These cases do
// not provide SQL, real authentication or server authorization evidence.
test.use({ trace: 'off', screenshot: 'off', video: 'off' });

const firstTask = 'Підготувати перевірку доступу';
const secondTask = 'Перевірити запрошення учасників';
const filters = (page: Page) => page.locator('.task-filters');
const projectSelect = (page: Page) => filters(page).getByRole('combobox', { name: /^Проєкт/ });
const assigneeSelect = (page: Page) => filters(page).getByRole('combobox', { name: /^Виконавець/ });
const json = (route: Route, value: unknown, status = 200) => route.fulfill({ status,
  contentType: 'application/json', body: JSON.stringify(value), headers: { 'Cache-Control': 'no-store' } });

async function queryIs(page: Page, expected: Record<string, string | null>) {
  await expect.poll(() => {
    const query = new URL(page.url()).searchParams;
    return Object.fromEntries(Object.keys(expected).map(key => [key, query.get(key)]));
  }).toEqual(expected);
}

test('synthetic task history restores filters while view paging and Clear preserve the intended workspace', async ({ page }) => {
  await syntheticApi(page);
  await page.goto('/tasks?pageSize=1');
  const search = page.getByLabel('Пошук', { exact: true });
  const apply = page.getByRole('button', { name: 'Показати', exact: true });
  await expect(page.getByRole('link', { name: firstTask, exact: true })).toBeVisible();
  await search.fill('Перевірити'); await apply.click();
  await expect(page.getByRole('link', { name: secondTask, exact: true })).toBeVisible();
  await search.fill('Підготувати'); await apply.click();
  await expect(page.getByRole('link', { name: firstTask, exact: true })).toBeVisible();

  await page.goBack();
  await queryIs(page, { search: 'Перевірити' });
  await expect(search).toHaveValue('Перевірити');
  await expect(page.getByRole('link', { name: secondTask, exact: true })).toBeVisible();
  await page.goForward();
  await queryIs(page, { search: 'Підготувати' });
  await expect(search).toHaveValue('Підготувати');
  await expect(page.getByRole('link', { name: firstTask, exact: true })).toBeVisible();

  await page.getByRole('button', { name: 'Дошка', exact: true }).click();
  await page.getByRole('button', { name: 'Очистити фільтри', exact: true }).click();
  await queryIs(page, { view: 'board', pageSize: '1', search: null, page: null });
  await expect(search).toHaveValue('');
  await revealTaskFilters(page);
  await expect(projectSelect(page)).toHaveValue('');
  await expect(assigneeSelect(page)).toHaveValue('');
  await expect(filters(page).getByRole('combobox', { name: /^Статус/ })).toHaveValue('');
  await expect(page.getByRole('region', { name: 'Дошка завдань поточної сторінки', exact: true })).toBeVisible();

  const draft = 'Незастосована чернетка пошуку';
  await search.fill(draft);
  await page.getByRole('button', { name: 'Список', exact: true }).click();
  await expect(search).toHaveValue(draft);
  await page.getByRole('button', { name: 'Дошка', exact: true }).click();
  await expect(search).toHaveValue(draft);
  await page.getByRole('button', { name: 'Далі', exact: true }).click();
  await queryIs(page, { page: '2', search: null, view: 'board', pageSize: '1' });
  await expect(page.getByRole('link', { name: secondTask, exact: true })).toBeVisible();
  await expect(search).toHaveValue(draft);
  await page.getByRole('button', { name: 'Очистити фільтри', exact: true }).click();
  await expect(search).toHaveValue('');
  await queryIs(page, { page: null, search: null, view: 'board', pageSize: '1' });
  await expect(page.getByRole('link', { name: firstTask, exact: true })).toBeVisible();
});

test('synthetic Board saves announce status and restore focus without stealing a newer focus during held PATCH', async ({ page }) => {
  const api = await syntheticApi(page);
  await page.goto('/tasks?view=board');
  const firstStatus = page.getByRole('combobox', { name: `Статус: ${firstTask}`, exact: true });
  const feedback = page.locator('.task-save-feedback');
  await firstStatus.focus(); await firstStatus.selectOption('InProgress');
  await expect(feedback).toHaveAttribute('aria-live', 'polite');
  await expect(feedback).toHaveAttribute('aria-atomic', 'true');
  await expect(feedback).toHaveText(`Статус «${firstTask}» змінено: У роботі.`);
  await expect(firstStatus).toHaveValue('InProgress');
  await expect(firstStatus).toBeEnabled();
  await expect(firstStatus).toBeFocused();
  await expect(page.locator('.board-column[data-state="InProgress"] [data-task-id="101"]')).toBeVisible();

  let release: () => void = () => {};
  let entered: () => void = () => {};
  const held = new Promise<void>(resolve => { release = resolve; });
  const received = new Promise<void>(resolve => { entered = resolve; });
  await page.route('**/api/tasks/102/state', async route => {
    if (route.request().method() !== 'PATCH') return route.fallback();
    entered(); await held; await route.fallback();
  });
  const secondStatus = page.getByRole('combobox', { name: `Статус: ${secondTask}`, exact: true });
  try {
    await secondStatus.focus(); await secondStatus.selectOption('Done');
    await received;
    await expect(secondStatus).toBeDisabled();
    const search = page.getByLabel('Пошук', { exact: true });
    await search.fill('Чернетка під час збереження');
    release();
    await expect(feedback).toHaveText(`Статус «${secondTask}» змінено: Готово.`);
    await expect(secondStatus).toHaveValue('Done');
    await expect(secondStatus).toBeEnabled();
    await expect(search).toBeFocused();
    await expect(search).toHaveValue('Чернетка під час збереження');
    await expect(page.locator('.board-column[data-state="Done"] [data-task-id="102"]')).toBeVisible();
    expect(api.captured.filter(call => call.method === 'PATCH').map(call => ({ path: call.path, keys: Object.keys(call.body ?? {}).sort() })))
      .toEqual([{ path: '/api/tasks/101/state', keys: ['expectedVersion', 'state'] }, { path: '/api/tasks/102/state', keys: ['expectedVersion', 'state'] }]);
  } finally { release(); }
});

test('synthetic filtered Board clamps an emptied second page after its final card changes status', async ({ page }) => {
  await syntheticApi(page);
  await page.goto('/tasks');
  const seed = TaskPageSchema.parse(await page.evaluate(async () => {
    const response = await fetch('/api/tasks/page?page=1&pageSize=20');
    return await response.json() as unknown;
  }));
  expect(seed.items.length).toBeGreaterThan(0);
  const tasks = [
    { ...seed.items[0], id: 201, name: 'Синтетична перша картка', state: 'ToDo' as const },
    { ...seed.items[0], id: 202, name: 'Синтетична остання картка', state: 'ToDo' as const },
  ];
  await page.route('**/api/tasks/page?**', async route => {
    const params = new URL(route.request().url()).searchParams;
    const visible = tasks.filter(task => !params.get('state') || task.state === params.get('state'));
    const pageNumber = Number(params.get('page') ?? 1); const pageSize = Number(params.get('pageSize') ?? 20);
    await json(route, { items: visible.slice((pageNumber - 1) * pageSize, pageNumber * pageSize), page: pageNumber, pageSize, totalCount: visible.length });
  });
  await page.route('**/api/tasks/202/state', async route => {
    const body = route.request().postDataJSON() as { state: string; expectedVersion: string };
    expect(route.request().method()).toBe('PATCH');
    expect(Object.keys(body).sort()).toEqual(['expectedVersion', 'state']);
    expect(body.expectedVersion).toBe(tasks[1].version);
    expect(body.state).toBe('InProgress');
    Object.assign(tasks[1], { state: body.state, version: versionTwo });
    await json(route, tasks[1]);
  });
  await page.goto('/tasks?view=board&state=ToDo&pageSize=1&page=2');
  await expect(page.getByRole('link', { name: tasks[1].name, exact: true })).toBeVisible();
  await page.getByRole('combobox', { name: `Статус: ${tasks[1].name}`, exact: true }).selectOption('InProgress');
  await queryIs(page, { page: '1', pageSize: '1', state: 'ToDo', view: 'board' });
  await expect(page.getByRole('link', { name: tasks[0].name, exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: tasks[1].name, exact: true })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Завдань не знайдено', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Назад', exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Далі', exact: true })).toBeDisabled();
  await expect(page.locator('.task-save-feedback')).toHaveText(`Статус «${tasks[1].name}» змінено: У роботі.`);
});

test('synthetic project team changes clear foreign assignees but retain same-team and Mine intent', async ({ page }) => {
  const api = await syntheticApi(page);
  api.user.roles.push('admin');
  await page.goto('/tasks');
  const seed = await page.evaluate(async () => ({
    projects: await (await fetch('/api/projects')).json() as unknown,
    team: await (await fetch('/api/teams/17')).json() as unknown,
  }));
  const project = ProjectSchema.array().parse(seed.projects)[0];
  const team = TeamSchema.parse(seed.team);
  const otherTeam = { ...team, id: 18, ownerId: 44, name: 'Інша синтетична команда', users: [
    { ...team.users[0], id: 44, teamId: 18, userName: 'synthetic-peer', email: 'peer@example.test', firstName: 'Синтетичний', lastName: 'Учасник' },
  ] };
  const projects = [project, { ...project, id: 72, name: 'Другий проєкт тієї самої команди' },
    { ...project, id: 73, teamId: 18, authorId: 44, team: otherTeam, tasks: [], name: 'Проєкт іншої команди' }];
  await page.route('**/api/projects', route => json(route, projects));
  await page.route('**/api/teams/18', route => json(route, otherTeam));
  await page.goto('/tasks');
  await revealTaskFilters(page);
  await projectSelect(page).selectOption('71');
  await assigneeSelect(page).selectOption('43');
  await projectSelect(page).selectOption('72');
  await expect(assigneeSelect(page)).toHaveValue('43');
  let release: () => void = () => {};
  const submitted = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/api/tasks/page?**', async route => {
    const params = new URL(route.request().url()).searchParams;
    if (params.get('projectId') === '72' && params.get('performerId') === '43') await submitted;
    await route.fallback();
  });
  try {
    // Submit and edit within one browser turn: the navigation and passive
    // effects cannot finish before the newer draft project change arrives.
    await page.evaluate(() => {
      document.querySelector<HTMLFormElement>('.task-filters')!.requestSubmit();
      const project = document.querySelector<HTMLSelectElement>('.task-filters .filter-advanced select')!;
      project.value = '73'; project.dispatchEvent(new Event('change', { bubbles: true }));
    });
    await queryIs(page, { projectId: '72', performerId: '43' });
    await expect(projectSelect(page)).toHaveValue('73');
    await expect(assigneeSelect(page)).toBeEnabled();
    await expect(assigneeSelect(page)).toHaveValue('');
    await expect(assigneeSelect(page).getByRole('option', { name: 'Синтетичний Учасник', exact: true })).toHaveCount(1);
  } finally { release(); }
  await page.getByRole('button', { name: 'Показати', exact: true }).click();
  await queryIs(page, { projectId: '73', performerId: null });

  await projectSelect(page).selectOption('71');
  await page.getByRole('checkbox', { name: 'Мої', exact: true }).check();
  await projectSelect(page).selectOption('73');
  await expect(page.getByRole('checkbox', { name: 'Мої', exact: true })).toBeChecked();
  await page.getByRole('button', { name: 'Показати', exact: true }).click();
  await queryIs(page, { projectId: '73', performerId: '42' });
  await expect.poll(() => api.captured.filter(call => call.path.startsWith('/api/tasks/page?')).map(call => {
    const params = new URL(call.path, 'http://synthetic.invalid').searchParams;
    return { project: params.get('projectId'), performer: params.get('performerId') };
  }).at(-1)).toEqual({ project: '73', performer: '42' });
});

async function activeNavigationFits(page: Page, navigationName: string, linkName: string) {
  const navigation = page.getByRole('navigation', { name: navigationName, exact: true });
  const active = navigation.getByRole('link', { name: linkName, exact: true });
  await expect(active).toHaveAttribute('aria-current', 'page');
  await expect.poll(() => active.evaluate(element => {
    const menu = element.closest('nav')!; const bounds = menu.getBoundingClientRect(); const item = element.getBoundingClientRect();
    return { overflows: menu.scrollWidth > menu.clientWidth, visible: item.left >= bounds.left - 1 && item.right <= bounds.right + 1 };
  })).toEqual({ overflows: true, visible: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
}

test('synthetic mobile navigation reveals the active direct route after language changes and desktop resize', async ({ page }) => {
  await syntheticApi(page);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/notifications');
  await activeNavigationFits(page, 'Головна навігація', 'Сповіщення');
  await page.getByLabel('Мова інтерфейсу', { exact: true }).selectOption('en');
  await activeNavigationFits(page, 'Main navigation', 'Notifications');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.getByRole('navigation', { name: 'Main navigation', exact: true }).evaluate(element => { element.scrollLeft = 0; });
  await page.setViewportSize({ width: 320, height: 844 });
  await activeNavigationFits(page, 'Main navigation', 'Notifications');
  await page.getByLabel('Interface language', { exact: true }).selectOption('uk');
  await activeNavigationFits(page, 'Головна навігація', 'Сповіщення');
});

test('synthetic Board refresh preserves focus on an untouched card while another card finishes saving', async ({ page }) => {
  await syntheticApi(page);
  await page.goto('/tasks?view=board');
  const firstStatus = page.getByRole('combobox', { name: `Статус: ${firstTask}`, exact: true });
  const untouchedStatus = page.getByRole('combobox', { name: `Статус: ${secondTask}`, exact: true });
  await expect(firstStatus).toBeEnabled();
  let releasePatch: () => void = () => {};
  let patchEntered: () => void = () => {};
  let releaseRefresh: () => void = () => {};
  let refreshEntered: () => void = () => {};
  const patchGate = new Promise<void>(resolve => { releasePatch = resolve; });
  const patchReceived = new Promise<void>(resolve => { patchEntered = resolve; });
  const refreshGate = new Promise<void>(resolve => { releaseRefresh = resolve; });
  const refreshReceived = new Promise<void>(resolve => { refreshEntered = resolve; });
  await page.route('**/api/tasks/101/state', async route => {
    if (route.request().method() !== 'PATCH') return route.fallback();
    patchEntered(); await patchGate; await route.fallback();
  });
  await page.route('**/api/tasks/page?**', async route => {
    refreshEntered(); await refreshGate; await route.fallback();
  });
  try {
    await firstStatus.focus(); await firstStatus.selectOption('Done');
    await patchReceived;
    await untouchedStatus.focus();
    await expect(untouchedStatus).toHaveValue('InProgress');
    releasePatch(); await refreshReceived;
    const board = page.getByRole('region', { name: 'Дошка завдань поточної сторінки', exact: true });
    await expect(board).toHaveAttribute('aria-busy', 'true');
    await expect(untouchedStatus).toBeFocused();
    await expect(untouchedStatus).toBeEnabled();
    releaseRefresh();
    await expect(board).toHaveAttribute('aria-busy', 'false');
    await expect(page.locator('.board-column[data-state="Done"] [data-task-id="101"]')).toBeVisible();
    await expect(untouchedStatus).toHaveValue('InProgress');
    await expect(untouchedStatus).toBeFocused();
    await expect(page.locator('.task-save-feedback')).toHaveText(`Статус «${firstTask}» змінено: Готово.`);
  } finally { releasePatch(); releaseRefresh(); }
});

test('synthetic mobile navigation keeps its active item visible when a delayed unread badge appears', async ({ page }) => {
  await syntheticApi(page);
  const inbox = await syntheticInboxApi(page, { count: 1024, holdLoad: true });
  await page.setViewportSize({ width: 390, height: 844 });
  try {
    await page.goto('/notifications');
    await expect.poll(() => inbox.captured.some(call => call.method === 'GET')).toBe(true);
    await activeNavigationFits(page, 'Головна навігація', 'Сповіщення');
    const navigation = page.getByRole('navigation', { name: 'Головна навігація', exact: true });
    const active = navigation.locator('a[aria-current="page"]');
    await expect(active.locator('.notification-count')).toHaveCount(0);
    inbox.releaseLoad();
    await expect(active.locator('.notification-count')).toHaveText('1024');
    await expect(active).toHaveAccessibleName(/Сповіщення\s*1024/);
    await expect.poll(() => active.evaluate(element => {
      const menu = element.closest('nav')!; const bounds = menu.getBoundingClientRect(); const item = element.getBoundingClientRect();
      const badge = element.querySelector('.notification-count')!.getBoundingClientRect();
      return { itemVisible: item.left >= bounds.left - 1 && item.right <= bounds.right + 1,
        badgeVisible: badge.left >= bounds.left - 1 && badge.right <= bounds.right + 1 };
    })).toEqual({ itemVisible: true, badgeVisible: true });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
  } finally { inbox.releaseLoad(); }
});

test('synthetic mobile advanced filters open by keyboard and retain drafts across collapse language and desktop resize', async ({ page }) => {
  await syntheticApi(page);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/tasks?view=board');
  const board = page.getByRole('region', { name: 'Дошка завдань поточної сторінки', exact: true });
  const toggle = filters(page).locator('button[aria-controls][aria-expanded]');
  await expect(board).toBeVisible();
  await expect(toggle).toHaveAccessibleName('Показати фільтри');
  await expect(toggle).toHaveAttribute('aria-expanded', 'false');
  const controlledId = await toggle.getAttribute('aria-controls');
  expect(controlledId).toBeTruthy();
  const advanced = page.locator(`[id="${controlledId}"]`);
  await expect(advanced).toHaveClass(/filter-advanced/);
  await expect(advanced).toBeHidden();
  for (const name of ['Мої', 'Прострочені']) await expect(filters(page).getByRole('checkbox', { name, exact: true })).toBeVisible();
  for (const name of ['Показати', 'Дошка', 'Список']) await expect(filters(page).getByRole('button', { name, exact: true })).toBeVisible();
  const closedTop = await board.evaluate(element => element.getBoundingClientRect().top + window.scrollY);
  await toggle.focus(); await page.keyboard.press('Enter');
  await expect(toggle).toHaveAttribute('aria-expanded', 'true');
  await expect(projectSelect(page)).toBeVisible();
  const openedTop = await board.evaluate(element => element.getBoundingClientRect().top + window.scrollY);
  expect(openedTop, 'Collapsed advanced fields must leave more vertical space for the Board.').toBeGreaterThan(closedTop + 40);

  await page.getByLabel('Пошук', { exact: true }).fill('Моя незастосована чернетка');
  await projectSelect(page).selectOption('71');
  await assigneeSelect(page).selectOption('43');
  await filters(page).getByRole('combobox', { name: /^Статус/ }).selectOption('InProgress');
  await filters(page).getByRole('combobox', { name: /^Сортування/ }).selectOption('created');
  await toggle.focus(); await page.keyboard.press('Space');
  await expect(toggle).toHaveAttribute('aria-expanded', 'false');
  await expect(advanced).toBeHidden();
  await expect(filters(page).getByRole('button', { name: 'Очистити фільтри', exact: true })).toBeVisible();
  await page.getByLabel('Мова інтерфейсу', { exact: true }).selectOption('en');
  await expect(toggle).toHaveAccessibleName('Show filters');
  await expect(page.getByLabel('Search', { exact: true })).toHaveValue('Моя незастосована чернетка');
  await toggle.focus(); await page.keyboard.press('Enter');
  await expect(toggle).toHaveAccessibleName('Hide filters');
  await expect(filters(page).getByRole('combobox', { name: /^Project/ })).toHaveValue('71');
  await expect(filters(page).getByRole('combobox', { name: /^Assignee/ })).toHaveValue('43');
  await expect(filters(page).getByRole('combobox', { name: /^Status/ })).toHaveValue('InProgress');
  await expect(filters(page).getByRole('combobox', { name: /^Sort/ })).toHaveValue('created');
  await toggle.focus(); await page.keyboard.press('Space');
  await expect(advanced).toBeHidden();
  await page.setViewportSize({ width: 1280, height: 900 });
  await expect(toggle).toBeHidden();
  await expect(advanced).toBeVisible();
  await expect(filters(page).getByRole('combobox', { name: /^Assignee/ })).toHaveValue('43');
  await expect(filters(page).getByRole('combobox', { name: /^Status/ })).toHaveValue('InProgress');
  await expect(filters(page).getByRole('combobox', { name: /^Sort/ })).toHaveValue('created');
  await expect(page.getByLabel('Search', { exact: true })).toHaveValue('Моя незастосована чернетка');
  await queryIs(page, { view: 'board', search: null, projectId: null, performerId: null, state: null, sort: null });
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(toggle).toHaveAttribute('aria-expanded', 'false');
  await expect(advanced).toBeHidden();
  await expect(page.getByLabel('Search', { exact: true })).toHaveValue('Моя незастосована чернетка');
});
