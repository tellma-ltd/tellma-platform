// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The grid's built-in cell editors. Each is a thin internal component whose
// hosted control self-registers as the session's `TmCellEditor` through the
// cell-scoped injector's TM_CELL_EDITOR_HOST — the same discovery path a
// consumer `*tmGridEditor` template uses, so the session drives built-ins
// and custom editors identically.

import { Component, input, output, signal, viewChild } from '@angular/core';

import { TmInput } from '@tellma/core-ui/input';
import { TmOption, TmSelect } from '@tellma/core-ui/select';

/**
 * The built-in text editor (`text`/`number`/`date`/`custom` columns): a
 * bare `tmInput` filling the cell box. The input registers itself with the
 * session through TM_CELL_EDITOR_HOST; parsing the committed text is the
 * engine's concern (§ the column's parse), never the editor's.
 */
@Component({
  selector: 'tm-grid-text-editor',
  imports: [TmInput],
  template: `<input tmInput class="tm-grid__editor-input" [attr.aria-label]="label()" />`,
  styleUrl: './editors.css',
  host: { class: 'tm-grid-text-editor' },
})
export class ɵTmGridTextEditor {
  /** The accessible name (the column's header text). */
  readonly label = input('');
}

/**
 * The built-in enum editor: a `tm-select` populated from the column's
 * options, size-matched to the cell box (the field-height custom properties
 * are re-pointed at the cell height in `editors.css`). The select registers
 * itself with the session; activating an option emits `activated`, which
 * the session turns into commit-and-close (Sheets behavior).
 */
@Component({
  selector: 'tm-grid-enum-editor',
  imports: [TmSelect, TmOption],
  template: `
    <tm-select
      [aria-label]="label()"
      (selectionChange)="activated.emit()"
      (opened)="panelOpen.set(true)"
      (closed)="panelOpen.set(false)"
    >
      @for (option of options(); track $index) {
        <tm-option [value]="valueOf(option)" [label]="textOf(option)">{{ textOf(option) }}</tm-option>
      }
    </tm-select>
  `,
  styleUrl: './editors.css',
  host: { class: 'tm-grid-enum-editor' },
})
export class ɵTmGridEnumEditor {
  /** The accessible name (the column's header text). */
  readonly label = input('');
  /** The column's options, in display order. */
  readonly options = input<readonly unknown[]>([]);
  /** Maps an option to its display label (`String(option)` otherwise). */
  readonly optionLabel = input<((option: unknown) => string) | undefined>(undefined);
  /** Maps an option to the value written to the model (the option itself otherwise). */
  readonly optionValue = input<((option: unknown) => unknown) | undefined>(undefined);
  /** Emits when the user activates an option (click, Enter, Space). */
  readonly activated = output<void>();

  /** Whether the options panel is open (the editing keymap's dropdown gate). */
  readonly panelOpen = signal(false);

  private readonly select = viewChild.required(TmSelect);

  /** Opens the options panel (Enter / Alt+ArrowDown on the cell). */
  openPanel(): void {
    this.select().open();
  }

  /** The model value of an option. */
  protected valueOf(option: unknown): unknown {
    const map = this.optionValue();
    return map === undefined ? option : map(option);
  }

  /** The display label of an option. */
  protected textOf(option: unknown): string {
    const map = this.optionLabel();
    return map === undefined ? String(option) : map(option);
  }
}
