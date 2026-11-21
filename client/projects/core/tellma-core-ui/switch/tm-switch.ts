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
  signal,
  viewChild,
  type Signal,
} from '@angular/core';
import type { ValidationError } from '@angular/forms/signals';

import type { TmFieldError, TmFormFieldControl } from '@tellma/core-ui/contracts';
import { TM_ERROR_DISPLAY, TM_UI_TRANSLATE, tmResolveFieldErrors } from '@tellma/core-ui';
import { TM_FORM_FIELD_CONTROL } from '@tellma/core-ui/form-field';

let nextUniqueId = 0;

/**
 * An on/off setting that takes effect as a setting does: a visually hidden
 * NATIVE `<input type="checkbox" role="switch">` carries the semantics; a
 * 28×16 px track with a 12 px knob, in the primary colour when on, is the
 * chrome. The projected content is the label, and the whole label row is
 * the pointer target.
 *
 * Signal Forms native: implements `FormCheckboxControl` (a `checked` model
 * and no `value`) plus the optional state inputs `[formField]` binds;
 * reports touch on blur. A readonly switch takes focus but never toggles.
 *
 * @tmGroup form-control
 * @tmA11yNotes A native checkbox with role="switch" (announced as on or
 *   off); Space toggles; a click anywhere on the label row toggles.
 */
@Component({
  selector: 'tm-switch',
  providers: [{ provide: TM_FORM_FIELD_CONTROL, useExisting: TmSwitch }],
  template: `
    <label class="tm-switch__layout">
      <span class="tm-switch__track-wrap">
        <input
          #native
          class="tm-switch__native"
          type="checkbox"
          role="switch"
          [id]="controlId()"
          [checked]="checked()"
          [disabled]="disabled()"
          [required]="required()"
          [attr.aria-label]="ariaLabel()"
          [attr.aria-readonly]="readonly() ? 'true' : null"
          [attr.aria-invalid]="showsInvalid() ? 'true' : null"
          [attr.aria-describedby]="describedByAttr()"
          [attr.aria-busy]="pending() ? 'true' : null"
          (change)="onNativeChange($event)"
          (blur)="touch.emit()"
        />
        <!-- The track and its knob: chrome only; the input is the switch. -->
        <span class="tm-switch__track" aria-hidden="true">
          <span class="tm-switch__knob"></span>
        </span>
      </span>
      <span class="tm-switch__label"><ng-content /></span>
    </label>
  `,
  styleUrl: './tm-switch.css',
  host: {
    class: 'tm-switch',
    '[class.tm-switch--checked]': 'checked()',
    '[class.tm-switch--disabled]': 'disabled()',
    '[class.tm-switch--label-start]': 'labelPosition() === "start"',
    '[class.tm-switch--invalid]': 'showsInvalid()',
    // The name and description live on the native input; a role-less
    // custom element must not carry them.
    '[attr.aria-label]': 'null',
    '[attr.aria-describedby]': 'null',
  },
})
export class TmSwitch implements TmFormFieldControl {
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly errorDisplay = inject(TM_ERROR_DISPLAY);

  // ---- FormCheckboxControl + the optional state inputs. NO `value`. ----
  /** Whether the switch is on (the FormCheckboxControl model). */
  readonly checked = model(false);
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

  // ---- Own API ----
  /** The label's side of the track: `end` (the default) or `start`. */
  readonly labelPosition = input<'end' | 'start'>('end');
  /** Accessible name for a switch WITHOUT projected label text. */
  readonly ariaLabel = input<string | null>(null, { alias: 'aria-label' });
  /**
   * Author-supplied describedby ids (space-separated), relocated to the
   * native input and preserved — the enclosing field's hint/error ids are
   * merged after them.
   */
  readonly ariaDescribedby = input<string | null>(null, { alias: 'aria-describedby' });

  /** Stable generated id of the native input — the `<label for>` and aria wiring target. */
  readonly controlId = signal(`tm-switch-${nextUniqueId++}`).asReadonly();

  private readonly native = viewChild.required<ElementRef<HTMLInputElement>>('native');

  // ---- TmFormFieldControl ----
  /** Renders its own track; an enclosing field adds only its label, hint and error. */
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
  /** Whether the enclosing field displays an error this control must mark (see `setFieldError`). */
  private readonly fieldError = signal(false);
  /** Whether invalidity is surfaced — the display policy, or the enclosing field's displayed error. */
  protected readonly showsInvalid = computed(
    () =>
      this.fieldError() ||
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

  /** Receives whether the enclosing field displays an error (its plain `error` input included). */
  setFieldError(showsError: boolean): void {
    this.fieldError.set(showsError);
  }

  /** Focuses the switch when the user clicks the field's container chrome. */
  onContainerClick(): void {
    this.focus();
  }

  /** Focuses the native input; Signal Forms calls this when asked to focus the field. */
  focus(options?: FocusOptions): void {
    this.native().nativeElement.focus(options);
  }

  /** Native change: reverted while readonly (a checkbox has no readonly), else written to `checked`. */
  protected onNativeChange(event: Event): void {
    const native = event.target as HTMLInputElement;
    if (this.readonly()) {
      native.checked = this.checked();
      return;
    }
    this.checked.set(native.checked);
  }
}
