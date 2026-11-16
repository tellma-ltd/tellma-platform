// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, type Locator, type Page } from '@playwright/test';

import { holdResponses, releaseResponses } from './held-responses';
import { type PaintedFeedback } from './paint-probe';
import { appStable } from './settle';

// The shared batteries of the component library additions (spec 0023): one
// entry per showcase story of its thirty entry points (§1), with the states
// §12's axe battery opens and the key §13's keydown-to-paint probe presses.
//
// Where spec 0023 §13's definition of done is pinned:
// - Placement: size:check, api:check, docs:build, the en/ar parity gate,
//   tools/tests/library-docs.spec.mts (components.json, llms.txt, MCP
//   entries and stories for every entry point), and the registry check in
//   e2e/specs/additions-axe.spec.ts.
// - Instant warm launch: size:check, skeleton.spec.ts, tiles.spec.ts
//   ("skeleton tiles hold the geometry"); spec 0024's warm-launch e2e.
// - Instant feedback: additions-keydown-paint.spec.ts (one probe per
//   story), busy-overlay.spec.ts (the overlay), search-field.spec.ts and the
//   search unit spec (timing), additions-motion.spec.ts (nothing animates
//   beyond §2.2's list).
// - Non-blocking: the search-field timing specs; pager.spec.ts ("a click
//   while a page loads writes the page again").
// - Stable layout: badge.spec.ts (counter 9 → 10), pager.spec.ts (1 → 6 of
//   120), search-field.spec.ts (first keystroke), toast.spec.ts (never
//   displaces the page), chip.spec.ts (a chip added), copy.spec.ts (copy
//   success), tree.spec.ts (lazy spinner), tiles.spec.ts (tiles data),
//   card.spec.ts (the error dot), page-header.spec.ts (end item hidden,
//   band wrapping), date-time.spec.ts (zone label across DST), icon.spec.ts
//   (glyph in the first frame), error-dialog.spec.ts (pending footer button).
// - RTL: every story spec's axe floor in both directions, and
//   additions-live-language.spec.ts (radio, segmented, splitter, tree, tiles).
// - WCAG AA: additions-axe.spec.ts and each story spec's axe floor, the
//   keyboard specs, additions-forced-colors.spec.ts,
//   additions-motion.spec.ts, additions-sizes.spec.ts and the touch
//   specs (24 px targets).
// - Page context, telemetry: core-lint-scope.spec.mts keeps @tellma/core
//   out of the library; Acme's hand-coins icon is in its icon registration.

/** One state a story is put in for the accessibility battery, beyond the state it loads in. */
export interface AdditionsState {
  /** What the state shows, for the test title. */
  readonly name: string;
  /** Prepares the page before the story loads (an init script, a stub). */
  readonly before?: (page: Page) => Promise<void>;
  /** Puts the freshly loaded, ready story into the state and waits until it shows. */
  readonly enter: (page: Page) => Promise<void>;
}

/** A key the keydown-to-paint probe presses, and the feedback it must paint. */
export interface AdditionsProbe {
  /** What the key does, for the test title. */
  readonly name: string;
  /** Prepares the page before the story loads (an init script, a stub). */
  readonly before?: (page: Page) => Promise<void>;
  /** Focuses the control the key goes to, once the story is ready. */
  readonly focus: (page: Page) => Promise<void>;
  /** The key, in Playwright's notation. */
  readonly key: string;
  /** The key event the control acts on: `keyup` for Space on a native control. */
  readonly trigger?: 'keydown' | 'keyup';
  /** The feedback the key must paint, checked on the DOM in the first frame after the key. */
  readonly painted: PaintedFeedback;
}

/** One showcase story of the library additions. */
export interface AdditionsStory {
  /** The story id, as in `/story/<id>`. */
  readonly id: string;
  /** The entry points (`@tellma/core-ui/<id>`) the story shows. */
  readonly entryPoints: readonly string[];
  /** Waits until the freshly loaded story has rendered. */
  readonly ready: (page: Page) => Promise<void>;
  /** The states the accessibility battery scans beyond the loaded one. */
  readonly states: readonly AdditionsState[];
  /** The keydown-to-paint probe, or why the story has no key to probe. */
  readonly probe: AdditionsProbe | { readonly none: string };
}

/**
 * The thirty entry points the additions bring, by id (`@tellma/core-ui/<id>`),
 * each with its showcase story of the same id.
 */
export const ADDITIONS_ENTRY_POINTS: readonly string[] = [
  'icon',
  'kbd',
  'copy',
  'badge',
  'avatar',
  'chip',
  'progress',
  'skeleton',
  'relative-time',
  'toast',
  'banner',
  'empty-state',
  'busy-overlay',
  'confirm',
  'error-dialog',
  'radio',
  'segmented',
  'switch',
  'search-field',
  'date-range',
  'date-time',
  'card',
  'page-header',
  'description-list',
  'splitter',
  'pager',
  'tree',
  'drag-drop',
  'reorder-list',
  'tiles',
];

/** The library's polite and assertive announcements. */
function liveRegion(page: Page): Locator {
  return page.locator('.cdk-live-announcer-element');
}

/** Replaces the async clipboard with one that resolves at once, so no test touches the OS clipboard. */
async function stubClipboard(page: Page): Promise<void> {
  await page.addInitScript(() => {
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: {
        writeText: () => Promise.resolve(),
        write: () => Promise.resolve(),
      },
    });
  });
}

/** A tile of the flat tiles story by its customer id. */
function tile(page: Page, id: number, testId = 'tiles'): Locator {
  return page.getByTestId(testId).locator(`.tm-tiles__item[data-index="${id - 1}"]`);
}

/** A tile of the tree-mode tiles story by its account name. */
function account(page: Page, name: string): Locator {
  return page
    .getByTestId('tiles-tree')
    .locator('.tm-tiles__item')
    .filter({
      has: page.locator('.tm-tiles__title', { hasText: new RegExp(`^\\s*${name}\\s*$`) }),
    });
}

/** A node of a tree story by its name. */
function treeNode(page: Page, name: string, testId = 'tree-fields'): Locator {
  return page.getByTestId(testId).getByRole('treeitem', { name, exact: true });
}

/** A node of the fields tree by its key, whatever its name reads while it loads. */
function treeNodeByKey(page: Page, key: string): Locator {
  return page.getByTestId('tree-fields').locator(`[role="treeitem"][data-key="${key}"]`);
}

/** A handle of the reorder story's first list by its row's label. */
function reorderHandle(page: Page, label: string): Locator {
  return page.getByTestId('reorder-columns').getByRole('button', { name: `Reorder ${label}` });
}

/** Opens the date picker of a date input from the keyboard and waits for its calendar. */
async function openDatePicker(page: Page, input: Locator): Promise<void> {
  await input.focus();
  await input.press('Alt+ArrowDown');
  await expect(page.getByRole('dialog').getByRole('grid')).toBeVisible();
}

/** Every story of the additions, in the order the showcase lists them. */
export const ADDITIONS_STORIES: readonly AdditionsStory[] = [
  {
    id: 'icon',
    entryPoints: ['icon'],
    ready: (page) => expect(page.getByTestId('gallery')).toBeVisible(),
    states: [],
    probe: {
      name: 'Enter on a button inserts rows that each carry a glyph',
      focus: (page) => page.getByTestId('insert-icons').focus(),
      key: 'Enter',
      painted: { selector: '[data-testid="inserted"] tm-icon svg' },
    },
  },
  {
    id: 'kbd',
    entryPoints: ['kbd'],
    ready: (page) => expect(page.getByTestId('bindings')).toBeVisible(),
    states: [],
    probe: {
      name: 'a shortcut typed into the tester is matched',
      focus: (page) => page.getByTestId('key-tester').focus(),
      key: 'ControlOrMeta+KeyK',
      painted: { selector: '[data-testid="matched"]', text: 'Mod+K' },
    },
  },
  {
    id: 'copy',
    entryPoints: ['copy'],
    ready: (page) => expect(page.getByTestId('reference')).toBeVisible(),
    states: [
      {
        name: 'the icon button hovered, its tooltip shown',
        enter: async (page) => {
          await page.getByTestId('copy-icon').getByRole('button').hover();
          await expect(page.locator('.tm-tooltip__panel')).toBeVisible();
        },
      },
      {
        name: 'a labelled button showing "Copied"',
        before: stubClipboard,
        enter: async (page) => {
          await page.getByTestId('copy-label').getByRole('button').click();
          await expect(page.getByTestId('copy-label')).toHaveClass(/tm-copy-button--copied/);
        },
      },
    ],
    probe: {
      // The clipboard is stubbed to answer at once: the probe then pins the
      // component's own path from the key to the "Copied" label.
      name: 'Enter copies and the label reads "Copied"',
      before: stubClipboard,
      focus: (page) => page.getByTestId('copy-label').getByRole('button').focus(),
      key: 'Enter',
      painted: { selector: '[data-testid="copy-label"]', className: 'tm-copy-button--copied' },
    },
  },
  {
    id: 'badge',
    entryPoints: ['badge'],
    ready: (page) => expect(page.getByTestId('counter-inline')).toBeVisible(),
    states: [
      {
        name: 'the inline counter stepped to two digits',
        enter: async (page) => {
          await page.getByTestId('increment').click();
          await expect(page.getByTestId('counter-inline')).toContainText('10');
        },
      },
    ],
    probe: {
      name: 'Enter on "Add one" steps the inline counter from 9 to 10',
      focus: (page) => page.getByTestId('increment').focus(),
      key: 'Enter',
      painted: { selector: '[data-testid="counter-inline"]', text: '10' },
    },
  },
  {
    id: 'avatar',
    entryPoints: ['avatar'],
    ready: (page) => expect(page.getByTestId('tone-8')).toBeVisible(),
    states: [],
    probe: { none: 'Avatars are images: the story has no control to press a key on.' },
  },
  {
    id: 'progress',
    entryPoints: ['progress'],
    ready: (page) => expect(page.getByTestId('bar-40')).toBeVisible(),
    states: [
      {
        name: 'the held bar running',
        enter: async (page) => {
          await page.getByTestId('toggle-active').click();
          await expect(page.getByTestId('toggle-active')).toHaveText('Finish');
        },
      },
    ],
    probe: {
      name: 'Enter on Start runs the held bar',
      focus: (page) => page.getByTestId('toggle-active').focus(),
      key: 'Enter',
      painted: { selector: '[data-testid="toggle-active"]', text: 'Finish' },
    },
  },
  {
    id: 'skeleton',
    entryPoints: ['skeleton'],
    ready: (page) => expect(page.getByTestId('table')).toBeVisible(),
    states: [],
    probe: { none: 'Skeletons are static placeholders: the story has no control.' },
  },
  {
    id: 'relative-time',
    entryPoints: ['relative-time'],
    ready: (page) => expect(page.getByTestId('long')).toBeVisible(),
    states: [],
    probe: { none: 'Relative times change on a timer, never on a key: the story has no control.' },
  },
  {
    id: 'empty-state',
    entryPoints: ['empty-state'],
    ready: (page) => expect(page.getByTestId('page')).toBeVisible(),
    states: [
      {
        name: 'no matches replacing the rows',
        enter: async (page) => {
          await page.getByTestId('toggle-search').click();
          await expect(page.getByTestId('no-matches')).toBeVisible();
        },
      },
    ],
    probe: {
      name: 'Enter swaps the rows for the no-matches state',
      focus: (page) => page.getByTestId('toggle-search').focus(),
      key: 'Enter',
      painted: { selector: '[data-testid="no-matches"]' },
    },
  },
  {
    id: 'chip',
    entryPoints: ['chip'],
    ready: (page) =>
      expect(page.getByTestId('set-collapse').locator('.tm-chip-set__more-button')).toBeVisible(),
    states: [
      {
        name: 'the "+N" chip focused, its list in a tooltip',
        enter: async (page) => {
          const set = page.getByTestId('set-collapse');
          await set.locator('button.tm-chip__label').first().focus();
          await page.keyboard.press('End');
          await expect(set.locator('.tm-chip-set__more-button')).toBeFocused();
          await expect(page.locator('.tm-tooltip__panel')).toContainText('more:');
        },
      },
    ],
    probe: {
      name: 'Delete removes the focused chip',
      focus: (page) => page.getByTestId('set-wrap').locator('tm-chip button').first().focus(),
      key: 'Delete',
      painted: { selector: '[data-testid="set-wrap"] tm-chip', count: 5 },
    },
  },
  {
    id: 'toast',
    entryPoints: ['toast'],
    ready: (page) => expect(page.getByTestId('show-neutral')).toBeVisible(),
    states: [
      {
        name: 'five toasts, three on show above the overflow pill',
        enter: async (page) => {
          await page.getByTestId('show-five').click();
          await expect(page.locator('.tm-toast-host .tm-toast')).toHaveCount(3);
        },
      },
      {
        name: 'a toast focused through focus()',
        enter: async (page) => {
          await page.getByTestId('show-error').click();
          await page.getByTestId('focus-toasts').click();
          await expect(page.getByTestId('toast-focused')).toHaveText('focus(): true');
        },
      },
      {
        name: 'a toast shown above an open modal',
        enter: async (page) => {
          await page.getByTestId('open-modal').click();
          await expect(page.getByTestId('modal-body')).toBeVisible();
          await expect(page.locator('.tm-toast-host .tm-toast')).toHaveCount(1);
        },
      },
    ],
    probe: {
      name: 'Enter on a show button shows the toast',
      focus: (page) => page.getByTestId('show-neutral').focus(),
      key: 'Enter',
      painted: { selector: '.tm-toast-host .tm-toast', count: 1 },
    },
  },
  {
    id: 'banner',
    entryPoints: ['banner'],
    ready: (page) => expect(page.getByTestId('banner-paged')).toBeVisible(),
    states: [
      {
        name: 'the paged bar moved to its error message',
        enter: async (page) => {
          const bar = page.getByTestId('banner-paged');
          await bar.getByRole('button', { name: 'Previous message' }).click();
          await expect(bar).toHaveAttribute('data-tone', 'error');
        },
      },
    ],
    probe: {
      name: 'Enter on Next shows the next message',
      focus: (page) =>
        page.getByTestId('banner-paged').getByRole('button', { name: 'Next message' }).focus(),
      key: 'Enter',
      painted: {
        selector: '[data-testid="banner-paged"] .tm-banner-bar__position',
        text: '2 of 3',
      },
    },
  },
  {
    id: 'busy-overlay',
    entryPoints: ['busy-overlay'],
    ready: (page) => expect(page.getByTestId('save')).toBeVisible(),
    states: [
      {
        name: 'the scrim, spinner and label up',
        enter: async (page) => {
          // Real time: the scan runs on timers, and the long import keeps the
          // scrim up for as long as the scan takes.
          await page.getByTestId('import').click();
          await expect(page.locator('.tm-busy-overlay')).toHaveClass(/tm-busy-overlay--scrim/);
        },
      },
    ],
    probe: {
      name: 'Enter on Save blocks the page in the key’s frame',
      focus: (page) => page.getByTestId('save').focus(),
      key: 'Enter',
      painted: { selector: '.tm-busy-overlay', className: 'tm-busy-overlay--active' },
    },
  },
  {
    id: 'confirm',
    entryPoints: ['confirm'],
    ready: (page) => expect(page.getByTestId('open-leave')).toBeVisible(),
    states: [
      {
        name: 'the leave guard open',
        enter: async (page) => {
          await page.getByTestId('open-leave').click();
          await expect(page.getByRole('alertdialog')).toBeVisible();
        },
      },
      {
        name: 'the post question open',
        enter: async (page) => {
          await page.getByTestId('open-post').click();
          await expect(page.getByRole('alertdialog')).toBeVisible();
        },
      },
    ],
    probe: {
      name: 'Enter opens the question',
      focus: (page) => page.getByTestId('open-delete').focus(),
      key: 'Enter',
      painted: { selector: '[role="alertdialog"]' },
    },
  },
  {
    id: 'error-dialog',
    entryPoints: ['error-dialog'],
    ready: (page) => expect(page.getByTestId('open-server')).toBeVisible(),
    states: [
      {
        name: 'the validation failure open, its items listed',
        enter: async (page) => {
          await page.getByTestId('open-validation').click();
          await expect(page.getByRole('alertdialog')).toBeVisible();
        },
      },
    ],
    probe: {
      name: 'Enter opens the error dialog',
      focus: (page) => page.getByTestId('open-server').focus(),
      key: 'Enter',
      painted: { selector: '[role="alertdialog"]' },
    },
  },
  {
    id: 'card',
    entryPoints: ['card'],
    ready: (page) => expect(page.getByTestId('card-full')).toBeVisible(),
    states: [
      {
        name: 'a card collapsed, another flagged with an error',
        enter: async (page) => {
          await page.getByRole('button', { name: 'Notes' }).click();
          await page.getByTestId('toggle-invalid').click();
          await expect(page.getByRole('button', { name: 'Address, has errors' })).toBeVisible();
          await expect(page.getByRole('button', { name: 'Notes' })).toHaveAttribute(
            'aria-expanded',
            'false',
          );
        },
      },
    ],
    probe: {
      name: 'Enter on a disclosure collapses its card',
      focus: (page) => page.getByRole('button', { name: 'Notes' }).focus(),
      key: 'Enter',
      painted: {
        selector: '[data-testid="card-collapsible"] button[aria-expanded="false"]',
        count: 1,
      },
    },
  },
  {
    id: 'page-header',
    entryPoints: ['page-header'],
    ready: (page) => expect(page.getByTestId('header-details')).toBeVisible(),
    states: [
      {
        name: 'the end slot’s first item hidden',
        enter: async (page) => {
          await page.getByTestId('toggle-share').click();
          await expect(page.getByTestId('end-share')).toBeHidden();
        },
      },
    ],
    probe: {
      name: 'Enter hides the end slot’s first item',
      focus: (page) => page.getByTestId('toggle-share').focus(),
      key: 'Enter',
      painted: { selector: '[data-testid="end-share"]', count: 0 },
    },
  },
  {
    id: 'description-list',
    entryPoints: ['description-list'],
    ready: (page) => expect(page.getByTestId('dl-one')).toBeVisible(),
    states: [],
    probe: { none: 'A description list is static markup: the story has no control.' },
  },
  {
    id: 'splitter',
    entryPoints: ['splitter'],
    ready: (page) => expect(page.getByTestId('splitter-horizontal')).toBeVisible(),
    states: [],
    probe: {
      name: 'ArrowRight moves the bar',
      focus: (page) => page.getByTestId('splitter-horizontal').getByRole('separator').focus(),
      key: 'ArrowRight',
      painted: { selector: '[data-testid="list-size"]', text: 'List: 436' },
    },
  },
  {
    id: 'pager',
    entryPoints: ['pager'],
    ready: (page) =>
      expect(page.getByTestId('pager-range').locator('.tm-pager__fraction')).toHaveText(
        '1–20 / 120',
      ),
    states: [
      {
        name: 'the page-size select open',
        enter: async (page) => {
          await page.getByTestId('pager-range').locator('tm-select').click();
          await expect(page.getByRole('listbox')).toBeVisible();
        },
      },
    ],
    probe: {
      name: 'Enter on Next page moves to page 2',
      focus: (page) =>
        page.getByTestId('pager-range').getByRole('button', { name: 'Next page' }).focus(),
      key: 'Enter',
      painted: { selector: '[data-testid="pager-range"] .tm-pager__fraction', text: '21–40 / 120' },
    },
  },
  {
    id: 'tree',
    entryPoints: ['tree'],
    ready: (page) => expect(treeNode(page, 'Agent')).toBeVisible(),
    states: [
      {
        name: 'a lazy branch loading in its reserved slot',
        enter: async (page) => {
          await holdResponses(page, 'children');
          await treeNode(page, 'Lines').locator('.tm-tree__twisty').click();
          const entries = treeNodeByKey(page, 'Lines.Entries');
          await expect(entries).toBeVisible();
          await entries.locator('.tm-tree__twisty').first().click();
          await expect(entries.locator('tm-spinner')).toBeVisible();
        },
      },
      {
        name: 'a branch whose load failed',
        enter: async (page) => {
          await holdResponses(page, 'children');
          await treeNode(page, 'Lines').locator('.tm-tree__twisty').click();
          const archive = treeNodeByKey(page, 'Lines.Archive');
          await expect(archive).toBeVisible();
          await archive.locator('.tm-tree__twisty').click();
          await releaseResponses(page, 'children');
          await expect(liveRegion(page)).toHaveText("Couldn't load Archive");
        },
      },
      {
        name: 'nodes selected in the multiple-selection tree',
        enter: async (page) => {
          await treeNode(page, 'Amount', 'tree-multiple').click();
          await expect(page.getByTestId('tree-multiple-selected')).toContainText('Amount');
          await expect(treeNode(page, 'Amount', 'tree-multiple')).toHaveAttribute(
            'aria-selected',
            'true',
          );
        },
      },
    ],
    probe: {
      name: 'ArrowRight expands the active branch',
      focus: (page) => treeNode(page, 'Agent').focus(),
      key: 'ArrowRight',
      painted: { selector: '[data-testid="tree-expanded"]', text: 'Expanded: Agent' },
    },
  },
  {
    id: 'reorder-list',
    entryPoints: ['reorder-list'],
    ready: (page) => expect(reorderHandle(page, 'Code')).toBeVisible(),
    states: [
      {
        name: 'an item picked up',
        enter: async (page) => {
          await reorderHandle(page, 'Code').focus();
          await page.keyboard.press('Space');
          await expect(liveRegion(page)).toHaveText('Code picked up. Position 2 of 5.');
        },
      },
      {
        name: 'a handle’s move menu open',
        enter: async (page) => {
          await reorderHandle(page, 'Code').click();
          await expect(page.getByRole('menu', { name: 'Move Code' })).toBeVisible();
        },
      },
    ],
    probe: {
      name: 'Space on a handle picks the item up',
      focus: (page) => reorderHandle(page, 'Code').focus(),
      key: 'Space',
      painted: {
        selector: '[data-testid="reorder-columns"] .tm-reorder-list__row--grabbed',
        count: 1,
      },
    },
  },
  {
    id: 'tiles',
    entryPoints: ['tiles'],
    ready: (page) => expect(tile(page, 1)).toBeVisible(),
    states: [
      {
        name: 'loading over the data',
        enter: async (page) => {
          await page.getByTestId('busy').click();
          await expect(page.getByTestId('tiles').locator('[role="grid"]')).toHaveAttribute(
            'aria-busy',
            'true',
          );
        },
      },
      {
        name: 'no data, shown through the custom template',
        enter: async (page) => {
          await page.getByTestId('empty').click();
          await page.getByTestId('custom-empty').click();
          await expect(page.getByTestId('custom-empty-text')).toBeVisible();
        },
      },
    ],
    probe: {
      name: 'ArrowRight moves to the next tile',
      focus: (page) => tile(page, 1).focus(),
      key: 'ArrowRight',
      painted: {
        selector: '[data-testid="tiles"] .tm-tiles__item[data-index="1"]:focus',
        count: 1,
      },
    },
  },
  {
    id: 'tiles-tree',
    entryPoints: ['tiles'],
    ready: (page) => expect(account(page, 'Equity')).toBeVisible(),
    states: [],
    probe: {
      name: 'ArrowRight expands the active row',
      focus: (page) => account(page, 'Equity').focus(),
      key: 'ArrowRight',
      painted: { selector: '[data-testid="tiles-tree"] [role="treegrid"]', contains: 'Capital' },
    },
  },
  {
    id: 'tiles-large',
    entryPoints: ['tiles'],
    ready: (page) =>
      expect(page.getByTestId('tiles-large').locator('[role="gridcell"]').first()).toBeVisible(),
    states: [
      {
        // No story spec scans this story: the battery gives it its floor.
        name: 'as loaded',
        enter: () => Promise.resolve(),
      },
    ],
    probe: {
      name: 'ArrowDown moves down a line',
      focus: (page) => tile(page, 1, 'tiles-large').focus(),
      key: 'ArrowDown',
      painted: {
        selector: '[data-testid="tiles-large"] .tm-tiles__item:focus:not([data-index="0"])',
        count: 1,
      },
    },
  },
  {
    id: 'drag-drop',
    entryPoints: ['drag-drop'],
    ready: (page) => expect(treeNode(page, 'Amount', 'drag-tree')).toBeVisible(),
    states: [
      {
        name: 'a drag hovering the drop well',
        enter: async (page) => {
          await appStable(page);
          // A synthetic drag holds the hover state still, which a real one cannot.
          await page.evaluate(() => {
            const row = document.querySelector('[data-key="Amount"] > .tm-tree__row')!;
            const target = document.querySelector('[data-testid="drop-well"]')!;
            const data = new DataTransfer();
            row.dispatchEvent(new DragEvent('dragstart', { bubbles: true, dataTransfer: data }));
            target.dispatchEvent(
              new DragEvent('dragenter', { bubbles: true, cancelable: true, dataTransfer: data }),
            );
          });
          await expect(page.getByTestId('drop-well')).toHaveClass(/tm-drop-target--over/);
        },
      },
    ],
    probe: {
      // Dragging itself is pointer-only; its keyboard path is the tree's.
      name: 'ArrowDown moves the source tree to the next field',
      focus: (page) => treeNode(page, 'Amount', 'drag-tree').focus(),
      key: 'ArrowDown',
      painted: { selector: '[data-testid="drag-tree"] [data-key="Code"]:focus', count: 1 },
    },
  },
  {
    id: 'radio',
    entryPoints: ['radio'],
    ready: (page) =>
      expect(page.getByTestId('ff-required').locator('.tm-form-field__error')).toHaveText(
        'Choose how the order is delivered',
      ),
    states: [],
    probe: {
      name: 'ArrowDown checks the next enabled radio',
      focus: (page) =>
        page.getByTestId('radio-terms').getByRole('radio', { name: 'Net 30' }).focus(),
      key: 'ArrowDown',
      painted: {
        selector: '[data-testid="radio-terms"] .tm-radio-group__row:has(input:checked)',
        contains: 'Card',
      },
    },
  },
  {
    id: 'segmented',
    entryPoints: ['segmented'],
    ready: (page) => expect(page.getByTestId('seg-period')).toBeVisible(),
    states: [
      {
        name: 'icon segments without their labels, one hovered with its tooltip',
        enter: async (page) => {
          await page.getByTestId('container-narrow').click();
          await page.getByTestId('seg-view').locator('.tm-segmented__segment').first().hover();
          await expect(page.locator('.tm-tooltip__panel')).toHaveText('Grid');
        },
      },
    ],
    probe: {
      name: 'ArrowRight checks the next segment',
      focus: (page) => page.getByTestId('seg-view').getByRole('radio', { name: 'Grid' }).focus(),
      key: 'ArrowRight',
      painted: {
        selector: '[data-testid="seg-view"] .tm-segmented__segment--checked',
        contains: 'Tiles',
      },
    },
  },
  {
    id: 'switch',
    entryPoints: ['switch'],
    ready: (page) =>
      expect(page.getByTestId('ff-terms').locator('.tm-form-field__error')).toHaveText(
        'Turn this on to continue',
      ),
    states: [],
    probe: {
      name: 'Space turns the switch on',
      focus: (page) => page.getByRole('switch', { name: 'Notify me by email' }).focus(),
      key: 'Space',
      // A native checkbox toggles when Space comes back up.
      trigger: 'keyup',
      painted: { selector: '[data-testid="switch-notify"] input:checked', count: 1 },
    },
  },
  {
    id: 'search-field',
    entryPoints: ['search-field'],
    ready: (page) => expect(page.getByTestId('search-main')).toBeVisible(),
    states: [
      {
        name: 'text typed, the clear button shown',
        enter: async (page) => {
          await page.getByTestId('search-main').getByRole('searchbox').fill('acme');
          await expect(
            page.getByTestId('search-main').getByRole('button', { name: 'Clear search' }),
          ).toBeVisible();
        },
      },
    ],
    probe: {
      name: 'the first keystroke emits at once',
      focus: (page) => page.getByTestId('search-main').getByRole('searchbox').focus(),
      key: 'j',
      painted: { selector: '[data-testid="search-emitted"]', text: '["j"]' },
    },
  },
  {
    id: 'date-range',
    entryPoints: ['date-range'],
    ready: (page) => expect(page.getByTestId('range-period')).toBeVisible(),
    states: [
      {
        name: 'an out-of-order range, erring on "to"',
        enter: async (page) => {
          const range = page.getByTestId('range-period');
          await range.getByRole('combobox', { name: 'From' }).fill('3/10/2026');
          const to = range.getByRole('combobox', { name: 'To' });
          await to.fill('3/1/2026');
          await to.blur();
          // The "to" part's own field carries the error.
          await expect(
            range.locator('tm-form-field').nth(1).locator('.tm-form-field__error'),
          ).toHaveText('The end date must be on or after the start date');
        },
      },
      {
        name: 'the "from" calendar open',
        enter: (page) =>
          openDatePicker(
            page,
            page.getByTestId('range-period').getByRole('combobox', { name: 'From' }),
          ),
      },
    ],
    probe: {
      // The popup opens in the key's frame. Its calendar renders a frame or
      // more later: the date picker defers the calendar to its own chunk
      // (spec 0010's popup), which this probe exempts; the axe state above
      // checks the calendar once it shows.
      name: 'Alt+ArrowDown opens the calendar popup',
      focus: (page) =>
        page.getByTestId('range-period').getByRole('combobox', { name: 'From' }).focus(),
      key: 'Alt+ArrowDown',
      painted: {
        selector: '[data-testid="range-period"] [role="combobox"][aria-expanded="true"]',
        count: 1,
      },
    },
  },
  {
    id: 'date-time',
    entryPoints: ['date-time'],
    ready: (page) => expect(page.getByTestId('dt-beirut')).toBeVisible(),
    states: [
      {
        name: 'the date-time field’s calendar open',
        enter: (page) =>
          openDatePicker(
            page,
            page.getByTestId('dt-beirut').getByRole('combobox', { name: 'Date' }),
          ),
      },
      {
        name: 'a date without its time, erring on the time',
        enter: async (page) => {
          const field = page.getByTestId('dt-beirut');
          await field.getByRole('combobox', { name: 'Date' }).fill('1/15/2026');
          await field.getByRole('combobox', { name: 'Date' }).blur();
          // The time part's own field carries the error.
          await expect(
            field.locator('tm-form-field').nth(1).locator('.tm-form-field__error'),
          ).toHaveText('Enter both a date and a time');
        },
      },
    ],
    probe: {
      name: 'ArrowUp steps the time across midnight',
      focus: async (page) => {
        // From a known time, so the stepped one is known too.
        const time = page.getByTestId('time-closes').getByRole('textbox');
        await time.fill('11:45 pm');
        await time.press('Enter');
        await expect(time).toHaveValue('11:45 PM');
      },
      key: 'ArrowUp',
      painted: { selector: '[data-testid="time-closes"] input', value: '12:15 AM' },
    },
  },
];
