// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  Component,
  computed,
  inject,
  input,
  model,
  numberAttribute,
  output,
  viewChild,
  ViewEncapsulation,
  type ElementRef,
} from '@angular/core';
import { Search, X } from 'lucide';

import { TM_UI_TRANSLATE, type TmControlSize } from '@tellma/core-ui';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmInput } from '@tellma/core-ui/input';

import { TmSearch } from './tm-search';

/**
 * The search box of pages and panels: a magnifier, the search input and a
 * clear button in field chrome, at the field height of the resolved size.
 * The timing of `search` is `TmSearch`'s: on the leading and trailing
 * edges of a burst of typing, at once on Enter and on clear, never twice
 * with the same text.
 *
 * The clear button's slot is always reserved, so the text never reflows
 * when it comes and goes; the button shows while there is text, and clears
 * it undoably, emits and refocuses the input. Binds by `[(value)]`, not as
 * a form control: a search box is not a form value.
 *
 * Loading feedback is the consumer's: turn a `tm-progress-bar` active when
 * `search` emits, and keep it active while the shown result is not for the
 * last emitted text.
 *
 * @tmGroup form-control
 * @tmA11yNotes One tab stop for the input plus the clear button while it
 *   shows; the input is named by `ariaLabel`, the button "Clear search".
 *   Enter searches at once; Escape clears non-empty text and otherwise
 *   propagates; Ctrl+Z (Cmd+Z on Apple) undoes a clear.
 */
@Component({
  selector: 'tm-search-field',
  imports: [TmFormField, TmIcon, TmInput, TmSearch],
  // The stylesheet reaches into the enclosing field's box (the invalid
  // border, the reserved clear slot), so it is not encapsulated; every
  // selector is namespaced under .tm-search-field.
  encapsulation: ViewEncapsulation.None,
  template: `
    <tm-form-field class="tm-search-field__field" [size]="size()">
      <!-- The magnifier: decoration; the input carries the name. -->
      <tm-icon tmPrefix class="tm-search-field__icon" [svg]="glyphs.search" />
      <input
        #input
        tmInput
        tmSearch
        class="tm-search-field__input"
        [(value)]="value"
        [debounce]="debounce()"
        [placeholder]="effectivePlaceholder()"
        [invalid]="invalid()"
        [touched]="invalid()"
        [attr.aria-label]="ariaLabel()"
        (search)="search.emit($event)"
      />
      <!-- The clear button keeps its slot while hidden, so the text never
           reflows; hidden, it is out of the tab order and the a11y tree. -->
      <button
        tmSuffix
        type="button"
        class="tm-search-field__clear tm-form-field__trailing-icon"
        [class.tm-search-field__clear--hidden]="value() === ''"
        [attr.aria-label]="clearLabel()"
        [attr.aria-hidden]="value() === '' ? 'true' : null"
        [tabIndex]="value() === '' ? -1 : 0"
        (click)="clear()"
      >
        <tm-icon [svg]="glyphs.clear" />
      </button>
    </tm-form-field>
  `,
  styleUrl: './tm-search-field.css',
  host: {
    class: 'tm-search-field',
    '[class.tm-search-field--invalid]': 'invalid()',
  },
})
export class TmSearchField {
  private readonly translate = inject(TM_UI_TRANSLATE);

  /** The text, updated on every keystroke. */
  readonly value = model('');
  // The name the platform's specs give this output; no native `search`
  // event reaches this element (the input's is stopped at its target, and
  // it does not bubble).
  /* eslint-disable @angular-eslint/no-output-native -- named by the spec */
  /** The query, on the leading and trailing edges of a burst of typing, on Enter and on clear. */
  readonly search = output<string>();
  /* eslint-enable @angular-eslint/no-output-native */
  /** The input's accessible name ("Search customers"). */
  readonly ariaLabel = input.required<string>();
  /** The placeholder; default "Search". */
  readonly placeholder = input<string | undefined>(undefined);
  /** The size step; unset, the workspace default (the touch height on a phone). */
  readonly size = input<TmControlSize | undefined>(undefined);
  /** Marks the query invalid (a red border and `aria-invalid`), shown at once. */
  readonly invalid = input(false, { transform: booleanAttribute });
  /** The coalescing window of `search` in milliseconds; `0` emits on every input. Default 50. */
  readonly debounce = input(50, { transform: numberAttribute });

  /** The glyph data: the magnifier and the clear button's cross. */
  protected readonly glyphs = { search: Search, clear: X } as const;
  /** The placeholder in effect: the input, else the localized "Search". */
  protected readonly effectivePlaceholder = computed(
    () => this.placeholder() ?? this.translate('searchField.placeholder')(),
  );
  /** The clear button's localized accessible name. */
  protected readonly clearLabel = computed(() => this.translate('searchField.clear')());

  private readonly inputElement = viewChild.required<ElementRef<HTMLInputElement>>('input');
  private readonly searchBehaviour = viewChild.required(TmSearch);

  /** Focuses the input; `select: true` also selects its text, for retyping. */
  focus(options?: { readonly select?: boolean }): void {
    const element = this.inputElement().nativeElement;
    element.focus();
    if (options?.select === true) {
      element.select();
    }
  }

  /** Clears the text undoably, emits the empty query and refocuses the input. */
  protected clear(): void {
    this.searchBehaviour().clear();
  }
}
