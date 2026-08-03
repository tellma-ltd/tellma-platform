// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * The tm-entity-picker form-path battery over the entity-picker story: the
 * search lifecycle (sync fast path, async spinner/coalescing, failure,
 * hasMore), the keyboard matrix with the pristine-browse rules, blur/Enter
 * resolution, the three modal pages, the aria-in-overlay real-mouse guard,
 * axe/RTL/forced-colors/reduced-motion gates, and the live locale switch.
 *
 * Story fixtures: 'Adam Brown' ×2 (ambiguous), 'Alice Green'/'Alan Grey'
 * (shared 'Al' prefix), browse capped at 5 with `hasMore`. The field picker
 * sits in a native form whose submit count proves Enter-consumption.
 */

/** The field-wrapped picker's input. */
function input(page: Page, testid = 'picker-field'): Locator {
  return page.getByTestId(testid).locator('.tm-entity-picker__input');
}

/** The open dropdown panel (top-layer). */
function panel(page: Page): Locator {
  return page.locator('.tm-entity-picker__panel');
}

/** The entity result rows (footer rows excluded). */
function options(page: Page): Locator {
  return page.locator('.tm-entity-picker__option:not(.tm-entity-picker__action)');
}

/** The command footer rows. */
function actions(page: Page): Locator {
  return page.locator('.tm-entity-picker__action');
}

/** The highlighted option, if any. */
function activeOption(page: Page): Locator {
  return page.locator('.tm-entity-picker__option[data-active="true"]');
}

/** The dropdown's status row (spinner / no results / failed). */
function status(page: Page): Locator {
  return page.locator('.tm-entity-picker__status');
}

/** The field's error element. */
function fieldError(page: Page): Locator {
  return page.getByTestId('ff').locator('.tm-form-field__error');
}

/** The story's committed-model oracle. */
async function model(page: Page): Promise<{ supplierId: number | null; populatedId: number | null }> {
  const text = await page.getByTestId('model-json').textContent();
  return JSON.parse(text ?? '{}') as { supplierId: number | null; populatedId: number | null };
}

/** Switches the story's search to the synchronous fast path. */
async function useSyncSearch(page: Page): Promise<void> {
  await page.getByTestId('search-async').uncheck();
}

async function setSearchDelay(page: Page, ms: number): Promise<void> {
  await page.getByTestId('search-delay').fill(String(ms));
}

async function searchCalls(page: Page): Promise<number> {
  return Number(await page.getByTestId('search-calls').textContent());
}

test.beforeEach(async ({ page }) => {
  await page.goto(storyUrl('entity-picker'));
  await expect(input(page)).toBeVisible();
});

test.describe('search lifecycle', () => {
  test('sync source renders keystroke-instant with no spinner; async shows the immediate spinner', async ({
    page,
  }) => {
    await useSyncSearch(page);
    await input(page).fill('Al');
    await expect(options(page)).toHaveText(['Alice Green', 'Alan Grey']);
    await expect(status(page)).toHaveCount(0); // no spinner ever appeared

    // Async: the spinner shows at once and holds until the fresh set lands.
    await page.getByTestId('search-async').check();
    await setSearchDelay(page, 400);
    await input(page).fill('Alice');
    await expect(status(page).locator('tm-spinner')).toBeVisible();
    await expect(options(page)).toHaveCount(0); // stale rows cleared immediately
    await expect(options(page)).toHaveText(['Alice Green']);
    await expect(status(page)).toHaveCount(0);
  });

  test('a held key costs ONE trailing request, however long it repeats', async ({ page }) => {
    // The window slides: every keystroke inside it pushes the deadline out,
    // so a sustained burst costs the leading request plus one trailing one
    // — not one per window, which is what a fixed window would charge.
    // The window is widened here only so the assertion cannot be decided
    // by CDP round-trip jitter; the semantics under test are the default's.
    await setSearchDelay(page, 200);
    await page.getByTestId('search-debounce').fill('300');
    const before = await searchCalls(page);
    await input(page).pressSequentially('aaaaaaaaaaaaaaaaaaaa', { delay: 33 });
    await page.waitForTimeout(600);
    expect((await searchCalls(page)) - before).toBe(2);
  });

  test('normal typing pays no added latency — every settled keystroke searches', async ({
    page,
  }) => {
    // The window only absorbs bursts: at human typing speed each keystroke
    // is its own leading edge and fires immediately.
    await setSearchDelay(page, 100);
    const before = await searchCalls(page);
    await input(page).pressSequentially('Ali', { delay: 120 });
    await expect(options(page)).toHaveText(['Alice Green']);
    expect((await searchCalls(page)) - before).toBe(3);
  });

  test('a failed search shows the status row with footer rows intact; the next change retries', async ({
    page,
  }) => {
    await useSyncSearch(page);
    await page.getByTestId('toggle-fail').check();
    await input(page).fill('Alice');
    await expect(status(page)).toHaveText('Search failed');
    await expect(actions(page).first()).toBeVisible(); // recovery stays reachable
    await input(page).fill('Alice Gr');
    await expect(options(page)).toHaveText(['Alice Green']);
  });

  test('a truncated browse renders the hasMore hint above the footer rows', async ({ page }) => {
    await useSyncSearch(page);
    await input(page).click();
    await expect(options(page)).toHaveCount(5); // capped
    const hint = page.locator('.tm-entity-picker__hint');
    await expect(hint).toHaveText('Showing top 5 matches. Keep typing to refine.');
    await expect(hint).toHaveAttribute('aria-hidden', 'true');
    // Visually distinct from the command rows below it: smaller and muted,
    // so nothing about it reads as clickable.
    const [hintSize, actionSize] = await Promise.all([
      hint.evaluate((el) => parseFloat(getComputedStyle(el).fontSize)),
      actions(page).first().evaluate((el) => parseFloat(getComputedStyle(el).fontSize)),
    ]);
    expect(hintSize).toBeLessThan(actionSize);
  });

  test('the portaled ARIA id chain resolves: combobox → listbox → active option', async ({
    page,
  }) => {
    await useSyncSearch(page);
    await input(page).fill('Al');
    await expect(options(page)).toHaveCount(2);
    const controls = await input(page).getAttribute('aria-controls');
    expect(controls).toBeTruthy();
    await expect(page.locator(`[id="${controls}"]`)).toHaveRole('listbox');
    await expect(input(page)).toHaveAttribute('aria-autocomplete', 'list');
    const active = await input(page).getAttribute('aria-activedescendant');
    expect(active).toBeTruthy();
    await expect(page.locator(`[id="${active}"]`)).toHaveRole('option');
  });
});

test.describe('mouse interaction (angular/components#32504 guard, real events)', () => {
  test('clicking an option commits it and closes the panel', async ({ page }) => {
    await useSyncSearch(page);
    await input(page).fill('Al');
    await options(page).filter({ hasText: 'Alan Grey' }).click();
    await expect(panel(page)).toHaveCount(0);
    await expect(input(page)).toHaveValue('Alan Grey');
    expect((await model(page)).supplierId).toBe(4);
    await expect(input(page)).toBeFocused(); // the press never blurred the input
  });

  test('clicking outside closes a pristine browse panel without changing anything', async ({
    page,
  }) => {
    await useSyncSearch(page);
    await input(page, 'picker-populated').click();
    await expect(panel(page)).toBeVisible();
    // Derive the outside point from the panel's own box — a hard-coded
    // coordinate silently starts landing INSIDE the panel when layout moves.
    const box = (await panel(page).boundingBox())!;
    await page.mouse.click(box.x + box.width + 60, Math.max(8, box.y - 40));
    await expect(panel(page)).toHaveCount(0);
    expect((await model(page)).populatedId).toBe(5);
    await expect(input(page, 'picker-populated')).toHaveValue('Bob Stone');
  });

  test('the magnifier opens the advanced-search page with the typed text preserved', async ({
    page,
  }) => {
    await useSyncSearch(page);
    await input(page).fill('Ali');
    await page.getByTestId('picker-field').locator('.tm-entity-picker__magnifier').click();
    await expect(page.getByTestId('page-query')).toHaveText('Ali');
    await expect(panel(page)).toHaveCount(0); // the dropdown yielded to the modal
    await page.getByTestId('adv-cancel').click();
    await expect(input(page)).toHaveValue('Ali'); // dismissal is a strict no-op
    await expect(input(page)).toBeFocused();
  });
});

test.describe('keyboard matrix', () => {
  test('type → first result auto-highlights → Enter commits; Enter never submits while open', async ({
    page,
  }) => {
    await useSyncSearch(page);
    await input(page).fill('Al');
    await expect(activeOption(page)).toHaveText('Alice Green');
    await input(page).press('Enter');
    await expect(panel(page)).toHaveCount(0);
    await expect(input(page)).toHaveValue('Alice Green');
    expect((await model(page)).supplierId).toBe(3);
    await expect(page.getByTestId('submit-count')).toHaveText('0'); // consumed while open
    await input(page).press('Enter'); // closed: the native form submit applies
    await expect(page.getByTestId('submit-count')).toHaveText('1');
  });

  test('a pristine browse highlights nothing; open-then-Tab changes nothing', async ({ page }) => {
    await useSyncSearch(page);
    const populated = input(page, 'picker-populated');
    await populated.click();
    await expect(options(page).first()).toBeVisible();
    await expect(activeOption(page)).toHaveCount(0);
    // The committed row still mirrors as selected.
    await expect(page.locator('[aria-selected="true"]')).toHaveText(/Bob Stone/);
    await populated.press('Tab');
    await expect(panel(page)).toHaveCount(0);
    expect((await model(page)).populatedId).toBe(5);
    await expect(populated).toHaveValue('Bob Stone');
  });

  test('Space types into the query instead of activating the highlight', async ({ page }) => {
    await useSyncSearch(page);
    await input(page).fill('Adam');
    await expect(activeOption(page)).toHaveCount(1);
    await input(page).press('Space');
    await expect(panel(page)).toBeVisible();
    await expect(input(page)).toHaveValue('Adam ');
    expect((await model(page)).supplierId).toBeNull();
  });

  test('arrows walk the results into the footer rows; Esc closes with the text intact', async ({
    page,
  }) => {
    await useSyncSearch(page);
    await input(page).fill('Alice Gr');
    await expect(activeOption(page)).toHaveText('Alice Green');
    await input(page).press('ArrowDown'); // past the last result…
    await expect(activeOption(page)).toHaveText('Advanced search…');
    await input(page).press('ArrowDown');
    await expect(activeOption(page)).toHaveText('Create supplier…');
    await input(page).press('Escape');
    await expect(panel(page)).toHaveCount(0);
    await expect(input(page)).toHaveValue('Alice Gr');
  });

  test('arrowing past the fold scrolls the highlighted row into view, both directions', async ({
    page,
  }) => {
    // DOM focus never leaves the input, so nothing scrolls the list on its
    // own — the highlight would otherwise walk out of sight.
    await useSyncSearch(page);
    const long = input(page, 'picker-long');
    const listbox = page.locator('.tm-entity-picker__listbox');
    /** Whether the highlighted row lies inside the scroll window. */
    const activeIsInView = async (): Promise<boolean> => {
      const list = (await listbox.boundingBox())!;
      const active = (await activeOption(page).boundingBox())!;
      return active.y >= list.y - 1 && active.y + active.height <= list.y + list.height + 1;
    };

    await long.click();
    await expect(options(page).first()).toBeVisible();
    const scrollTop = (): Promise<number> => listbox.evaluate((el) => el.scrollTop);
    expect(await scrollTop()).toBe(0);

    // Walk down past the visible window: the list follows the highlight.
    // Each step waits for the highlight it just asked for — a burst of
    // un-awaited presses measures how fast the runner is, not whether the
    // list follows. The browse list highlights nothing, so the FIRST press
    // lands on row 1 rather than advancing from it.
    for (let i = 1; i <= 12; i++) {
      await long.press('ArrowDown');
      await expect(activeOption(page)).toHaveText(`Supplier ${String(i).padStart(2, '0')}`);
    }
    const scrolled = await scrollTop();
    expect(scrolled).toBeGreaterThan(0);
    expect(await activeIsInView()).toBe(true);

    // …and back up again.
    for (let i = 11; i >= 1; i--) {
      await long.press('ArrowUp');
      await expect(activeOption(page)).toHaveText(`Supplier ${String(i).padStart(2, '0')}`);
    }
    expect(await scrollTop()).toBeLessThan(scrolled);
    expect(await activeIsInView()).toBe(true);
  });

  test('Tab commits the highlighted result and focus proceeds to the next control', async ({
    page,
  }) => {
    await useSyncSearch(page);
    const before = Number(await page.getByTestId('picked-count').textContent());
    await input(page).fill('Alice Gr');
    await expect(activeOption(page)).toHaveText('Alice Green');
    await input(page).press('Tab');
    await expect(panel(page)).toHaveCount(0);
    expect((await model(page)).supplierId).toBe(3);
    await expect(page.getByTestId('submit')).toBeFocused();
    // ONE commit gesture, one `picked`. The blur that Tab triggers arrives
    // before the native input has been repainted with the committed label,
    // so a departure that resolved what it read there would re-pick the same
    // row and report the edit twice.
    expect(Number(await page.getByTestId('picked-count').textContent()) - before).toBe(1);
  });

  test('Tab commits out of a MULTI-row set — the blur behind it resolves nothing', async ({
    page,
  }) => {
    // The row is committed while the field still reads the query that found
    // it, and that query names two suppliers. Nothing about the departure
    // may re-open the question the Tab just answered.
    await useSyncSearch(page);
    await input(page).fill('Adam');
    await expect(options(page)).toHaveCount(2);
    await expect(activeOption(page)).toHaveText('Adam Brown');
    await input(page).press('Tab');
    await expect(panel(page)).toHaveCount(0);
    await expect(input(page)).toHaveValue('Adam Brown');
    expect((await model(page)).supplierId).toBe(1);
    await expect(fieldError(page)).toHaveText('');
    await expect(input(page)).not.toHaveAttribute('aria-invalid', 'true');
  });

  test('an arrowed row Tab-commits the row the user chose, not the first one', async ({ page }) => {
    await useSyncSearch(page);
    await input(page).fill('Adam');
    await expect(activeOption(page)).toHaveText('Adam Brown');
    // Both rows read 'Adam Brown', so the highlight has to be tracked by
    // identity: waiting on the TEXT would let Tab race the arrow.
    const first = await input(page).getAttribute('aria-activedescendant');
    await input(page).press('ArrowDown'); // the SECOND 'Adam Brown', id 2
    await expect(input(page)).not.toHaveAttribute('aria-activedescendant', first ?? '');
    await input(page).press('Tab');
    await expect(input(page)).toHaveValue('Adam Brown');
    expect((await model(page)).supplierId).toBe(2);
  });

  test('clearing the text stops the field claiming a selection it no longer shows', async ({
    page,
  }) => {
    // Emptying the box does not write null until the commit gesture, so the
    // model still holds the id — but nothing on screen may go on saying
    // "this is your supplier" while the box reads empty.
    await useSyncSearch(page);
    await input(page).fill('Alice Gr');
    await input(page).press('Enter');
    await expect(input(page)).toHaveValue('Alice Green');

    await input(page).click();
    await expect(actions(page)).toHaveText([
      'Advanced search…',
      'Create supplier…',
      'Edit…',
    ]);
    await expect(page.locator('.tm-entity-picker__option[aria-selected="true"]')).toHaveCount(1);

    await input(page).fill('');
    await expect(panel(page)).toBeVisible();
    await expect(actions(page)).toHaveText(['Advanced search…', 'Create supplier…']);
    await expect(page.locator('.tm-entity-picker__option[aria-selected="true"]')).toHaveCount(0);

    // …and typing the label back brings both affordances back.
    await input(page).fill('Alice Green');
    await expect(actions(page)).toHaveText([
      'Advanced search…',
      'Create supplier…',
      'Edit…',
    ]);
  });

  test('Enter on a fresh empty typed set fails fast with the popup open for recovery', async ({
    page,
  }) => {
    await useSyncSearch(page);
    await input(page).fill('Zebra');
    await expect(status(page)).toHaveText('No results');
    await expect(activeOption(page)).toHaveCount(0);
    await input(page).press('Enter');
    await expect(panel(page)).toBeVisible();
    await expect(fieldError(page)).toContainText('No match for');
    await expect(page.getByTestId('submit-count')).toHaveText('0');
    expect((await model(page)).supplierId).toBeNull();
  });
});

test.describe('blur resolution', () => {
  test('a unique match auto-picks even when the response lands after the departure', async ({
    page,
  }) => {
    await setSearchDelay(page, 500);
    await input(page).fill('Alice Gr');
    await page.getByTestId('submit').focus(); // real departure mid-flight
    await expect(panel(page)).toHaveCount(0);
    await expect(input(page)).toHaveValue('Alice Green');
    expect((await model(page)).supplierId).toBe(3);
    await expect(fieldError(page)).toHaveText(''); // no error flash
    await expect(
      page.getByTestId('picker-field').locator('.tm-entity-picker__live'),
    ).toHaveText('Alice Green selected');
  });

  test('a picker with no bound field still shows its unresolved text as an error', async ({
    page,
  }) => {
    // The 'no magnifier' picker is field-wrapped but NOT [formField]-bound:
    // its own resolution errors are the only error channel there is, so the
    // control must surface them itself — text that names no entity may never
    // sit there looking like a valid selection.
    await useSyncSearch(page);
    const plain = input(page, 'picker-plain');
    const field = page.locator('tm-form-field', { has: page.getByTestId('picker-plain') });
    await plain.fill('Adam');
    // A departure that carries no commit gesture of its own — Tab would
    // commit the highlighted row and there would be nothing left to resolve.
    await page.getByTestId('submit').focus();
    await expect(plain).toHaveValue('Adam'); // kept for correction
    await expect(field.locator('.tm-form-field__error')).toContainText('matches more than one item');
    await expect(field).toHaveClass(/tm-form-field--invalid/);
    await expect(plain).toHaveAttribute('aria-invalid', 'true');
  });

  test('a query in progress paints no error; the departure does, and resuming clears it', async ({
    page,
  }) => {
    await useSyncSearch(page);
    const long = input(page, 'picker-long');
    const field = page.locator('tm-form-field', { has: page.getByTestId('picker-long') });
    const error = field.locator('.tm-form-field__error');

    await long.fill('Supp'); // half-typed: a query, not a mistake
    await expect(error).toBeEmpty();
    await expect(long).not.toHaveAttribute('aria-invalid', 'true');
    await expect(field).not.toHaveClass(/tm-form-field--invalid/);

    await page.getByTestId('submit').focus(); // a real departure on unresolvable text reports
    await expect(error).toContainText('matches more than one item');
    await expect(field).toHaveClass(/tm-form-field--invalid/);

    await long.click(); // …and resuming the edit clears it again
    await long.fill('Supplier 03');
    await expect(error).toBeEmpty();
    await expect(field).not.toHaveClass(/tm-form-field--invalid/);
  });

  test('ambiguous and no-match departures keep the text with the localized error', async ({
    page,
  }) => {
    await useSyncSearch(page);
    await input(page).fill('Adam Brown');
    await page.getByTestId('submit').focus();
    await expect(fieldError(page)).toContainText('matches more than one item');
    await expect(input(page)).toHaveValue('Adam Brown');
    expect((await model(page)).supplierId).toBeNull();

    await input(page).fill('Zebra');
    await page.getByTestId('submit').focus();
    await expect(fieldError(page)).toContainText('No match for');
    await expect(input(page)).toHaveValue('Zebra');
  });
});

test.describe('panel placement', () => {
  test('a bottom-anchored picker opens UPWARD from the first paint — no downward flash', async ({
    page,
  }) => {
    // The side is chosen from the anchor's room, before the panel exists —
    // so the very first painted position is the final one. (Choosing it
    // from the panel instead means measuring an empty panel, which always
    // "fits below" and then gets yanked up a frame later.)
    await setSearchDelay(page, 600); // the panel is still a spinner when it attaches
    const flip = input(page, 'picker-flip');
    const anchor = (await flip.boundingBox())!;
    await flip.click();
    await expect(panel(page)).toBeVisible();

    const whileLoading = (await panel(page).boundingBox())!;
    expect(whileLoading.y + whileLoading.height).toBeLessThanOrEqual(anchor.y + 2);

    // …and it stays above once the results replace the spinner.
    await expect(options(page).first()).toBeVisible();
    const withResults = (await panel(page).boundingBox())!;
    expect(withResults.y + withResults.height).toBeLessThanOrEqual(anchor.y + 2);
  });

  test('a growing result set never re-orients the panel or overflows the viewport', async ({
    page,
  }) => {
    await setSearchDelay(page, 400);
    const flip = input(page, 'picker-flip');
    await flip.click();
    await expect(panel(page)).toBeVisible();
    const loadingTop = (await panel(page).boundingBox())!.y;

    // The full directory is the longest set this picker can show.
    await expect(options(page).first()).toBeVisible();
    const grown = (await panel(page).boundingBox())!;
    const viewport = page.viewportSize()!;
    // Same side (the panel grew upward from the same anchored edge, so its
    // top moved UP, never across the field), and fully on screen.
    expect(grown.y).toBeLessThanOrEqual(loadingTop + 1);
    expect(grown.y).toBeGreaterThanOrEqual(0);
    expect(grown.y + grown.height).toBeLessThanOrEqual(viewport.height + 1);
    // The list scrolls inside the clamp instead of running off the top.
    const listbox = page.locator('.tm-entity-picker__listbox');
    const maxHeight = await listbox.evaluate((el) => parseFloat(getComputedStyle(el).maxBlockSize));
    expect(maxHeight).toBeGreaterThan(0);
    expect(maxHeight).toBeLessThanOrEqual(viewport.height);
  });

  test('a picker with room below still opens downward', async ({ page }) => {
    await useSyncSearch(page);
    const anchor = (await input(page).boundingBox())!;
    await input(page).click();
    await expect(panel(page)).toBeVisible();
    const box = (await panel(page).boundingBox())!;
    expect(box.y).toBeGreaterThanOrEqual(anchor.y + anchor.height - 2);
  });
});

test.describe('size stability', () => {
  test('the field box keeps its size across loading, resolving, resolved, and error states', async ({
    page,
  }) => {
    await setSearchDelay(page, 800);
    const box = page.getByTestId('ff').locator('.tm-form-field__box');
    const before = (await box.boundingBox())!;

    await input(page).fill('Alice Gr'); // loading — dropdown-only churn
    const loading = (await box.boundingBox())!;

    await page.getByTestId('submit').focus(); // resolving — the spinner slot fills
    await expect(page.getByTestId('ff').locator('.tm-form-field__spinner')).toBeVisible();
    const resolving = (await box.boundingBox())!;

    await expect(input(page)).toHaveValue('Alice Green'); // the pick lands
    const resolved = (await box.boundingBox())!;

    await input(page).fill('Zebra'); // kept-error state
    await page.getByTestId('submit').focus();
    await expect(fieldError(page)).toContainText('No match for');
    const errored = (await box.boundingBox())!;

    for (const rect of [loading, resolving, resolved, errored]) {
      expect(rect.width).toBe(before.width);
      expect(rect.height).toBe(before.height);
    }
  });
});

test.describe('modal round-trips', () => {
  test('an advanced-search pick applies, focuses the input, and closes the loop', async ({
    page,
  }) => {
    await useSyncSearch(page);
    await page.getByTestId('picker-field').locator('.tm-entity-picker__magnifier').click();
    await page.getByTestId('adv-pick-4').click();
    await expect(input(page)).toHaveValue('Alan Grey');
    expect((await model(page)).supplierId).toBe(4);
    await expect(input(page)).toBeFocused();
  });

  test('the create page prefills the query; its pick applies with the fresh label', async ({
    page,
  }) => {
    await useSyncSearch(page);
    await input(page).fill('Newco');
    await expect(status(page)).toHaveText('No results');
    // Each arrow waits for the row it asked for: a burst measures how fast
    // the runner is, not where the highlight goes.
    await input(page).press('ArrowDown');
    await expect(activeOption(page)).toHaveText('Advanced search…');
    await input(page).press('ArrowDown');
    await expect(activeOption(page)).toHaveText('Create supplier…');
    await input(page).press('Enter');
    await expect(page.getByTestId('create-name')).toHaveValue('Newco');
    await page.getByTestId('create-save').click();
    await expect(input(page)).toHaveValue('Newco');
    expect((await model(page)).supplierId).toBeGreaterThanOrEqual(100);
  });

  test('the edit page targets the committed id; rename re-applies, delete clears', async ({
    page,
  }) => {
    await useSyncSearch(page);
    const populated = input(page, 'picker-populated');
    await populated.click();
    // ArrowUp with nothing highlighted wraps straight to the LAST option.
    await populated.press('ArrowUp');
    await expect(activeOption(page)).toHaveText('Edit…');
    await populated.press('Enter');
    await expect(page.getByTestId('edit-id')).toHaveText('5');
    await page.getByTestId('edit-name').fill('Bob S. (renamed)');
    await page.getByTestId('edit-save').click();
    await expect(populated).toHaveValue('Bob S. (renamed)');
    expect((await model(page)).populatedId).toBe(5); // same id, fresh label

    // Round 2: the page deletes the entity — close(null) clears the field.
    await populated.click();
    await populated.press('ArrowUp');
    await expect(activeOption(page)).toHaveText('Edit…');
    await populated.press('Enter');
    await page.getByTestId('edit-delete').click();
    await expect(populated).toHaveValue('');
    expect((await model(page)).populatedId).toBeNull();
  });

  test('a picker inside a modal stacks its pages and Esc dismisses innermost-first', async ({
    page,
  }) => {
    await useSyncSearch(page);
    await page.getByTestId('open-host-modal').click();
    const inner = page.getByTestId('picker-in-modal').locator('.tm-entity-picker__input');
    await inner.fill('Al');
    await expect(options(page)).toHaveCount(2);
    // Esc №1: the dropdown; the host modal stays.
    await inner.press('Escape');
    await expect(panel(page)).toHaveCount(0);
    await expect(inner).toBeVisible();
    // The picker's own page stacks ABOVE the host modal and closes first.
    await page.getByTestId('picker-in-modal').locator('.tm-entity-picker__magnifier').click();
    await expect(page.getByTestId('adv-filter')).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.getByTestId('adv-filter')).toHaveCount(0);
    await expect(inner).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(inner).toHaveCount(0);
  });
});

test.describe('RTL & locale', () => {
  test('under dir=rtl the magnifier sits at the inline end and the panel mirrors', async ({
    page,
  }) => {
    await page.goto(storyUrl('entity-picker', { dir: 'rtl' }));
    await useSyncSearch(page);
    const field = input(page);
    const magnifier = page.getByTestId('picker-field').locator('.tm-entity-picker__magnifier');
    const inputBox = (await field.boundingBox())!;
    const magnifierBox = (await magnifier.boundingBox())!;
    // Inline end in RTL is the PHYSICAL LEFT.
    expect(magnifierBox.x).toBeLessThan(inputBox.x);
    await field.fill('Al');
    await expect(options(page)).toHaveCount(2);
    await expect(
      page.locator('.cdk-overlay-connected-position-bounding-box'),
    ).toHaveAttribute('dir', 'rtl');
  });

  test('a live locale switch re-renders captions and a KEPT error with the model untouched', async ({
    page,
  }) => {
    await useSyncSearch(page);
    await input(page).fill('Zebra');
    await page.getByTestId('submit').focus();
    await expect(fieldError(page)).toContainText('No match for');

    await page.getByTestId('lang-ar').click();
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    // The kept error re-renders in Arabic; text and model stay put.
    await expect(fieldError(page)).toContainText('لا يوجد تطابق');
    await expect(fieldError(page)).toContainText('Zebra');
    await expect(input(page)).toHaveValue('Zebra');
    expect((await model(page)).supplierId).toBeNull();
    // The footer captions re-render too.
    await input(page, 'picker-populated').click();
    await expect(actions(page).last()).toHaveText('تعديل…');
  });
});

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`no violations across popup states (${theme})`, async ({ page }) => {
      await page.goto(storyUrl('entity-picker', { theme }));
      await useSyncSearch(page);

      // Results + footer rows.
      await input(page).fill('Al');
      await expect(options(page)).toHaveCount(2);
      await expectNoAxeViolations(page);

      // The truncated browse (hasMore hint) — aria-hidden rows must stay
      // clean inside the listbox.
      await input(page).fill('');
      await expect(options(page)).toHaveCount(5);
      await expectNoAxeViolations(page);

      // Empty set.
      await input(page).fill('Zebra');
      await expect(status(page)).toHaveText('No results');
      await expectNoAxeViolations(page);

      // Kept resolution error on the field.
      await page.getByTestId('submit').focus();
      await expect(fieldError(page)).toContainText('No match for');
      await expectNoAxeViolations(page);

      // The loading state (a long-latency async search).
      await page.getByTestId('search-async').check();
      await setSearchDelay(page, 5000);
      await input(page).fill('Alice');
      await expect(status(page).locator('tm-spinner')).toBeVisible();
      await expectNoAxeViolations(page);
    });
  }
});

test.describe('forced-colors & reduced motion', () => {
  test('the active ring, selected check, and separator survive forced colors', async ({
    page,
  }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await page.goto(storyUrl('entity-picker'));
    await useSyncSearch(page);
    await input(page, 'picker-populated').click();
    await expect(options(page).first()).toBeVisible();
    await input(page, 'picker-populated').press('ArrowDown');
    await expect(activeOption(page)).toHaveCount(1);
    const outline = await activeOption(page).evaluate(
      (el) => getComputedStyle(el).outlineStyle,
    );
    expect(outline).toBe('solid');
    const separator = await page
      .locator('.tm-entity-picker__separator')
      .evaluate((el) => getComputedStyle(el).borderBlockStartStyle);
    expect(separator).toBe('solid');
    // The committed row's check glyph is visible AND carries the row's
    // honored HighlightText (its own brand color would be forced to
    // CanvasText and could vanish against the Highlight fill).
    const check = page.locator('[aria-selected="true"] .tm-entity-picker__check');
    await expect(check).toBeVisible();
    const [checkColor, rowColor] = await Promise.all([
      check.evaluate((el) => getComputedStyle(el).color),
      page
        .locator('[aria-selected="true"]')
        .evaluate((el) => getComputedStyle(el).color),
    ]);
    expect(checkColor).toBe(rowColor);
  });

  test('reduced motion: the picker declares no animation or transition at all', async ({
    page,
  }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.goto(storyUrl('entity-picker'));
    await useSyncSearch(page);
    await input(page).fill('Al');
    await expect(options(page).first()).toBeVisible();
    // Absence is the assertion (the date-picker gate's pattern): if motion
    // is ever added without a reduced-motion collapse, this goes red — the
    // probes cover every interactive surface, hover chrome included.
    const probes = [
      input(page),
      page.getByTestId('picker-field').locator('.tm-entity-picker__magnifier'),
      panel(page),
      options(page).first(),
    ];
    for (const locator of probes) {
      const motion = await locator.evaluate(
        (el) => `${getComputedStyle(el).animationName}|${getComputedStyle(el).transitionDuration}`,
      );
      expect(motion).toBe('none|0s');
    }
  });
});
