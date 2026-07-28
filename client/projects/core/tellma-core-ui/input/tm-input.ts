// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  computed,
  Directive,
  ElementRef,
  effect,
  inject,
  input,
  model,
  output,
  type Signal,
  signal,
} from '@angular/core';
import type { ValidationError } from '@angular/forms/signals';

import type { TmCellEditor, TmFieldError, TmFormFieldControl } from '@tellma/core-ui/contracts';
import {
  TM_CELL_EDITOR_HOST,
  TM_ERROR_DISPLAY,
  TM_UI_TRANSLATE,
  tmResolveFieldErrors,
} from '@tellma/core-ui';
import { TM_FORM_FIELD_CONTROL, TmFormField } from '@tellma/core-ui/form-field';

let nextUniqueId = 0;

/**
 * Single-line text field — a bare directive on the native `<input>` (the
 * matInput model): the native element IS the control, so it drops into
 * a grid cell with nothing to strip. Adornment chrome (bordered box, focus
 * ring, prefix/suffix) belongs to the enclosing `tm-form-field`.
 *
 * Signal Forms native: implements `FormValueControl<string>` (`value`
 * model) plus the optional state inputs `[formField]` binds; reports touch
 * on native blur. When bound, the field is authoritative for
 * disabled/readonly/required — template-binding those on a bound control is
 * forbidden by lint.
 *
 * Bidi: `dir="auto"` picks each field's base direction from its own content,
 * independent of page direction; alignment follows via
 * `text-align: start`.
 *
 * @tmGroup form-control
 * @tmA11yNotes Native input semantics; aria-invalid/aria-required/
 *   aria-describedby/aria-busy host-bound from field state.
 */
@Directive({
  selector: 'input[tmInput]',
  providers: [{ provide: TM_FORM_FIELD_CONTROL, useExisting: TmInput }],
  host: {
    class: 'tm-input',
    dir: 'auto',
    '[id]': 'controlId()',
    '[class.tm-input--in-field]': '!!formField',
    '[disabled]': 'disabled()',
    '[readOnly]': 'readonly()',
    '[required]': 'required()',
    '[placeholder]': 'placeholder()',
    '[attr.aria-invalid]': 'showsInvalid() ? "true" : null',
    '[attr.aria-describedby]': 'describedByAttr()',
    '[attr.aria-busy]': 'pending() ? "true" : null',
    '(input)': 'onInput($event)',
    '(blur)': 'touch.emit()',
  },
})
export class TmInput implements TmFormFieldControl, TmCellEditor<string> {
  private readonly element = inject<ElementRef<HTMLInputElement>>(ElementRef).nativeElement;
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly errorDisplay = inject(TM_ERROR_DISPLAY);
  /** The enclosing grid cell's registration sink, if any — absent standalone. */
  private readonly cellHost = inject(TM_CELL_EDITOR_HOST, { optional: true });
  /** The enclosing field, if any — used only to flag `--in-field` so the input
   * inherits the field's chrome and sizing. Form state flows via `[formField]`. */
  protected readonly formField = inject(TmFormField, { optional: true });

  // ---- FormValueControl<string> + the optional state inputs (§5) ----
  /** The field value (the FormValueControl model). */
  readonly value = model('');
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
  /** Touch reporting on native blur — `debounce('blur')` relies on it. */
  readonly touch = output<void>();

  // ---- Own API (§3.2) ----
  /** Placeholder text shown while the input is empty. */
  readonly placeholder = input('');
  /**
   * Author-supplied describedby ids (space-separated). Preserved — the
   * enclosing field's hint/error ids are merged AFTER them, never over
   * them. Set as the `aria-describedby` attribute or bound as an input;
   * a raw `[attr.aria-describedby]` binding is outside the seam and loses.
   */
  readonly ariaDescribedby = input<string | null>(null, { alias: 'aria-describedby' });

  /** Stable generated id of the input — the `<label for>` and aria wiring target. */
  readonly controlId = signal(`tm-input-${nextUniqueId++}`).asReadonly();

  // ---- TmFormFieldControl (§2.1) ----
  /** The field renders the bordered box around this bare directive. */
  readonly ownsChrome = false;
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

  /**
   * aria-invalid follows the error-DISPLAY policy, not raw field validity —
   * a pristine required field is technically invalid but must not be
   * announced as such before the user has interacted.
   */
  protected readonly showsInvalid = computed(() =>
    this.errorDisplay({
      invalid: this.invalid(),
      touched: this.touched(),
      dirty: this.dirty(),
      pending: this.pending(),
    }),
  );

  // ---- TmCellEditor<string> ----
  /**
   * The committed-text view of the content. For a plain text input the text
   * IS the value — never `null`; interpreting it (parsing, validation) is
   * the host's concern.
   */
  readonly text: Signal<string | null> = computed(() => this.value() ?? '');
  /** Cell-editor revert baseline: the value `cancel()` returns to. */
  private lastCommitted = '';
  /**
   * The value this input itself just wrote (keystroke, seed, cancel), so
   * the baseline effect can tell its own echo from an EXTERNAL write — only
   * external writes (form resets, a grid opening the editor) and `commit()`
   * move the revert baseline.
   */
  private selfWrite: { readonly value: string } | null = null;

  constructor() {
    this.cellHost?.register(this);

    // Reflect external value writes into the native input without clobbering
    // the caret on the user's own keystrokes; external writes also move the
    // cell-editor revert baseline (the input's own writes do not).
    effect(() => {
      const value = this.value() ?? '';
      const self = this.selfWrite;
      this.selfWrite = null;
      if (!self || !Object.is(self.value, value)) {
        this.lastCommitted = value;
      }
      if (this.element.value !== value) {
        this.element.value = value;
      }
    });
  }

  /** Receives the field's hint/error ids and exposes them via aria-describedby. */
  setDescribedByIds(ids: readonly string[]): void {
    this.fieldDescribedBy.set(ids);
  }

  /** Focuses the input when the user clicks the field's container chrome. */
  onContainerClick(): void {
    this.focus();
  }

  /** Signal Forms calls this when asked to focus the field. */
  focus(options?: FocusOptions): void {
    this.element.focus(options);
  }

  /**
   * Accepts the current content: the value channel already mirrors every
   * keystroke, so committing only moves the revert baseline.
   */
  commit(): void {
    this.lastCommitted = this.value() ?? '';
  }

  /** Reverts to the value present when editing began (a grid host's Esc). */
  cancel(): void {
    this.selfWrite = { value: this.lastCommitted };
    this.value.set(this.lastCommitted);
  }

  /** Type-to-edit seed: replaces the content with `text`, caret at the end. */
  seed(text: string): void {
    this.selfWrite = { value: text };
    this.value.set(text);
    // Write the native value now (not at effect flush) so the caret can be
    // placed synchronously — the user's next keystroke must append.
    this.element.value = text;
    this.element.setSelectionRange(text.length, text.length);
  }

  /** Mirrors native input events into the `value` model. */
  protected onInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.selfWrite = { value };
    this.value.set(value);
  }
}
