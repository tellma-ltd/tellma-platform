// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import { ChangeDetectionStrategy, Component, ElementRef, inject, input, model, output, signal, viewChild } from '@angular/core';
import Search from 'lucide/dist/esm/icons/search.mjs';
import X from 'lucide/dist/esm/icons/x.mjs';
import { TmLocalization } from '@tellma/core';
import { ɵtmControlSize } from '@tellma/core-ui';
import { TmGridDriver, type TmGridDriverTarget } from '@tellma/core-ui/grid';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmSearch } from '@tellma/core-ui/search-input';

// The search page's field (spec 0030 §3.2, §6.1): a native search input in
// the form field's chrome, from the first frame, so the header never shifts.
// It filters as the user types, through the search directive's leading and
// trailing edges, and drives the results' highlight while focus stays in it.
// The clear button keeps its slot while hidden, so the text never reflows.

/** The last field's number, which keeps each field's hint id apart. */
let fields = 0;

/**
 * The search page's field: the magnifier, the input and the clear button in
 * the form field's chrome, at the workspace's size (`lg` below `sm`). The
 * page reads its text, searches what it emits, and hands it what its Enter
 * and its arrow keys drive.
 *
 * @example
 * ```html
 * <tm-search-page-field [driver]="driver" [(text)]="text" titleId="title" (searched)="search($event)" />
 * ```
 */
@Component({
  selector: 'tm-search-page-field',
  imports: [TmGridDriver, TmIcon, TmSearch],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <tm-icon class="tm-search-page-field__magnifier" [svg]="glyphs.search" />
    <input
      #field
      tmSearch
      class="tm-search-page-field__input"
      data-tm-search
      [tmGridDriver]="driver()"
      [(value)]="text"
      [attr.maxlength]="maxLength()"
      [placeholder]="label()"
      [attr.aria-label]="label()"
      [attr.aria-describedby]="titleId() + ' ' + hintId"
      (search)="searched.emit($event)"
      (keydown)="key.emit($event)"
      (blur)="left.emit()"
    />
    <!-- The clear button: an undoable edit that searches the empty text and keeps focus in the field. -->
    <button
      type="button"
      class="tm-search-page-field__clear"
      data-tm-search-clear
      [class.tm-search-page-field__clear--hidden]="text() === ''"
      [attr.aria-label]="clearLabel()"
      [title]="clearLabel()"
      (click)="clear()"
    >
      <tm-icon [svg]="glyphs.clear" />
    </button>
    <span [id]="hintId" hidden>{{ hint() }}</span>
  `,
  styleUrl: './tm-search-page-field.css',
  host: {
    class: 'tm-search-page-field',
    '[class.tm-search-page-field--sm]': 'size() === "sm"',
    '[class.tm-search-page-field--lg]': 'size() === "lg"',
  },
})
export class ɵTmSearchPageField {
  /** What the field's arrow keys and Enter drive: the results' highlight. */
  readonly driver = input.required<TmGridDriverTarget>();

  /** The field's text as typed. */
  readonly text = model('');

  /** The field's `maxlength`; `null` for none. */
  readonly maxLength = input<number | null>(null);

  /** The id of the page's title, which describes the field first. */
  readonly titleId = input.required<string>();

  /** The text to search: on the leading and trailing edges of a burst of typing, on Enter and on clear. */
  readonly searched = output<string>();

  /** Every key pressed in the field. */
  readonly key = output<KeyboardEvent>();

  /** Focus left the field. */
  readonly left = output<void>();

  /** The localized texts. */
  readonly #l10n = inject(TmLocalization);

  /** The field's name and placeholder, "Search". */
  protected readonly label = this.#l10n.ɵtext('tm.search.search');

  /** The clear button's name. */
  protected readonly clearLabel = this.#l10n.ɵtext('tm.search.clearSearch');

  /** How to move through the results from the field. */
  protected readonly hint = this.#l10n.ɵtext('tm.search.searchHint');

  /** The hidden hint's id. */
  protected readonly hintId = `tm-search-hint-${++fields}`;

  /** The field's size: the workspace's, and `lg` below `sm`, as every control's. */
  protected readonly size = ɵtmControlSize(signal(undefined));

  /** The icons. */
  protected readonly glyphs = { search: Search, clear: X };

  /** The input element. */
  private readonly field = viewChild.required<ElementRef<HTMLInputElement>>('field');

  /** The search behaviour, which flushes a burst on Enter. */
  private readonly searcher = viewChild.required(TmSearch);

  /** The input, while it is in the document. */
  input(): HTMLInputElement | null {
    const element = this.field().nativeElement;
    return element.isConnected ? element : null;
  }

  /** Whether focus is in the input. */
  focused(): boolean {
    const element = this.field().nativeElement;
    return element.ownerDocument.activeElement === element;
  }

  /** Searches the text now, ending the burst. */
  flush(): void {
    this.searcher().flush();
  }

  /** Clears the text as an undoable edit, searches the empty text and keeps focus in the field. */
  clear(): void {
    this.searcher().clear();
  }
}
