// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { TmLocalization, type TmShortcut } from '@tellma/core';
import { tmSearchNormalize } from '@tellma/core/search-normalize';
import { TM_GRID_KEYS } from '@tellma/core-ui/contracts';
import { type TmKeys, TmKbd } from '@tellma/core-ui/kbd';
import { TmSearchField } from '@tellma/core-ui/search-field';

import { tmRegisterPinCommandsStrings } from '../pins/tm-pin-commands-strings';

// The Shortcuts dialog (spec 0027 §11.2), a deferred chunk that the session
// menu's Keyboard shortcuts row and Mod+/ open. A filter field narrows every
// section's rows through the search's text folding. Each section is a
// two-column table, what a key does and its keys:
//
// - General: the global registrations as the dialog opened;
// - This page: the page's registrations as the dialog opened, hidden when
//   it has none;
// - Lists and grids: the grid's published keys, one row per label;
// - Launcher: the launcher's keys.
//
// The dialog registers no shortcut of its own.

/** The scope of the Shortcuts dialog's strings under `tm`. */
const SCOPE = 'shortcutsDialog';

/** The Shortcuts dialog's English strings, under `tm.shortcutsDialog`. */
export const TM_SHORTCUTS_DIALOG_STRINGS_EN = {
  filter: 'Filter shortcuts',
  /** The tables' column headers. */
  action: 'Action',
  keys: 'Keys',
  /** The sections. */
  general: 'General',
  page: 'This page',
  grids: 'Lists and grids',
  launcher: 'Launcher',
  /** The launcher's keys. */
  launcherMove: 'Move between results',
  launcherOpen: 'Open the result',
  launcherNewTab: 'Open the result in a new tab',
  launcherClose: 'Close the launcher',
  /** The filter matched nothing. */
  noMatch: 'No shortcut matches.',
} as const;

/** One row of a section: what the keys do, and the combinations that do it. */
interface TmShortcutRow {
  /** What the keys do. */
  readonly label: string;
  /** The combinations, in order. */
  readonly keys: readonly TmKeys[];
}

/** One section of the dialog. */
interface TmShortcutSection {
  /** The section's id, which names its heading. */
  readonly id: string;
  /** The heading. */
  readonly title: string;
  /** The rows the filter keeps. */
  readonly rows: readonly TmShortcutRow[];
}

/** The launcher's keys, with their labels' keys. */
const LAUNCHER_KEYS: readonly (readonly [string, readonly TmKeys[]])[] = [
  ['launcherMove', ['ArrowUp', 'ArrowDown']],
  ['launcherOpen', ['Enter']],
  ['launcherNewTab', ['Mod+Enter', 'Shift+Enter']],
  ['launcherClose', ['Escape']],
];

/** Counts the dialogs, so each one's heading ids are unique in the document. */
let nextDialogId = 0;

/** The Shortcuts dialog: a filter, then the general, page, grid and launcher keys, each in a table. */
@Component({
  selector: 'tm-shortcuts-dialog',
  imports: [TmKbd, TmSearchField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The filter. -->
    <tm-search-field [ariaLabel]="text('filter')" [placeholder]="text('filter')" [(value)]="query" />
    <!-- The sections, each a table; a section the filter empties is hidden. -->
    @for (section of sections(); track section.id) {
      @if (section.rows.length > 0) {
        <section class="tm-shortcuts-dialog__section">
          <h3 class="tm-shortcuts-dialog__title" [id]="section.id">{{ section.title }}</h3>
          <table class="tm-shortcuts-dialog__table" [attr.aria-labelledby]="section.id">
            <thead>
              <tr>
                <th scope="col">{{ text('action') }}</th>
                <th scope="col">{{ text('keys') }}</th>
              </tr>
            </thead>
            <tbody>
              @for (row of section.rows; track $index) {
                <tr>
                  <td>{{ row.label }}</td>
                  <td class="tm-shortcuts-dialog__keys">
                    @for (keys of row.keys; track keys) {
                      <tm-kbd [keys]="keys" />
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </section>
      }
    }
    @if (empty()) {
      <p class="tm-shortcuts-dialog__empty" role="status">{{ text('noMatch') }}</p>
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-4);
    }

    .tm-shortcuts-dialog__title {
      margin: 0 0 var(--space-2);
      font-size: var(--text-sm);
      font-weight: var(--weight-semibold);
    }

    .tm-shortcuts-dialog__table {
      inline-size: 100%;
      border-collapse: collapse;
      font-size: var(--text-sm);
    }

    .tm-shortcuts-dialog__table th {
      color: var(--text-muted);
      font-size: var(--text-xs);
      font-weight: var(--weight-regular);
      text-align: start;
    }

    .tm-shortcuts-dialog__table th,
    .tm-shortcuts-dialog__table td {
      padding-block: var(--space-1);
      border-block-end: var(--border-width) solid var(--divider);
    }

    .tm-shortcuts-dialog__keys {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      justify-content: flex-end;
    }

    .tm-shortcuts-dialog__empty {
      margin: 0;
      color: var(--text-muted);
    }
  `,
})
export class TmShortcutsDialog {
  readonly #l10n = inject(TmLocalization);
  readonly #id = `tm-shortcuts-dialog-${nextDialogId++}`;

  /** The live registrations as the dialog opened. */
  readonly registered = input<readonly TmShortcut[]>([]);

  /** The filter's text. */
  protected readonly query = signal('');

  /** Every section with every row, before the filter. */
  readonly #all = computed<readonly TmShortcutSection[]>(() => {
    const registered = this.registered();
    const rowsOf = (scope: TmShortcut['scope']): TmShortcutRow[] =>
      registered.filter((shortcut) => shortcut.scope === scope).map((shortcut) => ({ label: this.#l10n.ɵtext(shortcut.labelKey)(), keys: [shortcut.keys] }));
    // The grid's entries that share a label form one row.
    const grid = new Map<string, TmKeys[]>();
    for (const { keys, labelKey } of TM_GRID_KEYS) {
      grid.set(labelKey, [...(grid.get(labelKey) ?? []), keys]);
    }
    return [
      { id: `${this.#id}-general`, title: this.text('general'), rows: rowsOf('global') },
      { id: `${this.#id}-page`, title: this.text('page'), rows: rowsOf('page') },
      {
        id: `${this.#id}-grids`,
        title: this.text('grids'),
        rows: [...grid].map(([labelKey, keys]) => ({ label: this.#l10n.ɵtext(labelKey)(), keys })),
      },
      {
        id: `${this.#id}-launcher`,
        title: this.text('launcher'),
        rows: LAUNCHER_KEYS.map(([key, keys]) => ({ label: this.text(key), keys })),
      },
    ];
  });

  /** The sections with the rows the filter keeps: a row whose label holds the folded text. */
  protected readonly sections = computed<readonly TmShortcutSection[]>(() => {
    const query = tmSearchNormalize(this.query().trim());
    const all = this.#all();
    return query === '' ? all : all.map((section) => ({ ...section, rows: section.rows.filter((row) => tmSearchNormalize(row.label).includes(query)) }));
  });

  /** Whether the filter emptied every section. */
  protected readonly empty = computed(() => this.sections().every((section) => section.rows.length === 0));

  /** Creates the dialog, which registers its strings and the pin commands', which name Alt+Shift+P's row. */
  constructor() {
    this.#l10n.ɵregisterStrings(SCOPE, TM_SHORTCUTS_DIALOG_STRINGS_EN);
    tmRegisterPinCommandsStrings(this.#l10n);
  }

  /**
   * A string of the dialog.
   *
   * @param key - The key under `tm.shortcutsDialog`.
   * @returns The text.
   */
  protected text(key: string): string {
    return this.#l10n.ɵtext(`tm.shortcutsDialog.${key}`)();
  }
}
