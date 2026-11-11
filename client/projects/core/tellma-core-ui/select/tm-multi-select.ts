// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, isDevMode, model, output } from '@angular/core';
import { Combobox, ComboboxPopup, ComboboxWidget } from '@angular/aria/combobox';
import { Listbox, Option } from '@angular/aria/listbox';
import { OverlayModule } from '@angular/cdk/overlay';

import { TM_ACTIVE_LOCALE } from '@tellma/core-ui';
import { TM_FORM_FIELD_CONTROL } from '@tellma/core-ui/form-field';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmSpinner } from '@tellma/core-ui/spinner';

import { TmSelectBase } from './tm-select-base';

/**
 * Closed-list multiple choice: the `tm-select` trigger and panel, with a
 * checkbox on every row. It reads the same `tm-option` and
 * `tm-option-group` definitions as `tm-select`.
 *
 * Click, Space and Enter toggle the active option and keep the panel open;
 * Escape, Tab and a press outside close it. Home, End and typeahead move
 * through the open panel, and a printable character on the closed trigger
 * opens it at the first match without changing the value. Shift with an
 * arrow extends a range, and Ctrl+A (⌘A on Apple) toggles every option.
 *
 * The value is always in option order, never click order. Values that no
 * option matches are kept, never pruned, after the matched ones; they show
 * in the trigger through `displayWith`, else as `String(value)` with a
 * development warning.
 *
 * The trigger keeps one line at the field height: the selected labels
 * joined as a short list in the active locale ("Service, Operation"),
 * ellipsized, or the placeholder when nothing is selected.
 *
 * Not a grid cell editor: grid cells hold scalars.
 *
 * @example
 * ```html
 * <tm-form-field label="Center type">
 *   <tm-multi-select [formField]="filter.types">
 *     <tm-option-group label="Operating">
 *       <tm-option value="Service">Service</tm-option>
 *       <tm-option value="Operation">Operation</tm-option>
 *     </tm-option-group>
 *     <tm-option value="Sale">Sale</tm-option>
 *   </tm-multi-select>
 * </tm-form-field>
 * ```
 *
 * @tmGroup form-control
 * @tmA11yNotes A select-only combobox over a listbox with
 *   `aria-multiselectable="true"`; focus never leaves the trigger
 *   (active-descendant model), and every option carries `aria-selected`.
 *   The trigger's text is the full joined list (only its rendering is
 *   ellipsized), so assistive technology reads every selected label.
 *   Option groups render as `role="group"` named by their heading.
 */
@Component({
  selector: 'tm-multi-select',
  imports: [
    Combobox,
    ComboboxPopup,
    ComboboxWidget,
    Listbox,
    Option,
    NgTemplateOutlet,
    OverlayModule,
    TmIcon,
    TmSpinner,
  ],
  providers: [{ provide: TM_FORM_FIELD_CONTROL, useExisting: TmMultiSelect }],
  template: `
    <div
      ngCombobox
      #cb="ngCombobox"
      class="tm-select__trigger"
      [id]="controlId()"
      [(expanded)]="expanded"
      [disabled]="disabled()"
      [softDisabled]="readonly()"
      [attr.aria-label]="ariaLabel()"
      [attr.aria-labelledby]="ariaLabelledBy()"
      [attr.aria-describedby]="describedByAttr()"
      [attr.aria-invalid]="showsInvalid() ? 'true' : null"
      [attr.aria-busy]="pending() ? 'true' : null"
      [attr.aria-required]="required() ? 'true' : null"
      [attr.aria-readonly]="readonly() ? 'true' : null"
      (keydown)="onTriggerKeydown($event)"
      (blur)="touch.emit()"
    >
      <span class="tm-select__value" [class.tm-select__value--placeholder]="showsPlaceholder()">
        {{ triggerLabel() }}
      </span>
      @if (pending()) {
        <tm-spinner class="tm-select__spinner" />
      }
      <tm-icon class="tm-select__caret" [svg]="caretIcon" />
    </div>

    <ng-template
      [cdkConnectedOverlay]="anchored.overlayConfig()"
      [cdkConnectedOverlayOpen]="expanded()"
      (attach)="anchored.handleAttach()"
    >
      <ng-template ngComboboxPopup [combobox]="cb">
        <!-- A press inside the panel must not take focus from the trigger:
             the panel stays open across toggles only while the combobox
             keeps focus, and Escape and Tab are the trigger's keys. -->
        <div class="tm-select__panel" (mousedown)="$event.preventDefault()">
          <ul
            ngListbox
            ngComboboxWidget
            #lb="ngListbox"
            class="tm-select__listbox"
            [tabindex]="-1"
            focusMode="activedescendant"
            selectionMode="explicit"
            [multi]="true"
            [readonly]="readonly()"
            [(value)]="listboxValue"
            [activeDescendant]="lb.activeDescendant()"
            (click)="commitFromListbox()"
            (keydown)="commitFromListbox()"
          >
            <!-- Runs of rows, as in tm-select: ungrouped options directly in
                 the listbox, a group's under its heading in role=group. -->
            @for (entry of entries(); track entry.key) {
              @if (entry.group; as group) {
                <li class="tm-option-group" role="group" [attr.aria-labelledby]="group.headingId">
                  <div class="tm-option-group__heading" [id]="group.headingId">
                    {{ group.label() }}
                  </div>
                  <ul class="tm-option-group__options" role="none">
                    @for (option of entry.options; track option) {
                      <li
                        ngOption
                        #optionRow
                        class="tm-option__row"
                        [value]="keyOf(option.value())"
                        [label]="option.effectiveLabel()"
                        [disabled]="option.disabled()"
                      >
                        <span class="tm-option__content">
                          <ng-container [ngTemplateOutlet]="option.contentTemplate()" />
                        </span>
                        <span class="tm-option__box" aria-hidden="true">
                          <tm-icon class="tm-option__box-check" [svg]="checkIcon" />
                        </span>
                      </li>
                    }
                  </ul>
                </li>
              } @else {
                @for (option of entry.options; track option) {
                  <li
                    ngOption
                    #optionRow
                    class="tm-option__row"
                    [value]="keyOf(option.value())"
                    [label]="option.effectiveLabel()"
                    [disabled]="option.disabled()"
                  >
                    <span class="tm-option__content">
                      <ng-container [ngTemplateOutlet]="option.contentTemplate()" />
                    </span>
                    <span class="tm-option__box" aria-hidden="true">
                      <tm-icon class="tm-option__box-check" [svg]="checkIcon" />
                    </span>
                  </li>
                }
              }
            }
          </ul>
        </div>
      </ng-template>
    </ng-template>
  `,
  styleUrls: ['./tm-select.css', './tm-multi-select.css'],
  host: {
    class: 'tm-multi-select',
    // The accessible name and description live on the trigger; strip both
    // from the role-less host.
    '[attr.aria-label]': 'null',
    '[attr.aria-describedby]': 'null',
    // The shared select chrome keys its states on the tm-select modifiers.
    '[class.tm-select--open]': 'expanded()',
    '[class.tm-select--disabled]': 'disabled()',
    '[class.tm-select--invalid]': 'showsInvalid()',
    '[class.tm-select--md]': 'effectiveSize() === "md"',
    '[class.tm-select--sm]': 'effectiveSize() === "sm"',
    '[class.tm-select--lg]': 'effectiveSize() === "lg"',
  },
})
export class TmMultiSelect<T> extends TmSelectBase<T, readonly T[]> {
  /** The active formatting locale, which joins the trigger's labels. */
  private readonly locale = inject(TM_ACTIVE_LOCALE);

  /**
   * The selected domain values — THE source of truth. Always in option
   * order, never click order; values no option matches follow the matched
   * ones and are never pruned.
   */
  readonly value = model<readonly T[]>([]);
  /** Emits the new value whenever the user toggles options. */
  readonly selectionChange = output<readonly T[]>();

  /** The localized built-in placeholder ("Select options"). */
  protected readonly defaultPlaceholder = this.translate('multiSelect.placeholder');

  /** The keys already warned about (an unmatched value with no `displayWith`). */
  private readonly warnedKeys = new Set<unknown>();

  /** The trigger's list formatter: a short unit list in the active locale. */
  private readonly listFormat = computed(
    () => new Intl.ListFormat(this.locale(), { type: 'unit', style: 'short' }),
  );

  /** The selected values in display order: option order first, then the unmatched values. */
  private readonly orderedValues = computed<readonly T[]>(() => {
    const value = this.value();
    const options = this.options();
    const order = new Map<unknown, number>();
    options.forEach((option, index) => {
      const key = this.keyOf(option.value());
      if (!order.has(key)) {
        order.set(key, index);
      }
    });
    const position = (item: T) => order.get(this.keyOf(item)) ?? options.length;
    // A stable sort: unmatched values keep their relative order at the end.
    return [...value].sort((a, b) => position(a) - position(b));
  });

  /** The selected labels, in display order. */
  private readonly labels = computed<readonly string[]>(() =>
    this.orderedValues()
      .map((item) => this.displayLabel(item))
      .filter((label) => label !== ''),
  );

  /** Whether the trigger currently shows the placeholder (nothing is selected). */
  protected readonly showsPlaceholder = computed(() => this.value().length === 0);

  /** The trigger's one line: the joined labels, or the placeholder. */
  protected readonly triggerLabel = computed(() =>
    this.showsPlaceholder() ? this.effectivePlaceholder() : this.listFormat().format(this.labels()),
  );

  /** Every key the current value selects, unmatched ones included. */
  protected selectedKeys(): readonly unknown[] {
    return this.value().map((item) => this.keyOf(item));
  }

  /**
   * A selected value's trigger label: `displayWith`, else its option's
   * label, else `String(value)` — for a value no option matches, or whose
   * option has no `label` and has not rendered yet (its text is only read
   * from the open panel) — with a development warning once per value.
   */
  private displayLabel(item: T): string {
    if (this.displayWith() !== undefined) {
      return this.labelOf(item);
    }
    const option = this.optionOf(item);
    const label = option?.effectiveLabel() ?? '';
    if (label !== '') {
      return label;
    }
    const key = this.keyOf(item);
    if (isDevMode() && !this.warnedKeys.has(key)) {
      this.warnedKeys.add(key);
      console.warn(
        option === undefined
          ? `tm-multi-select: no option matches the value "${String(item)}", so it is labelled ` +
              'with String(value). Pass displayWith to label values the options do not cover.'
          : `tm-multi-select: the option for the value "${String(item)}" has no label, so the ` +
              'closed trigger labels it with String(value). Pass the option a label, or pass ' +
              'displayWith.',
      );
    }
    return String(item);
  }

  /**
   * Commits the user's toggles: called after every click and keydown on the
   * listbox, once aria has applied the interaction to its own value. The
   * new value lists the selected options in option order, then every value
   * no option matches (aria never sees those, so they can neither be
   * toggled nor lost). Nothing is written or emitted when the interaction
   * changed nothing (navigation, a disabled row, a typeahead space).
   */
  protected commitFromListbox(): void {
    if (this.disabled() || this.readonly()) {
      return;
    }
    const value = this.value();
    const options = this.options();
    const selected = new Set(this.listboxValue());
    // The value's own items, by key, so an already-selected value keeps the
    // caller's instance rather than being swapped for the option's.
    const current = new Map<unknown, T>(value.map((item) => [this.keyOf(item), item]));
    const declared = new Set<unknown>();
    const next: T[] = [];
    for (const option of options) {
      const key = this.keyOf(option.value());
      if (declared.has(key)) {
        continue;
      }
      declared.add(key);
      if (selected.has(key)) {
        next.push(current.has(key) ? current.get(key)! : option.value());
      }
    }
    for (const item of value) {
      if (!declared.has(this.keyOf(item))) {
        next.push(item);
      }
    }
    const unchanged =
      next.length === value.length && next.every((item, i) => Object.is(item, value[i]));
    if (!unchanged) {
      this.value.set(next);
      this.selectionChange.emit(next);
    }
  }
}
