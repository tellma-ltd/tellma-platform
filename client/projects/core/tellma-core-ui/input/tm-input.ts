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
  signal,
} from '@angular/core';
import type { ValidationError } from '@angular/forms/signals';

import type { TmFieldError, TmFormFieldControl } from '@tellma/core-ui/contracts';
import { TM_ERROR_DISPLAY, TM_UI_TRANSLATE, tmResolveFieldErrors } from '@tellma/core-ui';
import { TM_FORM_FIELD_CONTROL, TmFormField } from '@tellma/core-ui/form-field';

let nextUniqueId = 0;

/**
 * Single-line text field — a bare directive on the native `<input>` (the
 * matInput model, §3.2): the native element IS the control, so it drops into
 * a grid cell with nothing to strip. Adornment chrome (bordered box, focus
 * ring, prefix/suffix) belongs to the enclosing `tm-form-field` (§3).
 *
 * Signal Forms native: implements `FormValueControl<string>` (`value`
 * model) plus the optional state inputs `[formField]` binds; reports touch
 * on native blur. When bound, the field is authoritative for
 * disabled/readonly/required — template-binding those on a bound control is
 * forbidden by lint (§5).
 *
 * Bidi: `dir="auto"` picks each field's base direction from its own content
 * (§7), independent of page direction; alignment follows via
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
    '[attr.aria-describedby]': 'ariaDescribedBy()',
    '[attr.aria-busy]': 'pending() ? "true" : null',
    '(input)': 'onInput($event)',
    '(blur)': 'touch.emit()',
  },
})
export class TmInput implements TmFormFieldControl {
  private readonly element = inject<ElementRef<HTMLInputElement>>(ElementRef).nativeElement;
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly errorDisplay = inject(TM_ERROR_DISPLAY);
  /** The enclosing field, if any — used only to resolve the size default. */
  protected readonly formField = inject(TmFormField, { optional: true });

  // ---- FormValueControl<string> + the optional state inputs (§5) ----
  /** The field value (the FormValueControl model). */
  readonly value = model('');
  /** Non-form usage only — the bound field is authoritative when bound (§5). */
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly readonly = input(false, { transform: booleanAttribute });
  readonly required = input(false, { transform: booleanAttribute });
  readonly invalid = input(false, { transform: booleanAttribute });
  readonly touched = input(false, { transform: booleanAttribute });
  readonly dirty = input(false, { transform: booleanAttribute });
  readonly pending = input(false, { transform: booleanAttribute });
  readonly errors = input<readonly ValidationError.WithOptionalFieldTree[]>([]);
  /** Touch reporting on native blur — `debounce('blur')` relies on it (§5). */
  readonly touch = output<void>();

  // ---- Own API (§3.2) ----
  readonly placeholder = input('');

  readonly controlId = signal(`tm-input-${nextUniqueId++}`).asReadonly();

  // ---- TmFormFieldControl (§2.1) ----
  /** The field renders the bordered box around this bare directive (§3). */
  readonly ownsChrome = false;
  readonly empty = computed(() => this.value() === '');
  private readonly fieldDescribedBy = signal<readonly string[]>([]);
  readonly describedByIds = this.fieldDescribedBy.asReadonly();
  readonly localizedErrors: () => readonly TmFieldError[] = tmResolveFieldErrors(
    this.errors,
    this.translate,
  );

  protected readonly ariaDescribedBy = computed(() => this.describedByIds().join(' ') || null);

  /**
   * aria-invalid follows the error-DISPLAY policy, not raw field validity —
   * a pristine required field is technically invalid but must not be
   * announced as such before the user has interacted (§5/§6).
   */
  protected readonly showsInvalid = computed(() =>
    this.errorDisplay({
      invalid: this.invalid(),
      touched: this.touched(),
      dirty: this.dirty(),
      pending: this.pending(),
    }),
  );

  constructor() {
    // Reflect external value writes into the native input without clobbering
    // the caret on the user's own keystrokes.
    effect(() => {
      const value = this.value();
      if (this.element.value !== value) {
        this.element.value = value;
      }
    });
  }

  setDescribedByIds(ids: readonly string[]): void {
    this.fieldDescribedBy.set(ids);
  }

  onContainerClick(): void {
    this.focus();
  }

  /** Signal Forms calls this when asked to focus the field. */
  focus(options?: FocusOptions): void {
    this.element.focus(options);
  }

  protected onInput(event: Event): void {
    this.value.set((event.target as HTMLInputElement).value);
  }
}
