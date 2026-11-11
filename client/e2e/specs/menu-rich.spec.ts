// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { cell, gotoGrid } from '../support/grid';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for the rich data-driven menu (the `menu-rich` story): the
 * menu button, submenus by keyboard and by hover on both reading
 * directions, touch rows on a phone-width viewport, checkable and
 * keep-open items, entries disabled with a reason, links whose modifier
 * clicks stay the browser's, the three menu-row glyphs, and the axe floor
 * in both themes and both directions.
 */

/** Layout tolerance: sub-pixel rounding of anchored overlays. */
const TOLERANCE = 1;

/** The computed transform of a glyph mirrored for right-to-left. */
const MIRRORED = 'matrix(-1, 0, 0, 1, 0, 0)';

function trigger(page: Page): Locator {
  return page.getByTestId('menu-rich-trigger');
}

/** The menu's own panel (not a submenu's). */
function menuPanel(page: Page): Locator {
  return page.locator('.tm-menu__panel:not(.tm-menu__panel--sub)');
}

/** The submenu panel currently shown; closed ones stay in the DOM, not displayed. */
function submenuPanel(page: Page): Locator {
  return page.locator('.tm-menu__panel--sub:not(.tm-menu__panel--closed)');
}

/** The row whose label is exactly `label`, on any level. */
function row(page: Page, label: string): Locator {
  const exact = new RegExp(`^\\s*${label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\s*$`);
  return page.locator('.tm-menu__item', {
    has: page.locator('.tm-menu__label', { hasText: exact }),
  });
}

/** The label of the row that holds DOM focus. */
function focusedLabel(page: Page): Locator {
  return page.locator('.tm-menu__item:focus .tm-menu__label');
}

/** Opens the menu from its button with a click and waits for the panel. */
async function openMenu(page: Page): Promise<void> {
  await trigger(page).click();
  await expect(menuPanel(page)).toBeVisible();
  await expect(focusedLabel(page)).toHaveText('Language');
}

/** Flips the document direction the way an app's language switch does. */
async function flipDocumentTo(page: Page, dir: 'ltr' | 'rtl'): Promise<void> {
  await page.evaluate((value) => {
    document.documentElement.dir = value;
  }, dir);
  await expect(page.locator('html')).toHaveAttribute('dir', dir);
}

/** A box that is known to exist. */
async function boxOf(
  locator: Locator,
): Promise<{ x: number; y: number; width: number; height: number }> {
  const box = await locator.boundingBox();
  if (box === null) {
    throw new Error('element has no box');
  }
  return box;
}

test.describe('axe floor (menu and submenu open)', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`the rich menu with a submenu open is axe-clean (${theme}, ${dir})`, async ({
        page,
      }) => {
        await page.goto(storyUrl('menu-rich', { theme, dir }));
        await openMenu(page);
        await page.keyboard.press(dir === 'ltr' ? 'ArrowRight' : 'ArrowLeft');
        await expect(submenuPanel(page)).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});

test.describe('layout in both directions', () => {
  // 'rtl-live' starts left-to-right and flips <html dir> at run time, so it
  // passes only through the live direction, not a direction read at load.
  for (const mode of ['ltr', 'rtl', 'rtl-live'] as const) {
    const rtl = mode !== 'ltr';

    test(`the menu opens under its button and the submenu at the inline end (${mode})`, async ({
      page,
    }) => {
      await page.goto(storyUrl('menu-rich', mode === 'rtl' ? { dir: 'rtl' } : {}));
      if (mode === 'rtl-live') {
        await flipDocumentTo(page, 'rtl');
      }
      await openMenu(page);

      // Below the button, start edges aligned.
      const button = await boxOf(trigger(page));
      const panel = await boxOf(menuPanel(page));
      expect(Math.abs(panel.y - (button.y + button.height))).toBeLessThanOrEqual(TOLERANCE);
      const startGap = rtl ? panel.x + panel.width - (button.x + button.width) : panel.x - button.x;
      expect(Math.abs(startGap)).toBeLessThanOrEqual(TOLERANCE);

      // The expand key follows the direction; the other arrow does nothing.
      await page.keyboard.press(rtl ? 'ArrowRight' : 'ArrowLeft');
      await expect(submenuPanel(page)).toHaveCount(0);
      await page.keyboard.press(rtl ? 'ArrowLeft' : 'ArrowRight');
      await expect(submenuPanel(page)).toBeVisible();
      await expect(focusedLabel(page)).toHaveText('English');

      // The submenu sits at the parent row's inline end, first rows level.
      const parent = await boxOf(row(page, 'Language'));
      const sub = await boxOf(submenuPanel(page));
      const sideGap = rtl ? sub.x + sub.width - parent.x : sub.x - (parent.x + parent.width);
      expect(Math.abs(sideGap)).toBeLessThanOrEqual(TOLERANCE);
      const first = await boxOf(row(page, 'English'));
      expect(Math.abs(first.y - parent.y)).toBeLessThanOrEqual(TOLERANCE);

      // The collapse key is the opposite arrow.
      await page.keyboard.press(rtl ? 'ArrowRight' : 'ArrowLeft');
      await expect(submenuPanel(page)).toHaveCount(0);
      await expect(focusedLabel(page)).toHaveText('Language');
    });

    test(`the menu-row glyphs keep their box and stroke; only the chevron mirrors (${mode})`, async ({
      page,
    }) => {
      await page.goto(storyUrl('menu-rich', mode === 'rtl' ? { dir: 'rtl' } : {}));
      if (mode === 'rtl-live') {
        await flipDocumentTo(page, 'rtl');
      }
      await openMenu(page);

      const glyphs = {
        check: row(page, 'Compact').locator('.tm-menu__icon tm-icon'),
        external: row(page, 'Documentation').locator('tm-icon.tm-menu__external'),
        chevron: row(page, 'Language').locator('tm-icon.tm-menu__chevron'),
      };
      for (const [name, glyph] of Object.entries(glyphs)) {
        const box = await boxOf(glyph);
        expect(box.width, `${name} box width`).toBe(15);
        expect(box.height, `${name} box height`).toBe(15);
        const svg = glyph.locator(':scope > svg');
        expect(
          await svg.evaluate((element) => getComputedStyle(element).strokeWidth),
          `${name} stroke`,
        ).toBe('1.75px');
        expect(
          await svg.evaluate((element) => getComputedStyle(element).transform),
          `${name} transform`,
        ).toBe(name === 'chevron' && rtl ? MIRRORED : 'none');
      }
    });
  }
});

test.describe('touch rows below the sm breakpoint', () => {
  /** The heights of the displayed rows whose label is one of `labels`. */
  async function heights(page: Page, labels: readonly string[]): Promise<number[]> {
    const result: number[] = [];
    for (const label of labels) {
      result.push((await boxOf(row(page, label))).height);
    }
    return result;
  }

  const ONE_LINE = ['Language', 'Compact', 'Line numbers', 'My settings', 'Sign out'];
  const TWO_LINE = ['Close period', 'Export', 'About'];

  test('rows are 44 px tall at 390 px, type sizes unchanged', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(storyUrl('menu-rich'));
    await openMenu(page);

    expect(await heights(page, ONE_LINE)).toEqual(ONE_LINE.map(() => 44));
    // A row with a second line is at least a touch target too.
    for (const height of await heights(page, TWO_LINE)) {
      expect(height).toBeGreaterThanOrEqual(44);
    }
    await expect(row(page, 'Language')).toHaveCSS('font-size', '12.5px');

    // A submenu's rows follow, and the submenu is pushed back on screen.
    await row(page, 'Language').click();
    await expect(submenuPanel(page)).toBeVisible();
    expect((await boxOf(row(page, 'English'))).height).toBe(44);
    const sub = await boxOf(submenuPanel(page));
    expect(sub.x).toBeGreaterThanOrEqual(0);
    expect(sub.x + sub.width).toBeLessThanOrEqual(390);
  });

  test('rows keep the dense 30 px from 576 px up', async ({ page }) => {
    await page.setViewportSize({ width: 768, height: 1024 });
    await page.goto(storyUrl('menu-rich'));
    await openMenu(page);

    expect(await heights(page, ONE_LINE)).toEqual(ONE_LINE.map(() => 30));
    for (const height of await heights(page, TWO_LINE)) {
      expect(height).toBeGreaterThan(30);
    }
    await expect(row(page, 'Language')).toHaveCSS('font-size', '12.5px');
  });

  test("the grid's context menu follows: 44 px rows at 390 px", async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await gotoGrid(page, 'grid-editable');
    await cell(page, 0, 1).click({ button: 'right' });
    await expect(menuPanel(page)).toBeVisible();

    const rows = menuPanel(page).locator('.tm-menu__item');
    const count = await rows.count();
    expect(count).toBeGreaterThan(3);
    for (let index = 0; index < count; index++) {
      expect((await boxOf(rows.nth(index))).height).toBe(44);
    }
  });
});

test.describe('the menu button', () => {
  test('carries the menu-button ARIA and toggles on click', async ({ page }) => {
    await page.goto(storyUrl('menu-rich'));
    const button = trigger(page);
    await expect(button).toHaveAttribute('aria-haspopup', 'menu');
    await expect(button).toHaveAttribute('aria-expanded', 'false');
    await expect(button).not.toHaveAttribute('aria-controls', /.*/);

    await openMenu(page);
    await expect(button).toHaveAttribute('aria-expanded', 'true');
    const controls = await button.getAttribute('aria-controls');
    await expect(page.locator(`[id="${controls}"]`)).toHaveAttribute('role', 'menu');

    await button.click();
    await expect(menuPanel(page)).toHaveCount(0);
    await expect(button).toHaveAttribute('aria-expanded', 'false');
    await expect(button).toBeFocused();
  });

  test('Enter and Space open on the first row; ArrowDown too; ArrowUp on the last', async ({
    page,
  }) => {
    await page.goto(storyUrl('menu-rich'));
    const button = trigger(page);

    for (const key of ['Enter', 'Space', 'ArrowDown']) {
      await button.focus();
      await page.keyboard.press(key);
      await expect(focusedLabel(page), `after ${key}`).toHaveText('Language');
      await page.keyboard.press('Escape');
      await expect(menuPanel(page)).toHaveCount(0);
      await expect(button).toBeFocused();
    }

    await page.keyboard.press('ArrowUp');
    await expect(focusedLabel(page)).toHaveText('Sign out');
  });

  test('the header block describes the menu, and a detail joins its row name', async ({ page }) => {
    await page.goto(storyUrl('menu-rich'));
    await openMenu(page);

    await expect(page.getByTestId('menu-rich-header')).toBeVisible();
    await expect(page.getByRole('menu', { name: 'Session' })).toHaveAccessibleDescription(
      /Rana Haddad\s*rana@example\.com/,
    );
    await expect(
      page.getByRole('menuitem', { name: 'Language, English', exact: true }),
    ).toBeVisible();
    await expect(page.getByRole('menuitem', { name: 'About, v2.14.0', exact: true })).toBeVisible();
    // A shortcut hint stays out of the name.
    await expect(page.getByRole('menuitem', { name: 'Sign out', exact: true })).toBeVisible();
  });
});

test.describe('submenus by keyboard', () => {
  test('opens with ArrowRight, Enter and Space; closes one level per Escape', async ({ page }) => {
    await page.goto(storyUrl('menu-rich'));
    await openMenu(page);

    for (const key of ['ArrowRight', 'Enter', 'Space']) {
      await page.keyboard.press(key);
      await expect(submenuPanel(page), `after ${key}`).toBeVisible();
      await expect(focusedLabel(page)).toHaveText('English');
      await expect(row(page, 'Language')).toHaveAttribute('aria-expanded', 'true');

      await page.keyboard.press('Escape');
      await expect(submenuPanel(page)).toHaveCount(0);
      await expect(menuPanel(page)).toBeVisible(); // one level only
      await expect(focusedLabel(page)).toHaveText('Language');
    }

    await page.keyboard.press('Escape');
    await expect(menuPanel(page)).toHaveCount(0);
    await expect(trigger(page)).toBeFocused();
  });

  test('choosing inside a submenu closes the whole menu and returns focus', async ({ page }) => {
    await page.goto(storyUrl('menu-rich'));
    await openMenu(page);
    await page.keyboard.press('ArrowRight');
    await expect(focusedLabel(page)).toHaveText('English');
    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('ArrowDown');
    await expect(focusedLabel(page)).toHaveText('Français');
    await expect(row(page, 'Français')).toHaveAttribute('lang', 'fr');

    await page.keyboard.press('Enter');

    await expect(menuPanel(page)).toHaveCount(0);
    await expect(page.getByTestId('menu-rich-language')).toHaveText('fr');
    await expect(page.getByTestId('menu-rich-selected')).toHaveText('1');
    await expect(trigger(page)).toBeFocused();

    // The parent row's detail and the radio state follow the choice.
    await openMenu(page);
    await expect(row(page, 'Language').locator('.tm-menu__detail')).toHaveText('Français');
    await page.keyboard.press('ArrowRight');
    await expect(row(page, 'Français')).toHaveAttribute('aria-checked', 'true');
    await expect(row(page, 'English')).toHaveAttribute('aria-checked', 'false');
  });

  test('typeahead matches labels, across rows of every kind', async ({ page }) => {
    await page.goto(storyUrl('menu-rich'));
    await openMenu(page);

    await page.keyboard.type('s');
    await expect(focusedLabel(page)).toHaveText('Sign out');
    // "v2.14.0" is About's detail, not a label: nothing moves.
    await page.waitForTimeout(600); // the typeahead buffer clears after 500 ms
    await page.keyboard.type('v');
    await expect(focusedLabel(page)).toHaveText('Sign out');
  });
});

test.describe('submenus by hover', () => {
  test('hover opens without taking focus; a click inside chooses; moving on closes', async ({
    page,
  }) => {
    await page.goto(storyUrl('menu-rich'));
    await openMenu(page);

    await row(page, 'Language').hover();
    await expect(submenuPanel(page)).toBeVisible();
    // Opened by the pointer: focus is not moved into the submenu.
    await expect(submenuPanel(page).locator(':focus')).toHaveCount(0);

    // Moving on to another row of the menu closes it.
    await row(page, 'Line numbers').hover();
    await expect(submenuPanel(page)).toHaveCount(0);

    // Back, and into the submenu: it stays open and takes the click.
    await row(page, 'Language').hover();
    await expect(submenuPanel(page)).toBeVisible();
    await row(page, 'العربية').hover();
    await expect(submenuPanel(page)).toBeVisible();
    await row(page, 'العربية').click();

    await expect(menuPanel(page)).toHaveCount(0);
    await expect(page.getByTestId('menu-rich-language')).toHaveText('ar');
  });

  test('a parent row disabled with a reason shows the reason and never opens', async ({ page }) => {
    await page.goto(storyUrl('menu-rich'));
    await openMenu(page);

    const parent = row(page, 'Export');
    await expect(parent).toHaveAttribute('aria-disabled', 'true');
    await expect(parent).toHaveAccessibleDescription('There is nothing to export yet');
    await parent.hover({ force: true });
    await parent.click({ force: true });
    await page.keyboard.press('ArrowRight');
    await page.waitForTimeout(300); // past the 100 ms hover-open delay
    await expect(submenuPanel(page)).toHaveCount(0);
    await expect(menuPanel(page)).toBeVisible();
  });
});

test.describe('checkable items and reasons', () => {
  test('a keep-open checkbox toggles in place, by pointer and by keyboard', async ({ page }) => {
    await page.goto(storyUrl('menu-rich'));
    await openMenu(page);
    const item = page.getByRole('menuitemcheckbox', { name: 'Line numbers', exact: true });
    await expect(item).toHaveAttribute('aria-checked', 'false');

    await item.click();
    await expect(page.getByTestId('menu-rich-line-numbers')).toHaveText('true');
    await expect(item).toHaveAttribute('aria-checked', 'true');
    await expect(item.locator('.tm-menu__icon tm-icon svg')).toBeVisible();
    await expect(menuPanel(page)).toBeVisible();
    await expect(item).toBeFocused();

    await page.keyboard.press('Enter');
    await expect(page.getByTestId('menu-rich-line-numbers')).toHaveText('false');
    await page.keyboard.press('Space');
    await expect(page.getByTestId('menu-rich-line-numbers')).toHaveText('true');
    await expect(menuPanel(page)).toBeVisible();
    await expect(page.getByTestId('menu-rich-selected')).toHaveText('3');
  });

  test('radio items in a labelled group move the check', async ({ page }) => {
    await page.goto(storyUrl('menu-rich'));
    await openMenu(page);

    const group = page.getByRole('group', { name: 'Density' });
    await expect(group.getByRole('menuitemradio')).toHaveCount(2);
    await group.getByRole('menuitemradio', { name: 'Comfortable' }).click();

    await expect(page.getByTestId('menu-rich-density')).toHaveText('comfortable');
    await expect(group.getByRole('menuitemradio', { name: 'Comfortable' })).toHaveAttribute(
      'aria-checked',
      'true',
    );
    await expect(group.getByRole('menuitemradio', { name: 'Compact' })).toHaveAttribute(
      'aria-checked',
      'false',
    );
    await expect(menuPanel(page)).toBeVisible();
  });

  test('an item disabled with a reason is focusable, described, and inert', async ({ page }) => {
    await page.goto(storyUrl('menu-rich'));
    await openMenu(page);
    const item = row(page, 'Close period');

    await expect(item).toHaveAttribute('aria-disabled', 'true');
    await expect(item).toHaveAccessibleDescription('Only an administrator can close a period');
    await expect(item.locator('.tm-menu__description')).toBeVisible();

    await page.keyboard.type('cl'); // typeahead still reaches it
    await expect(item).toBeFocused();
    await page.keyboard.press('Enter');
    await page.keyboard.press('Space');
    await item.click({ force: true });

    await expect(page.getByTestId('menu-rich-last')).toHaveText('none');
    await expect(menuPanel(page)).toBeVisible();
  });
});

test.describe('links', () => {
  test('a plain click runs the action in place; a link with none is followed', async ({ page }) => {
    await page.goto(storyUrl('menu-rich'));
    await openMenu(page);
    const settings = page.getByRole('menuitem', { name: 'My settings', exact: true });
    await expect(settings).toHaveJSProperty('tagName', 'A');
    await expect(settings).toHaveAttribute('href', '/story/menu-rich#my-settings');

    await settings.click();
    await expect(menuPanel(page)).toHaveCount(0);
    await expect(page.getByTestId('menu-rich-last')).toHaveText('settings');
    expect(new URL(page.url()).hash).toBe(''); // the action ran instead of the navigation
    await expect(page.getByTestId('menu-rich-selected')).toHaveText('0'); // links never report

    await openMenu(page);
    await row(page, 'Jump to the note').click();
    await expect(menuPanel(page)).toHaveCount(0);
    await expect(page).toHaveURL(/#menu-rich-anchor$/);
    await expect(page.getByTestId('menu-rich-last')).toHaveText('settings'); // unchanged
  });

  test('Enter activates a link the same way', async ({ page }) => {
    await page.goto(storyUrl('menu-rich'));
    await openMenu(page);

    await page.keyboard.type('m'); // My settings
    await expect(focusedLabel(page)).toHaveText('My settings');
    await page.keyboard.press('Enter');
    await expect(menuPanel(page)).toHaveCount(0);
    await expect(page.getByTestId('menu-rich-last')).toHaveText('settings');
    expect(new URL(page.url()).hash).toBe('');

    await openMenu(page);
    await page.keyboard.type('j'); // Jump to the note
    await expect(focusedLabel(page)).toHaveText('Jump to the note');
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/#menu-rich-anchor$/);
    await expect(menuPanel(page)).toHaveCount(0);
  });

  for (const gesture of ['Control+click', 'Shift+click', 'middle-click'] as const) {
    test(`a ${gesture} stays the browser's: a new page, no action, menu left open`, async ({
      page,
      context,
    }) => {
      await page.goto(storyUrl('menu-rich'));
      await openMenu(page);
      const settings = row(page, 'My settings');

      const opened = context.waitForEvent('page');
      if (gesture === 'middle-click') {
        await settings.click({ button: 'middle' });
      } else {
        await settings.click({
          modifiers: [gesture === 'Control+click' ? 'ControlOrMeta' : 'Shift'],
        });
      }
      const other = await opened;

      await expect(other).toHaveURL(/#my-settings$/);
      await expect(page.getByTestId('menu-rich-last')).toHaveText('none');
      await expect(menuPanel(page)).toBeVisible();
      expect(new URL(page.url()).hash).toBe('');
      await other.close();
    });
  }

  test('a new-tab link opens its target with no opener, and says so', async ({ page, context }) => {
    await context.route('https://example.com/**', (route) =>
      route.fulfill({ status: 200, contentType: 'text/html', body: '<title>Docs</title>' }),
    );
    await page.goto(storyUrl('menu-rich'));
    await openMenu(page);

    const docs = page.getByRole('menuitem', {
      name: 'Documentation (opens in a new tab)',
      exact: true,
    });
    await expect(docs).toHaveAttribute('target', '_blank');
    await expect(docs).toHaveAttribute('rel', 'noopener');

    const opened = context.waitForEvent('page');
    await docs.click();
    const other = await opened;
    await expect(other).toHaveURL('https://example.com/docs');
    // rel="noopener": the new page holds no reference back to this one.
    expect(await other.evaluate(() => window.opener === null)).toBe(true);
    await expect(menuPanel(page)).toHaveCount(0);
    await other.close();
  });

  test('the native context menu is kept on a link and suppressed on other rows', async ({
    page,
  }) => {
    await page.goto(storyUrl('menu-rich'));
    await openMenu(page);

    /** Whether the menu cancels a `contextmenu` event on the row. */
    const suppressed = (target: Locator) =>
      target.evaluate((element) => {
        const event = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });
        element.dispatchEvent(event);
        return event.defaultPrevented;
      });

    expect(await suppressed(row(page, 'My settings'))).toBe(false);
    expect(await suppressed(row(page, 'Sign out'))).toBe(true);
  });
});
