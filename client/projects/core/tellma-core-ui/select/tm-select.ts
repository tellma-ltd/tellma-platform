// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, effect, model, output, type Signal } from '@angular/core';
import { Combobox, ComboboxPopup, ComboboxWidget } from '@angular/aria/combobox';
import { Listbox, Option } from '@angular/aria/listbox';
import { OverlayModule } from '@angular/cdk/overlay';

import type { TmCellEditor } from '@tellma/core-ui/contracts';
import { TM_FORM_FIELD_CONTROL } from '@tellma/core-ui/form-field';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmSpinner } from '@tellma/core-ui/spinner';

import { TmSelectBase } from './tm-select-base';

/**
 * Single-select dropdown: a custom `<div>` trigger composed with
 * `@angular/aria`'s combobox/listbox directives, panel positioned by CDK
 * Overlay via the shared anchored-overlay helper (`usePopover:'inline'` —
 * native top layer, escapes clipping; `matchWidth`; `[bottom-start,
 * top-start]` flip; `disableClose` so aria alone owns Esc;
 * `updatePosition()`-on-attach macrotask so flip measures the real
 * panel). The aria directives own keyboard nav, typeahead,
 * active-descendant and all aria-* wiring; `tm-select` owns the brand
 * chrome, the Signal Forms glue, the scalar↔array key bridge, and label
 * resolution.
 *
 * The projected `tm-option`s are data + content templates; the actual
 * `[ngOption]` rows render here inside the listbox (aria's DI does not
 * cross content projection). Options may sit under `tm-option-group`
 * headings; navigation and typeahead skip the headings.
 *
 * Value integrity: the `value` model is the single source of truth.
 * It is mirrored into aria's listbox as a `valueKey`-mapped stable key and
 * RE-APPLIED whenever the option set changes, defeating aria's
 * unmatched-value auto-prune; commits happen on ACTIVATION events only
 * (click/Enter/Space), never on `valueChange`, so a prune can never wipe
 * the form value or close the panel.
 *
 * Not an entity picker: in-memory/simple lists only.
 *
 * @tmGroup form-control
 * @tmA11yNotes Focus never leaves the trigger (active-descendant model);
 *   the portaled listbox is referenced via aria-controls; Esc closes the
 *   panel only (reverting the value on a second Esc is a grid-host concern).
 *   A printable character on the CLOSED trigger opens the panel and moves
 *   the active option to the first match (select-only combobox behavior) —
 *   typing never changes the value silently; committing stays explicit.
 *   Option groups render as `role="group"` named by their heading.
 */
@Component({
  selector: 'tm-select',
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
  providers: [{ provide: TM_FORM_FIELD_CONTROL, useExisting: TmSelect }],
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
        <div class="tm-select__panel">
          <ul
            ngListbox
            ngComboboxWidget
            #lb="ngListbox"
            class="tm-select__listbox"
            [tabindex]="-1"
            focusMode="activedescendant"
            selectionMode="explicit"
            [(value)]="listboxValue"
            [activeDescendant]="lb.activeDescendant()"
            (click)="onListboxClick($event)"
            (keydown.enter)="commitFromListbox()"
            (keydown.space)="onSpaceKey()"
          >
            <!-- Runs of rows: an ungrouped run draws its options directly in
                 the listbox, exactly as a select without groups always has;
                 a group wraps its run in role=group under a heading row. -->
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
                        <tm-icon class="tm-option__check" [svg]="checkIcon" />
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
                    <tm-icon class="tm-option__check" [svg]="checkIcon" />
                  </li>
                }
              }
            }
          </ul>
        </div>
      </ng-template>
    </ng-template>
  `,
  styleUrl: './tm-select.css',
  host: {
    class: 'tm-select',
    // The accessible name and description live on the trigger; strip both
    // from the role-less host.
    '[attr.aria-label]': 'null',
    '[attr.aria-describedby]': 'null',
    '[class.tm-select--open]': 'expanded()',
    '[class.tm-select--disabled]': 'disabled()',
    '[class.tm-select--invalid]': 'showsInvalid()',
    '[class.tm-select--md]': 'effectiveSize() === "md"',
    '[class.tm-select--sm]': 'effectiveSize() === "sm"',
    '[class.tm-select--lg]': 'effectiveSize() === "lg"',
  },
})
export class TmSelect<T>
  extends TmSelectBase<T, T | undefined>
  implements TmCellEditor<T | undefined>
{
  /** The selected domain value — THE source of truth. */
  readonly value = model<T | undefined>(undefined);
  /** Emits the committed value whenever the user activates an option. */
  readonly selectionChange = output<T>();

  /** Grid revert baseline (TmCellEditor): the value `cancel()` returns to. */
  private lastCommitted: T | undefined;
  /**
   * The value this select itself just wrote (activation commit or cancel),
   * so the baseline effect can tell its own echo from an EXTERNAL write —
   * only external writes (form resets, grid loads) and `commit()` move the
   * revert baseline. If the baseline followed activations too, it would
   * equal `value()` at every observable point and `cancel()` could never
   * revert anything.
   */
  private selfWrite: { readonly value: T | undefined } | null = null;

  /** The localized built-in placeholder ("Select an option"). */
  protected readonly defaultPlaceholder = this.translate('select.placeholder');

  /** Whether the trigger currently shows the placeholder (no value selected). */
  protected readonly showsPlaceholder = computed(
    () => this.value() === undefined || this.value() === null,
  );

  /** Trigger label chain: displayWith → matched option → placeholder. */
  protected readonly triggerLabel = computed(() => {
    const value = this.value();
    if (value === undefined || value === null) {
      return this.effectivePlaceholder();
    }
    const label = this.labelOf(value);
    return label !== '' ? label : this.effectivePlaceholder();
  });

  /** Registers with an enclosing grid cell and tracks the revert baseline. */
  constructor() {
    super();
    this.cellHost?.register(this);

    // External writes (form resets, grid loads) move the revert baseline;
    // the select's own writes (marked via `selfWrite`) do not.
    effect(() => {
      const value = this.value();
      const self = this.selfWrite;
      this.selfWrite = null;
      if (!self || !Object.is(self.value, value)) {
        this.lastCommitted = value;
      }
    });
  }

  /** The one key the current value selects, or none. */
  protected selectedKeys(): readonly unknown[] {
    const value = this.value();
    return value === undefined || value === null ? [] : [this.keyOf(value)];
  }

  // ---- Commit path: activation events ONLY, never valueChange ----
  /** Commits when an ENABLED option row is clicked; other clicks do nothing. */
  protected onListboxClick(event: MouseEvent): void {
    // Only option rows commit — panel padding/scrollbar clicks and group
    // headings don't close. A DISABLED row doesn't either: no commit, no
    // close, and no re-emit of the previous selection.
    const row = (event.target as Element).closest('[ngOption]');
    if (row && row.getAttribute('aria-disabled') !== 'true') {
      this.commitFromListbox();
    }
  }

  /** Space commits — unless it is part of an in-progress typeahead query. */
  protected onSpaceKey(): void {
    // While a typeahead query is active, aria routes space into the search
    // string (its own select-on-space binding goes dormant); the commit here
    // must go dormant with it, or typing "united arab" closes the panel at
    // the first space.
    if (this.isTyping()) {
      return;
    }
    this.commitFromListbox();
  }

  /** Commits the listbox's active selection into `value` and closes the panel. */
  protected commitFromListbox(): void {
    if (this.readonly()) {
      return;
    }
    // softDisabled (aria's default) keeps disabled options NAVIGABLE, so
    // Enter/Space can activate one — a no-op: the panel stays open and the
    // previous selection is not re-emitted.
    if (this.activeOptionDisabled()) {
      return;
    }
    const keys = this.listboxValue();
    if (keys.length > 0) {
      const key = keys[0];
      const option = this.options().find((o) => this.keyOf(o.value()) === key);
      if (option && !option.disabled()) {
        const newValue = option.value();
        this.selfWrite = { value: newValue };
        this.value.set(newValue);
        this.selectionChange.emit(newValue);
      }
    }
    // Activation fires whether or not the value changed: same-value
    // reselection closes with no special case.
    this.setExpanded(false);
  }

  // ---- TmCellEditor<T | undefined> ----
  /**
   * The committed-text view of the selection: the resolved label of the
   * current value (`displayWith` first, else the matching option's label),
   * or the empty string while no value is selected. Never `null` — a
   * select's content is always representable.
   */
  readonly text: Signal<string | null> = computed(() => {
    const value = this.value();
    return value === undefined || value === null ? '' : this.labelOf(value);
  });

  /** Accepts the current value as the revert baseline and closes the panel. */
  commit(): void {
    this.lastCommitted = this.value();
    this.setExpanded(false);
  }

  /**
   * Reverts to the last committed value — for a GRID HOST to call on its
   * second Esc; the standalone control's own Esc only closes the panel
   * and never reaches here.
   */
  cancel(): void {
    this.selfWrite = { value: this.lastCommitted };
    this.value.set(this.lastCommitted);
    this.setExpanded(false);
  }

  /**
   * Type-to-edit seed: opens the panel and feeds the text into the
   * typeahead, moving the active option to the first match — the same
   * behavior as typing that text on the closed trigger. Committing stays an
   * explicit activation; seeding never changes the value.
   */
  seed(text: string): void {
    if (this.disabled() || this.readonly()) {
      return;
    }
    if (text !== '' && !text.startsWith(' ')) {
      this.ɵholdTypeaheadText(text);
    }
    this.focus();
    this.setExpanded(true);
  }
}
