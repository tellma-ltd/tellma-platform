// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, input, model, output } from '@angular/core';

import type {
  TmEntityId,
  TmEntityPicked,
  TmEntityPickerPage,
  TmEntitySearchFn,
} from './tm-entity-picker-types';

/**
 * The server-searched foreign-key selector: an editable combobox over a
 * consumer-supplied search function. The text is a query surface, never the
 * value — the committed value is the entity id, written only by picks,
 * empty commits, and failed resolutions.
 *
 * @tmGroup form-control
 * @tmStatus experimental
 * @tmA11yNotes Editable combobox with a listbox popup; DOM focus stays on
 * the input while `aria-activedescendant` tracks the highlighted option.
 */
@Component({
  selector: 'tm-entity-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<input type="text" class="tm-input tm-entity-picker__input" [placeholder]="placeholder()" />`,
  styleUrl: './tm-entity-picker.css',
  host: { class: 'tm-entity-picker' },
})
export class TmEntityPicker<T, Id extends TmEntityId = TmEntityId> {
  /** The committed foreign-key id; `null` when the field is empty. */
  readonly value = model<Id | null>(null);
  /** The consumer's search facility — the picker's only data source. */
  readonly search = input.required<TmEntitySearchFn<T>>();
  /** Maps a search result to its id. */
  readonly itemId = input.required<(item: T) => Id>();
  /** Maps a search result to its display text (called reactively). */
  readonly itemLabel = input.required<(item: T) => string>();
  /**
   * Resolves a committed id to display text (called reactively); `null`
   * defers to the pick-time label memo, then to `String(id)`.
   */
  readonly displayWith = input<((id: Id) => string | null) | undefined>(undefined);
  /** The advanced-search page; absent ⇒ no magnifier and no footer row. */
  readonly advancedSearch = input<TmEntityPickerPage | undefined>(undefined);
  /** The create page; absent ⇒ no Create… footer row. */
  readonly create = input<TmEntityPickerPage | undefined>(undefined);
  /** The edit page; absent ⇒ no Edit… footer row. */
  readonly edit = input<TmEntityPickerPage | undefined>(undefined);
  /** Overrides the localized Create… footer-row caption. */
  readonly createLabel = input<string | undefined>(undefined);
  /** Overrides the localized Edit… footer-row caption. */
  readonly editLabel = input<string | undefined>(undefined);
  /** Placeholder text for the empty input. */
  readonly placeholder = input('');
  /** The search coalescing window in milliseconds; `0` disables it. */
  readonly searchDebounce = input(50);
  /** Emits every committed selection, whatever produced it. */
  readonly picked = output<TmEntityPicked<T, Id>>();
}
