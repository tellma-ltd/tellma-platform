// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The grid's built-in cell editors. Each is a thin internal component whose
// hosted control self-registers as the session's `TmCellEditor` through the
// cell-scoped injector's TM_CELL_EDITOR_HOST — the same discovery path a
// consumer `*tmGridEditor` template uses, so the session drives built-ins
// and custom editors identically.

import { Component, input, output, signal, viewChild, ViewEncapsulation } from '@angular/core';

import { TmDatePicker } from '@tellma/core-ui/date-picker';
import {
  TmEntityPicker,
  type ɵTmEntityAdoptedSearch,
  type TmEntityId,
  type TmEntityPickerPage,
  type TmEntitySearchFn,
} from '@tellma/core-ui/entity-picker';
import { TmInput } from '@tellma/core-ui/input';
import { TmNumber } from '@tellma/core-ui/number';
import { TmOption, TmSelect } from '@tellma/core-ui/select';

/**
 * The built-in text editor (`text`/`date`/`custom` columns): a bare
 * `tmInput` filling the cell box. The input registers itself with the
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
 * The built-in number editor (`number` columns): a bare `tmNumber` filling
 * the cell box. The grid owns the value channel and the parse (the
 * control's own commit loop stands down in a cell); what the editor
 * contributes is `inputmode="decimal"`, the physical right alignment, and
 * one control to theme.
 */
@Component({
  selector: 'tm-grid-number-editor',
  imports: [TmNumber],
  template: `<input tmNumber class="tm-grid__editor-input" [attr.aria-label]="label()" />`,
  styleUrl: './editors.css',
  host: { class: 'tm-grid-number-editor' },
})
export class ɵTmGridNumberEditor {
  /** The accessible name (the column's header text). */
  readonly label = input('');
}

/**
 * The built-in date editor (`date` columns): a `tm-date-picker` filling
 * the cell box, its popup anchored to the cell rect through the shared
 * overlay helper. The grid owns the value channel and the parse; the
 * picker contributes typed entry, the cell-anchored calendar popup
 * (`Alt+ArrowDown` per the dropdown-cell convention), and stage one of
 * the two-stage Esc. Picking a day in the popup emits `activated`, which
 * the session turns into commit-and-close — the same shape as activating
 * an enum option.
 */
@Component({
  selector: 'tm-grid-date-editor',
  imports: [TmDatePicker],
  template: `<tm-date-picker [aria-label]="label()" (picked)="activated.emit()" />`,
  styleUrl: './editors-date.css',
  // Encapsulation OFF: the picker's inner input carries the picker's own
  // scope attribute, which a scoped stylesheet here could never match.
  encapsulation: ViewEncapsulation.None,
  host: { class: 'tm-grid-date-editor' },
})
export class ɵTmGridDateEditor {
  /** The accessible name (the column's header text). */
  readonly label = input('');
  /** Emits when the user picks a date in the calendar popup (not by typing). */
  readonly activated = output<void>();

  private readonly picker = viewChild.required(TmDatePicker);

  /** Whether the calendar popup is open (the editing keymap's dropdown gate). */
  isPopupOpen(): boolean {
    return this.picker().isPopupOpen();
  }

  /** Opens the calendar popup (Alt+ArrowDown on the cell). */
  openPopup(): void {
    this.picker().openPopup();
  }
}

/**
 * The built-in entity editor (`entity` columns configured with `search`): a
 * bare `tm-entity-picker` filling the cell box, its dropdown anchored to
 * the cell rect through the picker's own anchor rule. The picker registers
 * itself with the session; a pick that closes the cell (pointer or Enter
 * activation, a modal-page pick) emits `activated`, which the session turns
 * into commit-and-close — Tab-commits and unique-match auto-picks stay
 * silent so the grid's own commit paths run exactly once.
 */
@Component({
  selector: 'tm-grid-entity-editor',
  imports: [TmEntityPicker],
  template: `
    <tm-entity-picker
      [aria-label]="label()"
      [search]="search()"
      [itemId]="itemId()"
      [itemLabel]="itemLabel()"
      [displayWith]="displayWith()"
      [advancedSearch]="advancedSearch()"
      [create]="create()"
      [edit]="edit()"
      (ɵcellActivate)="activated.emit()"
    />
  `,
  styleUrl: './editors-entity.css',
  // Encapsulation OFF: the picker's inner input carries the picker's own
  // scope attribute, which a scoped stylesheet here could never match.
  encapsulation: ViewEncapsulation.None,
  host: { class: 'tm-grid-entity-editor' },
})
export class ɵTmGridEntityEditor {
  /** The accessible name (the column's header text). */
  readonly label = input('');
  /** The column's search facility. */
  readonly search = input.required<TmEntitySearchFn<unknown>>();
  /** Maps a search result to its id. */
  readonly itemId = input.required<(item: unknown) => TmEntityId>();
  /** Maps a search result to its display text. */
  readonly itemLabel = input.required<(item: unknown) => string>();
  /** The committed-id display resolver (the column's `format`, adapted). */
  readonly displayWith = input<((id: unknown) => string | null) | undefined>(undefined);
  /** The column's advanced-search page, if any. */
  readonly advancedSearch = input<TmEntityPickerPage | undefined>(undefined);
  /** The column's create page, if any. */
  readonly create = input<TmEntityPickerPage | undefined>(undefined);
  /** The column's edit page, if any. */
  readonly edit = input<TmEntityPickerPage | undefined>(undefined);
  /** Emits on pick-commits that close the cell (never Tab or auto picks). */
  readonly activated = output<void>();

  private readonly picker = viewChild.required(TmEntityPicker);

  /** Whether the dropdown is open (the editing keymap's dropdown gate). */
  isDropdownOpen(): boolean {
    return this.picker().isDropdownOpen();
  }

  /** Opens the dropdown (Alt+ArrowDown on the cell — the pristine browse list). */
  openDropdown(): void {
    this.picker().openDropdown();
  }

  /** Installs text WITHOUT searching or opening (edit-mode opens). */
  setText(text: string): void {
    this.picker().ɵsetCellText(text);
  }

  /**
   * Takes over the picker's own search for `text` so the grid can decide a
   * typed commit from it. The request detaches from this editor's lifetime;
   * `null` when the picker has nothing for that exact text.
   */
  adoptSearch(text: string): ɵTmEntityAdoptedSearch<unknown> | null {
    return this.picker().ɵadoptSearch(text);
  }
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
