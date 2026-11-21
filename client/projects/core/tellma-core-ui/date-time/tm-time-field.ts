// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  input,
  model,
  numberAttribute,
  output,
  signal,
  untracked,
  viewChild,
  ViewEncapsulation,
  type Signal,
} from '@angular/core';
import { transformedValue, type ParseResult, type ValidationError } from '@angular/forms/signals';

import {
  TM_PARSE_ERROR,
  type TmFieldError,
  type TmFormFieldControl,
} from '@tellma/core-ui/contracts';
import {
  TM_ERROR_DISPLAY,
  TM_NOW,
  TM_UI_TRANSLATE,
  TmL10n,
  tmResolveFieldErrors,
  type TmControlSize,
} from '@tellma/core-ui';
import { TM_FORM_FIELD_CONTROL, TmFormField } from '@tellma/core-ui/form-field';

import {
  ɵtmFormatTimeOfDay,
  ɵtmIsTimeOfDay,
  ɵtmParseTimeOfDay,
  ɵtmStepTimeOfDay,
  ɵtmTimeOfMinutes,
} from './internal/tm-time-of-day';
import { ɵtmWallTimeOf } from './internal/tm-zone-wall-time';

let nextUniqueId = 0;

/** The time every parse-error message shows as its example ("14:30", "2:30 PM"). */
const EXAMPLE_TIME = '14:30';

/**
 * A typed time of day: a text input whose value is `HH:mm` (24-hour, Latin
 * digits) or `null`, shown in the locale's hour cycle and digits ("14:30"
 * in en-GB, "2:30 PM" in en-US, "٢:٣٠ م" in ar-SA). There is no list of
 * times: typing is the entry path.
 *
 * Typed text reads digits in any script, `14`, `930`, `1430`, `14:30`, and
 * the locale's day-period words ("pm", "م"); it reformats on blur, and on
 * Enter. Unreadable text is the `parse` error and leaves the value `null`.
 * Up and Down add or subtract `step` minutes, wrapping at midnight; from
 * empty they start at the current time, rounded down to the step.
 *
 * Place it inside a `tm-form-field`, which supplies the box, the label and
 * the messages; that field takes the time width (`--date-time-time-width`),
 * which holds the widest locale's time text. Signal Forms native: implements
 * `FormValueControl<string | null>`; reports touch on blur.
 *
 * @tmGroup form-control
 * @tmA11yNotes A native text input (dir="auto", tabular digits); Up and
 *   Down step the time; a parse failure is announced through the
 *   enclosing field's error region.
 */
@Component({
  selector: 'tm-time-field',
  providers: [{ provide: TM_FORM_FIELD_CONTROL, useExisting: TmTimeField }],
  // The stylesheet sizes the enclosing tm-form-field to the time width, so
  // it is not encapsulated; every selector is namespaced under .tm-time-field.
  encapsulation: ViewEncapsulation.None,
  template: `
    <input
      #textInput
      type="text"
      class="tm-input tm-time-field__input"
      dir="auto"
      autocomplete="off"
      [id]="controlId()"
      [class.tm-input--in-field]="!!formField"
      [disabled]="disabled()"
      [readOnly]="readonly()"
      [required]="required()"
      [placeholder]="placeholder() ?? ''"
      [attr.aria-label]="ariaLabel()"
      [attr.aria-invalid]="showsInvalid() ? 'true' : null"
      [attr.aria-describedby]="describedByAttr()"
      [attr.aria-busy]="pending() ? 'true' : null"
      (input)="onInput()"
      (focus)="onFocus()"
      (blur)="onBlur()"
      (keydown)="onKeydown($event)"
    />
  `,
  styleUrl: './tm-time-field.css',
  host: {
    class: 'tm-time-field',
    '[class.tm-time-field--sm]': 'size() === "sm"',
    '[class.tm-time-field--lg]': 'size() === "lg"',
    '[class.tm-time-field--invalid]': 'showsInvalid()',
  },
})
export class TmTimeField implements TmFormFieldControl {
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly errorDisplay = inject(TM_ERROR_DISPLAY);
  private readonly now = inject(TM_NOW);
  /** The reactive formatting facade (the active locale and the display zone). */
  private readonly l10n = inject(TmL10n);
  /** The enclosing field, if any — flags the in-field chrome dissolution. */
  protected readonly formField = inject(TmFormField, { optional: true });

  // ---- FormValueControl<string | null> + the optional state inputs ----
  /** The time of day, `HH:mm` (24-hour, Latin digits); `null` when empty. */
  readonly value = model<string | null>(null);
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
  /** Touch reporting on native blur. */
  readonly touch = output<void>();

  // ---- Own API ----
  /** The minutes Up and Down add or subtract. Default 15. */
  readonly step = input(15, { transform: numberAttribute });
  /** Placeholder text shown while the input is empty. */
  readonly placeholder = input<string | undefined>(undefined);
  /**
   * The text's size step, for a time field outside a `tm-form-field`;
   * inside one, the field's size governs (give the field the size).
   */
  readonly size = input<TmControlSize | undefined>(undefined);
  /** Accessible name for a time field used WITHOUT a `tm-form-field`. */
  readonly ariaLabel = input<string | null>(null, { alias: 'aria-label' });
  /**
   * Author-supplied describedby ids (space-separated). Preserved — the
   * enclosing field's hint/error ids are merged after them.
   */
  readonly ariaDescribedby = input<string | null>(null, { alias: 'aria-describedby' });
  /**
   * The zone whose clock Up and Down start from on an empty field.
   * @internal Set by `tm-date-time-field`, whose zone is its own.
   */
  readonly ɵclockZone = input<string | undefined>(undefined);

  /** Stable generated id of the input — the `<label for>` and aria wiring target. */
  readonly controlId = signal(`tm-time-field-${nextUniqueId++}`).asReadonly();

  private readonly textInput = viewChild.required<ElementRef<HTMLInputElement>>('textInput');
  /** Whether the input has focus (the text is reflected from the value only while it does not). */
  private readonly focused = signal(false);
  /** The text present when focus was gained — the reformat-only-when-edited baseline. */
  private textAtFocus = '';

  /** The raw text channel over the time parser. */
  private readonly rawText = transformedValue<string | null, string>(this.value, {
    parse: (text) => this.parseText(text),
    format: (value) =>
      value === null || !ɵtmIsTimeOfDay(value)
        ? (value ?? '')
        : ɵtmFormatTimeOfDay(value, untracked(this.l10n.locale)),
  });

  /**
   * The parse errors of the typed text (the `parse` kind), whether or not
   * the field is bound: a composite that hosts this field reads them to
   * report them as its own.
   * @internal Read by `tm-date-time-field` only.
   */
  readonly ɵparseErrors: Signal<readonly ValidationError.WithoutFieldTree[]> =
    this.rawText.parseErrors;

  constructor() {
    // Reflect the raw text into the native input — only while unfocused, so
    // typing is never rewritten under the caret.
    effect(() => {
      const text = this.rawText();
      const element = this.textInput().nativeElement;
      if (!this.focused() && element.value !== text) {
        element.value = text;
      }
    });

    // A locale switch re-renders the text in place; the model never
    // changes. Unreadable text is kept for correction.
    effect(() => {
      const locale = this.l10n.locale();
      untracked(() => {
        const value = this.value();
        if (this.focused() || (value === null && this.rawText.parseErrors().length > 0)) {
          return;
        }
        const text =
          value === null || !ɵtmIsTimeOfDay(value) ? '' : ɵtmFormatTimeOfDay(value, locale);
        if (text !== this.rawText()) {
          this.rawText.set(text);
        }
      });
    });
  }

  // ---- TmFormFieldControl ----
  /** The field renders the bordered box around this bare input. */
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
  /** Touched by this input's own blur, for a field used without a bound state. */
  private readonly touchedSelf = signal(false);
  /** aria-invalid follows the error-display policy; the input's own parse errors count. */
  protected readonly showsInvalid = computed(() =>
    this.errorDisplay({
      invalid: this.invalid() || this.rawText.parseErrors().length > 0,
      touched: this.touched() || this.touchedSelf(),
      dirty: this.dirty(),
      pending: this.pending(),
    }),
  );

  /** Receives the field's hint/error ids and exposes them via aria-describedby. */
  setDescribedByIds(ids: readonly string[]): void {
    this.fieldDescribedBy.set(ids);
  }

  /** Focuses the input when the user clicks the field's container chrome. */
  onContainerClick(): void {
    this.focus();
  }

  /** Focuses the input; Signal Forms calls this when asked to focus the field. */
  focus(options?: FocusOptions): void {
    this.textInput().nativeElement.focus(options);
  }

  /**
   * The raw→model parse: blank → `null`; readable → `HH:mm`; unreadable →
   * `null` with the `parse` error and a localized example.
   */
  private parseText(text: string): ParseResult<string | null> {
    const locale = untracked(this.l10n.locale);
    const parsed = ɵtmParseTimeOfDay(text, locale);
    if (parsed !== TM_PARSE_ERROR) {
      return { value: parsed };
    }
    const example = ɵtmFormatTimeOfDay(EXAMPLE_TIME, locale);
    // The message ships inline (it wins over the kind table, whose `parse`
    // entry belongs to the number control).
    const message = untracked(this.translate('errors.parseTime', { example }));
    return {
      value: null,
      error: { kind: 'parse', message, example } as ValidationError.WithoutFieldTree,
    };
  }

  // ---- The text loop ----
  /** Mirrors keystrokes into the raw channel (live parse, live validation). */
  protected onInput(): void {
    this.rawText.set(this.textInput().nativeElement.value);
  }

  /** Captures the reformat-only-when-edited baseline. */
  protected onFocus(): void {
    this.textAtFocus = this.textInput().nativeElement.value;
    this.focused.set(true);
  }

  /** Blur: the canonical reformat (only when the text was edited), then touch. */
  protected onBlur(): void {
    if (this.textInput().nativeElement.value !== this.textAtFocus) {
      this.reformat();
    }
    this.focused.set(false);
    this.touchedSelf.set(true);
    this.touch.emit();
  }

  /** Enter reformats typed text; Up and Down step the time. */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      this.reformat();
      return;
    }
    if (
      (event.key !== 'ArrowUp' && event.key !== 'ArrowDown') ||
      event.altKey ||
      event.ctrlKey ||
      event.metaKey ||
      event.shiftKey
    ) {
      return;
    }
    event.preventDefault();
    if (this.disabled() || this.readonly()) {
      return;
    }
    const element = this.textInput().nativeElement;
    const locale = untracked(this.l10n.locale);
    const step = Math.max(1, Math.round(untracked(this.step)) || 15);
    const typed = ɵtmParseTimeOfDay(element.value, locale);
    let next: string;
    if (typeof typed === 'string') {
      next = ɵtmStepTimeOfDay(typed, event.key === 'ArrowUp' ? step : -step);
    } else {
      // Empty or unreadable: start from the clock, on the step grid.
      const zone = untracked(this.ɵclockZone) ?? untracked(this.l10n.timeZone);
      const [hour, minute] = ɵtmWallTimeOf(this.now(), zone).time.split(':').map(Number);
      const minutes = hour * 60 + minute;
      next = ɵtmTimeOfMinutes(minutes - (minutes % step));
    }
    const text = ɵtmFormatTimeOfDay(next, locale);
    this.rawText.set(text);
    element.value = text;
    this.textAtFocus = text;
  }

  /** Rewrites readable typed text in the canonical form; unreadable text stays for correction. */
  private reformat(): void {
    const element = this.textInput().nativeElement;
    const locale = untracked(this.l10n.locale);
    const parsed = ɵtmParseTimeOfDay(element.value, locale);
    if (parsed === TM_PARSE_ERROR) {
      return;
    }
    const text = parsed === null ? '' : ɵtmFormatTimeOfDay(parsed, locale);
    this.rawText.set(text);
    element.value = text;
    this.textAtFocus = text;
  }
}
