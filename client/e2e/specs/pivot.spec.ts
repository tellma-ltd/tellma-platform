// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { useExclusiveClipboard } from '../support/clipboard-lock';
import { appStable } from '../support/settle';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-pivot: the axe floor in both themes and
 * directions; the five looks and their rules as computed in light, dark
 * and right-to-left; the keyboard, activation and the refusal; the real
 * clipboard; the sticky set at host widths; and the large cube's text
 * calls, which only rendered cells make.
 */

/** The styles story's pivot. */
function pivot(page: Page): Locator {
  return page.getByTestId('pivot');
}

/** A body row's header by its label. */
function rowHeader(page: Page, label: string): Locator {
  return pivot(page).getByRole('rowheader', { name: label, exact: true });
}

/** The value cell of a body row at a data column (from 0). */
function valueCell(page: Page, label: string, dataColumn: number): Locator {
  // The row header and the code attribute come first. A `has` locator is
  // matched inside each row, so it starts from the page, not the pivot.
  return pivot(page)
    .getByRole('row')
    .filter({ has: page.getByRole('rowheader', { name: label, exact: true }) })
    .locator(`[role="gridcell"][aria-colindex="${3 + dataColumn}"]`);
}

/** Opens a story and waits for its first settled render. */
async function open(page: Page, id: string, options: Parameters<typeof storyUrl>[1] = {}): Promise<void> {
  await page.goto(storyUrl(id, options));
  await expect(page.getByRole('treegrid').first()).toBeVisible();
  await appStable(page);
}

/** What the looks are made of, as computed. */
interface Look {
  readonly background: string;
  readonly color: string;
  readonly weight: string;
  readonly fontStyle: string;
}

/** A cell's computed look. */
function lookOf(cell: Locator): Promise<Look> {
  return cell.evaluate((element) => {
    const style = getComputedStyle(element);
    const label = element.querySelector('.tm-pivot__label');
    return {
      background: style.backgroundColor,
      color: style.color,
      weight: style.fontWeight,
      fontStyle: label === null ? style.fontStyle : getComputedStyle(label).fontStyle,
    };
  });
}

/** A token resolved to a computed colour inside the pivot, where the theme applies. */
function tokenColor(page: Page, token: string): Promise<string> {
  return pivot(page).evaluate((host, name) => {
    const probe = document.createElement('span');
    probe.style.color = `var(${name})`;
    host.appendChild(probe);
    const color = getComputedStyle(probe).color;
    probe.remove();
    return color;
  }, token);
}

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`the pivot story is axe-clean, collapsed and expanded (${theme}, ${dir})`, async ({ page }) => {
        await open(page, 'pivot', { theme, dir });
        await expectNoAxeViolations(page);
        await page.getByTestId('pivot-expand-all').click();
        await expect(rowHeader(page, 'Olaya (direct)')).toBeVisible();
        await expectNoAxeViolations(page);
        await page.getByTestId('pivot-collapse-all').click();
        await expect(rowHeader(page, 'Olaya')).toHaveCount(0);
        await expectNoAxeViolations(page);
      });
    }
  }
});

test.describe('the looks', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`each look draws from its tokens, the total ruled (${theme}, ${dir})`, async ({ page }) => {
        await open(page, 'pivot', { theme, dir });
        await page.getByTestId('pivot-expand-all').click();
        await appStable(page);
        const [sunken, card, strong, body, secondary, rule] = await Promise.all(
          ['--surface-sunken', '--surface-card', '--text-strong', '--text-body', '--text-secondary', '--border-strong'].map(
            (token) => tokenColor(page, token),
          ),
        );
        // The rows: total, subtotal-1, subtotal-2, phantom and leaf.
        expect(await lookOf(rowHeader(page, 'Total'))).toMatchObject({ background: sunken, color: strong, weight: '600' });
        expect(await lookOf(rowHeader(page, 'Riyadh'))).toMatchObject({ background: sunken, color: strong, weight: '600' });
        expect(await lookOf(rowHeader(page, 'Olaya'))).toMatchObject({ background: card, weight: '600' });
        expect(await lookOf(rowHeader(page, 'Olaya (direct)'))).toMatchObject({
          background: card,
          color: secondary,
          weight: '400',
          fontStyle: 'italic',
        });
        expect(await lookOf(rowHeader(page, 'Malaz'))).toMatchObject({ background: card, color: body, weight: '400' });
        // A value cell takes the stronger look: a leaf row in the Q1 total column.
        expect(await lookOf(valueCell(page, 'Malaz', 0))).toMatchObject({ weight: '400', background: card });
        expect(await lookOf(valueCell(page, 'Malaz', 6))).toMatchObject({ weight: '600', background: sunken });
        // The grand total's 2px rules: on its row's block-start edge, and on
        // its column's inline-start edge (the right edge in RTL).
        const rowRule = await valueCell(page, 'Total', 0).evaluate((element) => {
          const style = getComputedStyle(element, '::before');
          return { height: style.height, top: style.top, background: style.backgroundColor };
        });
        expect(rowRule).toEqual({ height: '2px', top: '0px', background: rule });
        // Data columns: Jan, Feb, Mar, Q1 total, Q2's months, Q2 total, then
        // Total, two measures each.
        const totalColumn = valueCell(page, 'Malaz', 2 * 8);
        const columnRule = await totalColumn.evaluate((element) => {
          const style = getComputedStyle(element, '::after');
          return { width: style.width, left: style.left, right: style.right };
        });
        expect(columnRule.width).toBe('2px');
        expect(dir === 'ltr' ? columnRule.left : columnRule.right).toBe('0px');
        // Figures read left to right and align physically right in either direction.
        const cell = valueCell(page, 'Malaz', 0);
        const [box, text] = await Promise.all([cell.boundingBox(), cell.locator('[dir="ltr"]').boundingBox()]);
        expect(box!.x + box!.width - (text!.x + text!.width)).toBeLessThan(12);
        // The row header sits on the reading side.
        const [host, header] = await Promise.all([pivot(page).boundingBox(), rowHeader(page, 'Riyadh').boundingBox()]);
        if (dir === 'ltr') {
          expect(Math.abs(header!.x - host!.x)).toBeLessThan(2);
        } else {
          expect(Math.abs(host!.x + host!.width - (header!.x + header!.width))).toBeLessThan(20);
        }
      });
    }
  }

  test('a value cell that drills shows the hover look; a refused one does not', async ({ page }) => {
    await open(page, 'pivot');
    const [hover, link, card] = await Promise.all([
      tokenColor(page, '--pivot-hover-bg'),
      tokenColor(page, '--pivot-link-text'),
      tokenColor(page, '--surface-card'),
    ]);
    // Malaz × Jan drills: both members are leaves.
    await valueCell(page, 'Malaz', 0).hover();
    expect(await lookOf(valueCell(page, 'Malaz', 0))).toMatchObject({ background: hover, color: link });
    expect(await valueCell(page, 'Malaz', 0).evaluate((element) => getComputedStyle(element).textDecorationLine)).toBe(
      'underline',
    );
    // Olaya × Jan does not: Olaya has children.
    await page.getByTestId('pivot-expand-all').click();
    await valueCell(page, 'Olaya', 0).hover();
    expect(await lookOf(valueCell(page, 'Olaya', 0))).not.toMatchObject({ color: link });
    expect(await lookOf(valueCell(page, 'Olaya', 0))).toMatchObject({ background: card });
  });
});

test.describe('the keyboard', () => {
  for (const dir of ['ltr', 'rtl'] as const) {
    test(`the arrows follow the reading direction and Enter drills (${dir})`, async ({ page }) => {
      await open(page, 'pivot', { dir });
      const next = dir === 'ltr' ? 'ArrowRight' : 'ArrowLeft';
      await rowHeader(page, 'Malaz').focus();
      await page.keyboard.press(next);
      await expect(pivot(page).getByRole('gridcell', { name: 'RUH-MZ' })).toBeFocused();
      await page.keyboard.press(next);
      await expect(valueCell(page, 'Malaz', 0)).toBeFocused();
      await page.keyboard.press('Enter');
      await expect(page.getByTestId('pivot-activation')).toHaveText('12/11/0');
      await page.keyboard.press(next);
      await page.keyboard.press('ControlOrMeta+Enter');
      await expect(page.getByTestId('pivot-activation')).toHaveText('12/11/1 new tab');
      // Up past Olaya and Riyadh, then through the measure, month and
      // quarter headers; Alt+ArrowUp collapses Q1.
      for (let step = 0; step < 5; step++) {
        await page.keyboard.press('ArrowUp');
      }
      await expect(pivot(page).getByRole('columnheader', { name: 'Q1', exact: true })).toBeFocused();
      await page.keyboard.press('Alt+ArrowUp');
      await expect(pivot(page).getByRole('columnheader', { name: 'Q1', exact: true })).toHaveAttribute(
        'aria-expanded',
        'false',
      );
      await expect(pivot(page).getByRole('columnheader', { name: 'Q1', exact: true })).toBeFocused();
      await expect(page.locator('.cdk-live-announcer-element')).toHaveText('Q1 collapsed');
    });
  }

  test('a refused cell explains itself in its tooltip and on Enter', async ({ page }) => {
    await open(page, 'pivot');
    const refusal = 'Only the finest cells open their rows in this report.';
    await rowHeader(page, 'Riyadh').focus();
    await page.keyboard.press('ArrowRight');
    await page.keyboard.press('ArrowRight');
    await expect(valueCell(page, 'Riyadh', 0)).toBeFocused();
    await expect(page.locator('.tm-tooltip__panel')).toHaveText(refusal);
    await page.keyboard.press('Enter');
    await expect(page.locator('.cdk-live-announcer-element')).toHaveText(refusal);
    await expect(page.getByTestId('pivot-activation')).toHaveText('none');
    // A twisty click expands, announcing what it reveals.
    await rowHeader(page, 'Jeddah').locator('.tm-pivot__twisty').click();
    await expect(page.locator('.cdk-live-announcer-element')).toHaveText('Jeddah expanded, 2 rows');
  });
});

test.describe('the clipboard', () => {
  test.use({ permissions: ['clipboard-read', 'clipboard-write'] });
  useExclusiveClipboard();

  test('Mod+C copies the focused cell’s full text', async ({ page, browserName }) => {
    test.skip(browserName !== 'chromium', 'The real clipboard is read through Chromium permissions.');
    await open(page, 'pivot');
    await rowHeader(page, 'Malaz').focus();
    await page.keyboard.press('ArrowRight');
    await page.keyboard.press('ArrowRight');
    const text = (await valueCell(page, 'Malaz', 0).textContent())!.trim();
    await page.keyboard.press('ControlOrMeta+C');
    await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe(text);
  });
});

test.describe('sticky columns', () => {
  for (const width of [1280, 1024, 768, 375]) {
    test(`the sticky set follows 40% of the host at a ${width}px viewport`, async ({ page }) => {
      await page.setViewportSize({ width, height: 800 });
      await open(page, 'pivot');
      const host = (await pivot(page).boundingBox())!.width;
      // The rule over one attribute: the row header's default, then the
      // attribute's 96px, while they fit in 40% of the host.
      const rowHeaderWidth = host < 300 ? 120 : Math.min(280, Math.max(120, Math.round(0.4 * host)));
      const expected = host < 300 ? 0 : rowHeaderWidth + 96 <= 0.4 * host ? 2 : 1;
      const header = rowHeader(page, 'Riyadh');
      const code = pivot(page).getByRole('gridcell', { name: 'RUH', exact: true });
      const before = [(await header.boundingBox())!.x, (await code.boundingBox())!.x];
      await pivot(page).locator('.tm-pivot__scroller').evaluate((scroller) => {
        scroller.scrollLeft = 150;
      });
      await appStable(page);
      const after = [(await header.boundingBox())!.x, (await code.boundingBox())!.x];
      const stuck = [Math.abs(after[0] - before[0]) < 1, Math.abs(after[1] - before[1]) < 1];
      expect(stuck).toEqual([expected >= 1, expected >= 2]);
      await expect(header).toHaveClass(expected >= 1 ? /tm-pivot__sticky/ : /^(?!.*tm-pivot__sticky)/);
    });
  }
});

test.describe('the large cube', () => {
  test('a 500,000-cell cube asks only for the cells it renders', async ({ page }) => {
    await open(page, 'pivot-large');
    const grid = page.getByTestId('pivot-large');
    await expect(grid.getByRole('treegrid')).toHaveAttribute('aria-rowcount', '1001');
    await expect(grid.getByRole('treegrid')).toHaveAttribute('aria-colcount', '501');
    const rendered = () => grid.locator('td.tm-pivot__value').count();
    const calls = async () => Number(await page.getByTestId('pivot-text-calls').textContent());
    await expect.poll(calls).toBe(await rendered());
    const first = await calls();
    expect(first).toBeLessThan(2000);
    // A scroll asks for the cells it brings in, and no more.
    await grid.locator('.tm-pivot__scroller').evaluate((scroller) => {
      scroller.scrollTop = 400 * 30;
      scroller.scrollLeft = 200 * 112;
    });
    await appStable(page);
    await expect(grid.getByRole('rowheader', { name: 'Account 405', exact: true })).toBeVisible();
    await expect.poll(calls).toBeGreaterThan(first);
    expect(await calls()).toBeLessThanOrEqual(first + (await rendered()));
  });

  test('@perf @nightly scrolling the large cube keeps each step inside a frame budget', async ({ page, browserName }) => {
    test.skip(browserName !== 'chromium', 'Wall-clock frame timing is measured on Chromium.');
    await open(page, 'pivot-large');
    const scroller = page.getByTestId('pivot-large').locator('.tm-pivot__scroller');
    const box = (await scroller.boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    // Section: wheel a screen at a time down, then sideways, timing each step to its next frame.
    const steps: number[] = [];
    for (const [dx, dy] of [
      [0, box.height],
      [box.width, 0],
    ]) {
      for (let step = 0; step < 30; step++) {
        const started = await page.evaluate(() => performance.now());
        await page.mouse.wheel(dx, dy);
        const ended = await page.evaluate(
          () => new Promise<number>((resolve) => requestAnimationFrame(() => requestAnimationFrame(() => resolve(performance.now())))),
        );
        steps.push(ended - started);
      }
    }
    steps.sort((a, b) => a - b);
    expect(steps[Math.floor(steps.length / 2)]).toBeLessThan(50);
    expect(steps[Math.floor(steps.length * 0.95)]).toBeLessThan(120);
  });
});
