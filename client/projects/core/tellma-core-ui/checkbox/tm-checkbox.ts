// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  Component,
  computed,
  ElementRef,
  inject,
  input,
  model,
  output,
  type Signal,
  signal,
  viewChild,
} from '@angular/core';
import type { ValidationError } from '@angular/forms/signals';

import type { TmFieldError, TmFormFieldControl } from '@tellma/core-ui/contracts';
import { TM_ERROR_DISPLAY, TM_UI_TRANSLATE, tmResolveFieldErrors } from '@tellma/core-ui';
import { TM_FORM_FIELD_CONTROL } from '@tellma/core-ui/form-field';

let nextUniqueId = 0;

/**
 * Boolean / tri-state checkbox: a visually-hidden NATIVE
 * `<input type="checkbox">` carries the semantics; the styled 18px box +
 * check/indeterminate glyphs are chrome. The value channel is `checked`
 * (FormCheckboxControl) — deliberately NO `value` property (enforced by the
 * API golden).
 *
 * Tri-state: `indeterminate` host-binds the native `.indeterminate` IDL
 * property — the browser exposes `checked="mixed"` in the accessibility
 * tree itself; no manual aria-checked. Independent of `checked`; a
 * user toggle clears it, matching native behavior.
 *
 * Touch target: the clickable region is the whole label row, padded past
 * the 24px minimum (WCAG 2.2 AA 2.5.8) while the box renders at the brand
 * 18px; a bare checkbox expands its hit box via a transparent overlay.
 *
 * @tmGroup form-control
 * @tmA11yNotes Native checkbox semantics; space toggles; label click
 *   toggles; indeterminate surfaces as checked="mixed" automatically.
 */
@Component({
  selector: 'tm-checkbox',
  providers: [{ provide: TM_FORM_FIELD_CONTROL, useExisting: TmCheckbox }],
  template: `
    <label class="tm-checkbox__layout">
      <span class="tm-checkbox__box-wrap">
        <input
          #native
          class="tm-checkbox__native"
          type="checkbox"
          [id]="controlId()"
          [checked]="checked()"
          [indeterminate]="indeterminate()"
          [disabled]="disabled()"
          [required]="required()"
          [attr.aria-label]="ariaLabel()"
          [attr.aria-invalid]="showsInvalid() ? 'true' : null"
          [attr.aria-describedby]="describedByAttr()"
          [attr.aria-busy]="pending() ? 'true' : null"
          (change)="onNativeChange($event)"
          (blur)="touch.emit()"
        />
        <span class="tm-checkbox__box" aria-hidden="true">
          <svg class="tm-checkbox__glyph" viewBox="0 0 16 16" fill="none">
            @if (indeterminate()) {
              <path
                class="tm-checkbox__mark"
                d="M4 8h8"
                stroke="currentColor"
                stroke-width="2"
                stroke-linecap="round"
              />
            } @else {
              <polyline
                class="tm-checkbox__mark"
                points="3.5,8.5 6.5,11.5 12.5,4.5"
                stroke="currentColor"
                stroke-width="2"
                stroke-linecap="round"
                stroke-linejoin="round"
                fill="none"
              />
            }
          </svg>
        </span>
      </span>
      <span class="tm-checkbox__label"><ng-content /></span>
    </label>
  `,
  styleUrl: './tm-checkbox.css',
  host: {
    class: 'tm-checkbox',
    '[class.tm-checkbox--checked]': 'checked()',
    '[class.tm-checkbox--indeterminate]': 'indeterminate()',
    '[class.tm-checkbox--disabled]': 'disabled()',
    // The accessible name and description live on the NATIVE input; a
    // role-less custom element must not carry aria-label/-describedby.
    '[attr.aria-label]': 'null',
    '[attr.aria-describedby]': 'null',
  },
})
export class TmCheckbox implements TmFormFieldControl {
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly errorDisplay = inject(TM_ERROR_DISPLAY);

  // ---- FormCheckboxControl + optional state inputs (§5). NO `value`. ----
  /** The checkbox state (the FormCheckboxControl model). */
  readonly checked = model(false);
  /**
   * Tri-state "mixed": independent of `checked`; drives the native
   * `.indeterminate` IDL property; cleared by a user toggle.
   */
  readonly indeterminate = model(false);
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
  /** Emits when the native input blurs — touch reporting for the bound field. */
  readonly touch = output<void>();

  /** Accessible name for a LABEL-LESS checkbox, forwarded to the native input. */
  readonly ariaLabel = input<string | null>(null, { alias: 'aria-label' });
  /**
   * Author-supplied describedby ids (space-separated), relocated to the
   * NATIVE input and preserved — the enclosing field's hint/error ids are
   * merged AFTER them, never over them.
   */
  readonly ariaDescribedby = input<string | null>(null, { alias: 'aria-describedby' });

  /** Stable generated id of the native input — the `<label for>` and aria wiring target. */
  readonly controlId = signal(`tm-checkbox-${nextUniqueId++}`).asReadonly();

  private readonly native = viewChild.required<ElementRef<HTMLInputElement>>('native');

  // ---- TmFormFieldControl (§2.1) ----
  /** Renders its own box chrome; the field adds only label/hint/error. */
  readonly ownsChrome = true;
  private readonly fieldDescribedBy = signal<readonly string[]>([]);
  /** Every exposed describedby id: author-supplied first, then the field's hint/error ids. */
  readonly describedByIds: Signal<readonly string[]> = computed(() => [
    ...(this.ariaDescribedby()?.split(/\s+/).filter(Boolean) ?? []),
    ...this.fieldDescribedBy(),
  ]);
  /** Already-localized error messages resolved from `errors` — read by the enclosing field. */
  readonly localizedErrors: () => readonly TmFieldError[] = tmResolveFieldErrors(
    this.errors,
    this.translate,
  );

  /** The merged aria-describedby attribute value, or null when no ids apply. */
  protected readonly describedByAttr = computed(() => this.describedByIds().join(' ') || null);

  /** Whether invalidity is surfaced (aria-invalid) — follows the error-display policy. */
  protected readonly showsInvalid = computed(() =>
    this.errorDisplay({
      invalid: this.invalid(),
      touched: this.touched(),
      dirty: this.dirty(),
      pending: this.pending(),
    }),
  );

  /** Receives the field's hint/error ids and exposes them via aria-describedby. */
  setDescribedByIds(ids: readonly string[]): void {
    this.fieldDescribedBy.set(ids);
  }

  /** Focuses the checkbox when the user clicks the field's container chrome. */
  onContainerClick(): void {
    this.focus();
  }

  /** Focuses the native input; Signal Forms calls this when asked to focus the field. */
  focus(options?: FocusOptions): void {
    this.native().nativeElement.focus(options);
  }

  /** Native change handler: reverts when readonly, else updates `checked` and clears mixed. */
  protected onNativeChange(event: Event): void {
    if (this.readonly()) {
      // Native checkboxes have no readonly; revert the toggle. Activation
      // also cleared the .indeterminate IDL property, and the binding won't
      // re-fire (the signal never changed) — restore it too, or AT reports
      // "not checked" while the glyph still shows mixed.
      const native = event.target as HTMLInputElement;
      native.checked = this.checked();
      native.indeterminate = this.indeterminate();
      return;
    }
    this.checked.set((event.target as HTMLInputElement).checked);
    // A user toggle clears the mixed state (native behavior, §3.3).
    this.indeterminate.set(false);
  }
}
