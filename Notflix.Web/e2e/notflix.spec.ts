import type { Page } from '@playwright/test';
import { test, expect } from './fixtures';

const runtimeErrors = new WeakMap<Page, string[]>();
const consoleErrors = new WeakMap<Page, string[]>();
test.beforeEach(async ({ page }) => {
  runtimeErrors.set(page, []); consoleErrors.set(page, []);
  page.on('pageerror', error => runtimeErrors.get(page)!.push(error.message));
  page.on('console', message => { if (message.type() === 'error') consoleErrors.get(page)!.push(message.text()); });
});
test.afterEach(async ({ page }, testInfo) => {
  await testInfo.attach('browser-errors.json', {
    body: JSON.stringify({ uncaught: runtimeErrors.get(page), console: consoleErrors.get(page) }, null, 2), contentType: 'application/json',
  });
  expect(runtimeErrors.get(page)).toEqual([]);
});



async function login(page: Page, credentials: { userName: string; password: string }) {
  await page.goto('/');
  await page.getByLabel('Username', { exact: true }).fill(credentials.userName);
  await page.getByLabel('Password', { exact: true }).fill(credentials.password);
  await page.getByRole('button', { name: 'Log in', exact: true }).click();
  await expect(page).toHaveURL(/\/home$/);
  await expect(page.getByRole('heading', { name: 'Movie List' })).toBeVisible();
}

async function fillMovie(page: Page, title: string, category = 'Sci-Fi') {
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('textbox', { name: 'Title', exact: true }).fill(title);
  await dialog.getByRole('combobox', { name: 'Category', exact: true }).click();
  await page.getByRole('option', { name: category, exact: true }).click();
}

test('keyboard login, invalid credentials, and visible input focus', async ({ page, testApi }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Log in', exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'NotFlix', exact: true }).focus();
  await page.keyboard.press('Tab');
  const username = page.getByLabel('Username', { exact: true });
  await expect(username).toBeFocused();
  const focus = await username.evaluate(element => {
    const style = getComputedStyle(element);
    return { visible: element.matches(':focus-visible'), outline: style.outlineStyle, shadow: style.boxShadow };
  });
  expect(focus.visible).toBe(true);
  expect(focus.outline !== 'none' || focus.shadow !== 'none').toBe(true);
  await username.fill(testApi.userName);
  await page.keyboard.press('Tab');
  await expect(page.getByLabel('Password', { exact: true })).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(username).toBeFocused();
  await page.getByLabel('Password', { exact: true }).fill('incorrect-password');
  await page.getByRole('button', { name: 'Log in', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Invalid username or password.');
  await expect(page.getByRole('button', { name: 'Log in', exact: true })).toBeEnabled();
  await page.screenshot({ path: 'test-results/login-desktop.png', fullPage: true });
});

test('real login, add, edit, rate and reload persist to isolated SQL', async ({ page, testApi }) => {
  await login(page, testApi);
  const originalTitle = 'Browser verification movie';
  const updatedTitle = 'Browser verification movie edited';
  await page.getByRole('button', { name: 'Add Movie', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add Movie', exact: true });
  await expect(dialog.getByRole('textbox', { name: 'Title', exact: true })).toBeFocused();
  await expect(dialog.getByRole('button', { name: 'Save', exact: true })).toBeDisabled();
  await fillMovie(page, originalTitle);
  await dialog.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(dialog).toBeHidden();
  let row = page.getByRole('row').filter({ has: page.getByRole('cell', { name: originalTitle, exact: true }) });
  await expect(row).toBeVisible();
  await row.getByRole('combobox', { name: `Rate ${originalTitle}`, exact: true }).selectOption({ label: '5 / 5' });
  await expect(row.getByRole('combobox').locator('option:checked')).toHaveText('5 / 5');
  await expect(row).toContainText('5.0 / 5 (1)');
  await row.getByRole('button', { name: `Edit ${originalTitle}`, exact: true }).click();
  await fillMovie(page, updatedTitle, 'Comedy');
  await page.getByRole('dialog').getByRole('button', { name: 'Update', exact: true }).click();
  await expect(page.getByRole('dialog')).toBeHidden();
  await page.reload();
  row = page.getByRole('row').filter({ has: page.getByRole('cell', { name: updatedTitle, exact: true }) });
  await expect(row).toContainText('Comedy');
  await expect(row.getByRole('combobox').locator('option:checked')).toHaveText('5 / 5');
  await expect(row).toContainText('5.0 / 5 (1)');
  await page.screenshot({ path: 'test-results/catalogue-desktop.png', fullPage: true });
  page.once('dialog', confirmation => confirmation.accept());
  await row.getByRole('button', { name: `Delete ${updatedTitle}`, exact: true }).click();
  await expect(row).toHaveCount(0);
});

test('dialog traps keyboard focus, cancels with Space/Escape and restores focus', async ({ page, testApi }) => {
  await login(page, testApi);
  const add = page.getByRole('button', { name: 'Add Movie', exact: true });
  await add.focus(); await page.keyboard.press('Enter');
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('textbox', { name: 'Title', exact: true })).toBeFocused();
  for (let step = 0; step < 7; step++) {
    await page.keyboard.press('Tab');
    expect(await dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
  }
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden(); await expect(add).toBeFocused();
  await page.keyboard.press('Enter');
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).focus();
  await page.keyboard.press('Space');
  await expect(page.getByRole('dialog')).toBeHidden(); await expect(add).toBeFocused();
});

test('catalogue loading, empty result, server failure and retry are usable', async ({ page, testApi }) => {
  await login(page, testApi);
  let release!: () => void;
  const waiting = new Promise<void>(done => release = done);
  await page.route('**/api/movies', async route => { await waiting; await route.fulfill({ status: 200, json: [] }); });
  await page.reload();
  await expect(page.getByRole('status')).toContainText('Loading movies');
  release();
  await expect(page.getByRole('status')).toContainText('No movies found');
  await page.unroute('**/api/movies');
  await page.route('**/api/movies', route => route.fulfill({ status: 503, json: { title: 'Catalogue temporarily unavailable' } }));
  await page.reload();
  await expect(page.getByRole('alert')).toContainText('Catalogue temporarily unavailable');
  await page.unroute('**/api/movies');
  await page.getByRole('button', { name: 'Retry', exact: true }).click();
  await expect(page.getByRole('cell', { name: 'Arrival', exact: true })).toBeVisible();
});

test('dialog save failures stay actionable and an expired API session returns to login', async ({ page, testApi }) => {
  await login(page, testApi);
  await page.route('**/api/movies', async route => {
    if (route.request().method() === 'POST') await route.fulfill({ status: 500, json: { title: 'Movie temporarily unavailable' } });
    else await route.continue();
  });
  await page.getByRole('button', { name: 'Add Movie', exact: true }).click();
  await fillMovie(page, 'Failed write');
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('Movie temporarily unavailable');
  await expect(dialog.getByRole('button', { name: 'Save', exact: true })).toBeEnabled();
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  await page.unroute('**/api/movies');
  await page.route('**/api/movies', route => route.fulfill({ status: 401, json: {} }));
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Log in', exact: true })).toBeVisible();
  await expect(page.getByRole('alert')).toContainText('session expired');
});

test('mobile layout keeps page width contained and dialog controls reachable', async ({ page, testApi }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(page, testApi);
  await expect(page.getByRole('cell', { name: 'Arrival', exact: true })).toBeVisible();
  const dimensions = await page.evaluate(() => ({
    viewport: innerWidth, document: document.documentElement.scrollWidth, body: document.body.scrollWidth,
    overflow: [...document.querySelectorAll('body *')].map(element => ({
      tag: element.tagName, className: element.className?.toString(), right: element.getBoundingClientRect().right,
      localScrollParent: (() => {
        let parent = element.parentElement;
        while (parent && parent !== document.body) {
          if (['auto', 'scroll'].includes(getComputedStyle(parent).overflowX) && parent.scrollWidth > parent.clientWidth)
            return { tag: parent.tagName, className: parent.className, width: parent.clientWidth, scroll: parent.scrollWidth };
          parent = parent.parentElement;
        }
        return null;
      })(),
    })).filter(element => element.right > innerWidth + 1).slice(0, 12),
  }));
  await page.screenshot({ path: 'test-results/catalogue-mobile.png', fullPage: true });
  expect(dimensions.document <= dimensions.viewport + 1, JSON.stringify(dimensions)).toBe(true);
  await page.getByRole('button', { name: 'Add Movie', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('textbox', { name: 'Title', exact: true })).toBeFocused();
  const box = await dialog.boundingBox();
  expect(box).not.toBeNull(); expect(box!.x).toBeGreaterThanOrEqual(0); expect(box!.x + box!.width).toBeLessThanOrEqual(391);
  await expect(dialog.getByRole('button', { name: 'Cancel', exact: true })).toBeInViewport();
  await page.screenshot({ path: 'test-results/dialog-mobile.png', fullPage: true });
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
});

test('logout ends the real session and protected navigation redirects to login', async ({ page, testApi }) => {
  await login(page, testApi);
  await page.getByRole('button', { name: 'Log out', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Log in', exact: true })).toBeVisible();
  expect((await page.request.get('/api/movies')).status()).toBe(401);
  await page.goto('/home');
  await expect(page.getByRole('heading', { name: 'Log in', exact: true })).toBeVisible();
  await expect(page).toHaveURL(/returnUrl=/);
});





