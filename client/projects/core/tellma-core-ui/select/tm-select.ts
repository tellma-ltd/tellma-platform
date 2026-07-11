// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgTemplateOutlet } from '@angular/common';
import {
  afterRenderEffect,
  booleanAttribute,
  Component,
  computed,
  contentChildren,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  input,
  model,
  output,
  signal,
  untracked,
  viewChild,
  viewChildren,
} from '@angular/core';
import { Combobox, ComboboxPopup, ComboboxWidget } from '@angular/aria/combobox';
import { Listbox, Option } from '@angular/aria/listbox';
import { CdkConnectedOverlay, OverlayModule } from '@angular/cdk/overlay';
import type { ConnectedPosition } from '@angular/cdk/overlay';
import type { ValidationError } from '@angular/forms/signals';

import type { TmCellEditor, TmFieldError, TmFormFieldControl } from '@tellma/core-ui/contracts';
import {
  TM_ERROR_DISPLAY,
  TM_FORM_FIELD_DEFAULTS,
  TM_UI_TRANSLATE,
  tmResolveFieldErrors,
  TmSpinner,
} from '@tellma/core-ui';
import { TM_FORM_FIELD_CONTROL } from '@tellma/core-ui/form-field';

import { TmOption } from './tm-option';

let nextUniqueId = 0;

/**
 * Single-select dropdown: a custom `<div>` trigger composed with
 * `@angular/aria`'s combobox/listbox directives, panel positioned by CDK
 * Overlay (`usePopover:'inline'` — native top layer, escapes clipping;
 * `matchWidth`; `[bottom-start, top-start]` flip; `disableClose` so aria
 * alone owns Esc; `updatePosition()`-on-attach macrotask so flip measures
 * the real panel). The aria directives own keyboard nav, typeahead,
 * active-descendant and all aria-* wiring; `tm-select` owns the brand
 * chrome, the Signal Forms glue, the scalar↔array key bridge, and label
 * resolution.
 *
 * The projected `tm-option`s are data + content templates; the actual
 * `[ngOption]` rows render here inside the listbox (aria's DI does not
 * cross content projection).
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
      [attr.aria-describedby]="ariaDescribedBy()"
      [attr.aria-invalid]="showsInvalid() ? 'true' : null"
      [attr.aria-busy]="pending() ? 'true' : null"
      [attr.aria-required]="required() ? 'true' : null"
      (blur)="touch.emit()"
    >
      <span class="tm-select__value" [class.tm-select__value--placeholder]="showsPlaceholder()">
        {{ triggerLabel() }}
      </span>
      @if (pending()) {
        <tm-spinner class="tm-select__spinner" />
      }
      <svg class="tm-select__caret" viewBox="0 0 16 16" fill="none" aria-hidden="true">
        <polyline
          points="4,6 8,10 12,6"
          stroke="currentColor"
          stroke-width="1.5"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
      </svg>
    </div>

    <ng-template
      [cdkConnectedOverlay]="{
        origin: cb.element,
        usePopover: 'inline',
        matchWidth: true,
        disableClose: true,
        positions: positions,
      }"
      [cdkConnectedOverlayOpen]="expanded()"
      (attach)="onOverlayAttach()"
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
            (keydown.space)="commitFromListbox()"
          >
            @for (option of options(); track option) {
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
                <svg class="tm-option__check" viewBox="0 0 16 16" fill="none" aria-hidden="true">
                  <polyline
                    points="3.5,8.5 6.5,11.5 12.5,4.5"
                    stroke="currentColor"
                    stroke-width="2"
                    stroke-linecap="round"
                    stroke-linejoin="round"
                  />
                </svg>
              </li>
            }
          </ul>
        </div>
      </ng-template>
    </ng-template>
  `,
  styleUrl: './tm-select.css',
  host: {
    class: 'tm-select',
    // The accessible name lives on the trigger; strip it from the host.
    '[attr.aria-label]': 'null',
    '[class.tm-select--open]': 'expanded()',
    '[class.tm-select--disabled]': 'disabled()',
    '[class.tm-select--invalid]': 'showsInvalid()',
    '[class.tm-select--sm]': 'size() === "sm"',
    '[class.tm-select--lg]': 'size() === "lg"',
  },
})
export class TmSelect<T> implements TmFormFieldControl, TmCellEditor<T | undefined> {
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly errorDisplay = inject(TM_ERROR_DISPLAY);
  private readonly defaults = inject(TM_FORM_FIELD_DEFAULTS);

  // ---- FormValueControl<T | undefined> + optional state inputs (§5) ----
  /** The selected domain value — THE source of truth. */
  readonly value = model<T | undefined>(undefined);
  /** Non-form usage only — the bound field is authoritative when bound via [formField]. */
  readonly disabled = input(false, { transform: booleanAttribute });
  /** Readonly state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly readonly = input(false, { transform: booleanAttribute });
  /** Required state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly required = input(false, { transform: booleanAttribute });
  /** Validity state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly invalid = input(false, { transform: booleanAttribute });
  /** Touched state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly touched = input(false, { transform: booleanAttribute });
  /** Dirty state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly dirty = input(false, { transform: booleanAttribute });
  /** Async-validation-pending state — the bound field is authoritative when bound via [formField]. */
  readonly pending = input(false, { transform: booleanAttribute });
  /** The raw framework errors, bound by [formField] and localized into `localizedErrors`. */
  readonly errors = input<readonly ValidationError.WithOptionalFieldTree[]>([]);
  /** Emits when the trigger blurs — touch reporting for the bound field. */
  readonly touch = output<void>();

  // ---- Own API (§3.4) ----
  /**
   * Maps a domain value to a STABLE primitive key (aria selects with `===`).
   * Unneeded for primitive values.
   */
  readonly valueKey = input<((value: T) => string | number) | undefined>(undefined);
  /**
   * Resolves the trigger label without a materialized option — required in
   * practice for prepopulated/async lists.
   */
  readonly displayWith = input<((value: T) => string) | undefined>(undefined);
  /** Trigger text while no value is selected; defaults to the localized built-in placeholder. */
  readonly placeholder = input<string | undefined>(undefined);
  /** Accessible name for a select used WITHOUT tm-form-field. */
  readonly ariaLabel = input<string | null>(null, { alias: 'aria-label' });
  /** Height/density variant; defaults to the workspace-wide form-field default. */
  readonly size = input<'sm' | 'md' | 'lg'>(this.defaults.size);
  /** Emits the committed value whenever the user activates an option. */
  readonly selectionChange = output<T>();
  /** Emits when the options panel opens. */
  readonly opened = output<void>();
  /** Emits when the options panel closes. */
  readonly closed = output<void>();

  /** Stable generated id of the trigger — the target for the field's aria wiring. */
  readonly controlId = signal(`tm-select-${nextUniqueId++}`).asReadonly();

  /** Whether the options panel is open. */
  protected readonly expanded = signal(false);
  /** aria's listbox value — an ARRAY of keys; never the source of truth. */
  protected readonly listboxValue = signal<unknown[]>([]);
  /** The projected `tm-option` children. */
  protected readonly options = contentChildren(TmOption<T>);

  /** Overlay positions: below the trigger, flipping above when space runs out. */
  protected readonly positions: ConnectedPosition[] = [
    { originX: 'start', originY: 'bottom', overlayX: 'start', overlayY: 'top' },
    { originX: 'start', originY: 'top', overlayX: 'start', overlayY: 'bottom' },
  ];

  private readonly combobox = viewChild.required(Combobox);
  private readonly overlay = viewChild(CdkConnectedOverlay);
  private readonly listbox = viewChild(Listbox);
  private readonly optionRows = viewChildren('optionRow', { read: ElementRef });

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

  // ---- TmFormFieldControl (§2.1) ----
  /** Renders its own trigger chrome; the field adds only label/hint/error. */
  readonly ownsChrome = true;
  private readonly fieldDescribedBy = signal<readonly string[]>([]);
  /** The hint/error ids the enclosing field pushed via `setDescribedByIds`. */
  readonly describedByIds = this.fieldDescribedBy.asReadonly();
  private readonly labelIdFromField = signal<string | null>(null);
  /** Already-localized error messages resolved from `errors` — read by the enclosing field. */
  readonly localizedErrors: () => readonly TmFieldError[] = tmResolveFieldErrors(
    this.errors,
    this.translate,
  );

  /** The merged aria-describedby attribute value, or null when no ids apply. */
  protected readonly ariaDescribedBy = computed(() => this.describedByIds().join(' ') || null);
  /** The field-provided label id, bound as aria-labelledby on the trigger. */
  protected readonly ariaLabelledBy = computed(() => this.labelIdFromField());

  /** Whether invalidity is surfaced (aria-invalid) — follows the error-display policy. */
  protected readonly showsInvalid = computed(() =>
    this.errorDisplay({
      invalid: this.invalid(),
      touched: this.touched(),
      dirty: this.dirty(),
      pending: this.pending(),
    }),
  );

  /** Domain value → the stable primitive key aria selects on. */
  keyOf(value: T): unknown {
    if (value === undefined || value === null) {
      return value;
    }
    const keyFn = this.valueKey();
    return keyFn ? keyFn(value) : value;
  }

  private readonly defaultPlaceholder = this.translate('select.placeholder');
  /** The placeholder in effect: the `placeholder` input, or the localized default. */
  protected readonly effectivePlaceholder = computed(
    () => this.placeholder() ?? this.defaultPlaceholder(),
  );

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
    const displayWith = this.displayWith();
    if (displayWith) {
      return displayWith(value);
    }
    const key = this.keyOf(value);
    const option = this.options().find((o) => this.keyOf(o.value()) === key);
    const label = option?.effectiveLabel() ?? '';
    return label !== '' ? label : this.effectivePlaceholder();
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.pendingRemeasure));

    // The ONE-DIRECTIONAL value bridge (§3.4): mirror the model into aria's
    // listbox (as the stable key), re-applied whenever the option set
    // changes — aria's afterRenderEffect prunes any selected key without a
    // rendered option, and by re-asserting here a prepopulated value
    // SURVIVES async option arrival. Reading listboxValue also re-asserts
    // right after a prune. Never treat aria's value as authoritative.
    effect(() => {
      const value = this.value();
      this.options(); // re-apply on option turnover
      const current = this.listboxValue();
      const desired = value === undefined || value === null ? [] : [this.keyOf(value)];
      if (current.length !== desired.length || current[0] !== desired[0]) {
        this.listboxValue.set(desired);
      }
    });

    // Panel lifecycle outputs.
    let wasExpanded = false;
    effect(() => {
      const isExpanded = this.expanded();
      if (isExpanded !== wasExpanded) {
        wasExpanded = isExpanded;
        if (isExpanded) {
          this.opened.emit();
        } else {
          this.closed.emit();
        }
      }
    });

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

    // Typeahead textContent fallback (§3.4): after the rows render, feed
    // each label-less option its rendered text.
    afterRenderEffect(() => {
      const rows = this.optionRows();
      const options = this.options();
      for (let i = 0; i < rows.length && i < options.length; i++) {
        if (untracked(() => options[i].label()) === undefined) {
          const text =
            (rows[i].nativeElement as HTMLElement)
              .querySelector('.tm-option__content')
              ?.textContent?.trim() ?? '';
          options[i].derivedText.set(text);
        }
      }
      this.listbox()?.scrollActiveItemIntoView();
    });
  }

  // ---- Overlay plumbing (§3.4, proven by the stage-3 spike) ----
  private pendingRemeasure: ReturnType<typeof setTimeout> | undefined;

  /** Re-measures the overlay position one macrotask after attach, so flip-up can work. */
  protected onOverlayAttach(): void {
    // DeferredContent inserts the panel one render pass after CDK attaches
    // and measures; without a MACROTASK re-measure, flip-up would measure a
    // zero-height panel and never flip (spike-verified). The timer must not
    // outlive the component — updatePosition() on a disposed overlay throws
    // — so destroy clears it (registered once in the constructor).
    clearTimeout(this.pendingRemeasure);
    this.pendingRemeasure = setTimeout(() => this.overlay()?.overlayRef?.updatePosition());
  }

  // ---- Commit path: activation events ONLY, never valueChange (§3.4) ----
  /** Commits when an option row is clicked; panel padding/scrollbar clicks do nothing. */
  protected onListboxClick(event: MouseEvent): void {
    // Only option rows commit — panel padding/scrollbar clicks don't close.
    if ((event.target as Element).closest('[ngOption]')) {
      this.commitFromListbox();
    }
  }

  /** Commits the listbox's active selection into `value` and closes the panel. */
  protected commitFromListbox(): void {
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
    // reselection closes with no special case (§3.4).
    this.expanded.set(false);
  }

  // ---- TmFormFieldControl plumbing ----
  /** Receives the field's hint/error ids and exposes them via aria-describedby. */
  setDescribedByIds(ids: readonly string[]): void {
    this.fieldDescribedBy.set(ids);
  }

  /** The <div> trigger is not labelable — the field hands us its label id. */
  setLabelId(id: string | null): void {
    this.labelIdFromField.set(id);
  }

  /** Focuses the trigger when the user clicks the field's container chrome. */
  onContainerClick(): void {
    this.focus();
  }

  // ---- TmCellEditor<T | undefined> (DRAFT — §9; hardened with the grid) ----
  /** Accepts the current value as the revert baseline and closes the panel. */
  commit(): void {
    this.lastCommitted = this.value();
    this.expanded.set(false);
  }

  /**
   * Reverts to the last committed value — for a GRID HOST to call on its
   * second Esc; the standalone control's own Esc only closes the panel
   * and never reaches here.
   */
  cancel(): void {
    this.selfWrite = { value: this.lastCommitted };
    this.value.set(this.lastCommitted);
    this.expanded.set(false);
  }

  /** Focuses the trigger; Signal Forms calls this when asked to focus the field. */
  focus(options?: FocusOptions): void {
    untracked(() => this.combobox()).element.focus(options);
  }

  /** Grid-editor seam: hosts forward keys; the trigger's own listeners consume them. */
  onKeydown(event: KeyboardEvent): void {
    // Draft grid seam: the host forwards keys; aria's own host listener on
    // the trigger handles everything this control consumes. Nothing to do
    // standalone.
    void event;
  }
}
