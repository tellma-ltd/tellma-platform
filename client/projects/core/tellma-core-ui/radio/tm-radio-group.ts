// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  Component,
  computed,
  contentChildren,
  ElementRef,
  inject,
  input,
  model,
  output,
  signal,
  untracked,
  viewChildren,
  type Signal,
} from '@angular/core';
import type { ValidationError } from '@angular/forms/signals';

import type { TmFieldError, TmFormFieldControl } from '@tellma/core-ui/contracts';
import {
  TM_ERROR_DISPLAY,
  TM_UI_TRANSLATE,
  ɵtmControlSize,
  tmResolveFieldErrors,
  type TmControlSize,
} from '@tellma/core-ui';
import { TM_FORM_FIELD_CONTROL } from '@tellma/core-ui/form-field';
import { ɵtmRadioArrowTarget } from '@tellma/core-ui/private';

import { TmRadio } from './tm-radio';

let nextUniqueId = 0;

/**
 * Picks one value from a visible list: a `role="radiogroup"` of native
 * radios sharing one generated `name`, declared as `tm-radio` children.
 * Rows sit at the option-row height of the resolved size (26, 30 or 34 px).
 *
 * Tab enters the group at the checked radio, else at the first enabled
 * one. Up/Down and Left/Right (Left/Right follow the live direction) check
 * and focus the previous or next enabled radio, wrapping; Space checks.
 * The arrows are handled here rather than left to the engine, which maps
 * them by direction in Chromium only. A readonly group lets focus move but
 * never changes the value.
 *
 * Name the group with `ariaLabel`, or place it in a `tm-form-field`, whose
 * label then names it. Signal Forms native: implements
 * `FormValueControl<T | null>`; reports touch when focus leaves the group.
 *
 * @tmGroup form-control
 * @tmA11yNotes APG radio group over native radios: one tab stop; arrows
 *   move and check, wrapping, Left/Right mapped in RTL; Space checks; a
 *   radio's hint is its description.
 */
@Component({
  selector: 'tm-radio-group',
  providers: [{ provide: TM_FORM_FIELD_CONTROL, useExisting: TmRadioGroup }],
  template: `
    <div
      class="tm-radio-group__list"
      role="radiogroup"
      [id]="controlId()"
      [attr.aria-label]="ariaLabel() ?? null"
      [attr.aria-labelledby]="ariaLabel() === undefined ? labelId() : null"
      [attr.aria-describedby]="describedByAttr()"
      [attr.aria-required]="required() ? 'true' : null"
      [attr.aria-readonly]="readonly() ? 'true' : null"
      [attr.aria-disabled]="disabled() ? 'true' : null"
      [attr.aria-invalid]="showsInvalid() ? 'true' : null"
      [attr.aria-busy]="pending() ? 'true' : null"
      (keydown)="onKeydown($event)"
      (focusout)="onFocusOut($event)"
    >
      @for (radio of radios(); track radio; let index = $index) {
        <!-- One row: the native radio stretched over its circle, then the
             label and the optional hint. The whole row is the target. -->
        <label
          class="tm-radio-group__row"
          [class.tm-radio-group__row--disabled]="disabled() || radio.disabled()"
        >
          <span class="tm-radio-group__control">
            <input
              #native
              class="tm-radio-group__native"
              type="radio"
              [name]="name"
              [checked]="isChecked(radio)"
              [disabled]="disabled() || radio.disabled()"
              [tabIndex]="tabStop() === radio ? 0 : -1"
              [attr.aria-describedby]="radio.hint() ? hintId(index) : null"
              (change)="onNativeChange(radio)"
            />
            <span class="tm-radio-group__circle" aria-hidden="true"></span>
          </span>
          <span class="tm-radio-group__text">
            <span class="tm-radio-group__label">{{ radio.label() }}</span>
            @if (radio.hint(); as hint) {
              <span class="tm-radio-group__hint" [id]="hintId(index)">{{ hint }}</span>
            }
          </span>
        </label>
      }
    </div>
  `,
  styleUrl: './tm-radio-group.css',
  host: {
    class: 'tm-radio-group',
    '[class.tm-radio-group--horizontal]': 'orientation() === "horizontal"',
    '[class.tm-radio-group--disabled]': 'disabled()',
    '[class.tm-radio-group--invalid]': 'showsInvalid()',
    '[class.tm-radio-group--sm]': 'resolvedSize() === "sm"',
    '[class.tm-radio-group--lg]': 'resolvedSize() === "lg"',
  },
})
export class TmRadioGroup<T> implements TmFormFieldControl {
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly errorDisplay = inject(TM_ERROR_DISPLAY);
  private readonly hostElement = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly uniqueId = nextUniqueId++;

  // ---- FormValueControl<T | null> + the optional state inputs ----
  /** The checked radio's value; `null` while none is checked. */
  readonly value = model<T | null>(null);
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
  /** Emits when focus leaves the group — touch reporting for the bound field. */
  readonly touch = output<void>();

  // ---- Own API ----
  /** The size step of the rows; unset, the workspace default (the touch size on a phone). */
  readonly size = input<TmControlSize | undefined>(undefined);
  /** How the rows run: a column (the default) or a wrapping line. */
  readonly orientation = input<'vertical' | 'horizontal'>('vertical');
  /** The group's accessible name; unset, the enclosing `tm-form-field`'s label names it. */
  readonly ariaLabel = input<string | undefined>(undefined);
  /**
   * Author-supplied describedby ids (space-separated). Preserved — the
   * enclosing field's hint/error ids are merged after them.
   */
  readonly ariaDescribedby = input<string | null>(null, { alias: 'aria-describedby' });

  /** Stable generated id of the radiogroup element. */
  readonly controlId = signal(`tm-radio-group-${this.uniqueId}`).asReadonly();
  /** The `name` the group's native radios share. */
  protected readonly name = `tm-radio-group-${this.uniqueId}`;
  /** The size the rows render at. */
  protected readonly resolvedSize = ɵtmControlSize(this.size);

  /** The declared radios, in order. */
  protected readonly radios = contentChildren<TmRadio<T>>(TmRadio);
  private readonly natives = viewChildren<ElementRef<HTMLInputElement>>('native');

  /** The radio that holds the group's one tab stop: the checked one, else the first enabled one. */
  protected readonly tabStop = computed(() => {
    const radios = this.radios();
    const enabled = (radio: TmRadio<T>) => !radio.disabled();
    return (
      radios.find((radio) => enabled(radio) && this.isChecked(radio)) ??
      radios.find(enabled) ??
      null
    );
  });

  // ---- TmFormFieldControl ----
  /** Renders its own rows; an enclosing field adds only its label, hint and error. */
  readonly ownsChrome = true;
  /** The enclosing field's label id, which names the group (a div is not labelable). */
  protected readonly labelId = signal<string | null>(null);
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
  /** Whether the enclosing field displays an error this control must mark. */
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

  /** Receives the enclosing field's label id: the group's name when `ariaLabel` is unset. */
  setLabelId(id: string | null): void {
    this.labelId.set(id);
  }

  /** Receives whether the enclosing field displays an error (its plain `error` input included). */
  setFieldError(showsError: boolean): void {
    this.fieldError.set(showsError);
  }

  /** Focuses the group's tab stop when the user clicks the field's label or chrome. */
  onContainerClick(): void {
    this.focus();
  }

  /** Focuses the radio holding the tab stop; Signal Forms calls this when asked to focus the field. */
  focus(options?: FocusOptions): void {
    const stop = untracked(this.tabStop);
    const index = stop === null ? -1 : untracked(this.radios).indexOf(stop);
    untracked(this.natives)[index]?.nativeElement.focus(options);
  }

  /** Whether a radio's value is the group's value. */
  protected isChecked(radio: TmRadio<T>): boolean {
    return Object.is(radio.value(), this.value());
  }

  /** The element id of a radio's hint line. */
  protected hintId(index: number): string {
    return `${this.name}-hint-${index}`;
  }

  /** A native check (pointer, Space): written to the model, or reverted while readonly. */
  protected onNativeChange(radio: TmRadio<T>): void {
    if (this.readonly()) {
      this.syncNativeChecks();
      return;
    }
    this.value.set(radio.value());
  }

  /** The arrow keys: move to the previous or next enabled radio, wrapping, and check it. */
  protected onKeydown(event: KeyboardEvent): void {
    const natives = this.natives().map((ref) => ref.nativeElement);
    const target = ɵtmRadioArrowTarget(event, natives, this.hostElement);
    if (target === null) {
      return;
    }
    // The engine's own arrow handling (mapped by direction in some engines,
    // not in others) must not also run.
    event.preventDefault();
    target.focus();
    if (!this.readonly()) {
      this.value.set(this.radios()[natives.indexOf(target)].value());
    }
  }

  /** Reports touch when focus leaves the group (not when it moves between radios). */
  protected onFocusOut(event: FocusEvent): void {
    const next = event.relatedTarget;
    if (!(next instanceof Node) || !this.hostElement.contains(next)) {
      this.touch.emit();
    }
  }

  /**
   * Puts every native radio's checked state back to the model: a readonly
   * group's native check has already moved the browser's state, and the
   * bindings will not re-fire for a model that never changed.
   */
  private syncNativeChecks(): void {
    const radios = this.radios();
    this.natives().forEach((ref, index) => {
      ref.nativeElement.checked = this.isChecked(radios[index]);
    });
  }
}
