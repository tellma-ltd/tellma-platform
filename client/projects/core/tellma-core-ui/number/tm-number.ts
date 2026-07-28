// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  computed,
  Directive,
  effect,
  ElementRef,
  inject,
  input,
  isDevMode,
  model,
  output,
  signal,
  type Signal,
  untracked,
} from '@angular/core';
import { transformedValue, type ParseResult, type ValidationError } from '@angular/forms/signals';

import {
  TM_PARSE_ERROR,
  type TmCellEditor,
  type TmFieldError,
  type TmFormFieldControl,
} from '@tellma/core-ui/contracts';
import {
  TM_CELL_EDITOR_HOST,
  TM_ERROR_DISPLAY,
  TM_UI_TRANSLATE,
  TmL10n,
  tmResolveFieldErrors,
} from '@tellma/core-ui';
import { TM_FORM_FIELD_CONTROL, TmFormField } from '@tellma/core-ui/form-field';
import {
  TM_NUMBER_MAX_DIGITS,
  tmFormatNumber,
  tmNumberDigitCount,
  tmParseNumber,
  type TmNumberFormatOptions,
} from '@tellma/core-ui/l10n';

let nextUniqueId = 0;

/**
 * Locale-aware numeric input — a bare directive on the native `<input>`
 * (never `type="number"`, whose spinner, scroll-to-increment and locale
 * quirks are the reason this control exists): the typed `value` model
 * (`FormValueControl<number | null>`) is the source of truth and the text
 * parses/formats through the shared locale codec.
 *
 * Gaining focus never rewrites the text (no flicker, stable caret): the
 * user edits the formatted string in place — the parser accepts group
 * separators anyway. On blur the text reformats to the canonical display
 * form, and only when it actually changed: focusing and leaving a field
 * never rewrites its model. On commit the model is rounded to the display
 * scale (`maxDecimals`), so the persisted value can never silently differ
 * from what the field shows; a committed value whose decimal digit count
 * exceeds 15 is rejected as a parse-level error rather than silently
 * corrupted (an IEEE-754 double round-trips at most 15 significant
 * digits against an exact server-side decimal). A PROGRAMMATIC write is
 * never mutated: `maxDecimals` is an entry/display policy, not the
 * storage scale — an external value carrying more precision displays
 * rounded while the model keeps the written value.
 *
 * Percent mode displays a fraction as a percentage (`0.75` → `75%`) and
 * parses `75`, `75%` and `٧٥٪` alike to `0.75`; `min`/`max` validate the
 * model fraction (a `0..1` range bounds a percentage field).
 *
 * Inside a grid cell the grid owns the value channel and the parse; the
 * control's own commit loop stands down and it contributes the numeric
 * mobile keypad, the physical right alignment, and one control to theme.
 *
 * Known platform limitation: the iOS decimal keypad has no minus key, so
 * negative amounts there need the standard keyboard.
 *
 * @tmGroup form-control
 * @tmA11yNotes Native input semantics with inputmode="decimal" for the
 *   numeric mobile keypad; aria-invalid/aria-describedby/aria-busy
 *   host-bound from field state; parse errors resolve to localized
 *   messages naming a locale-true example.
 */
@Directive({
  selector: 'input[tmNumber]',
  providers: [{ provide: TM_FORM_FIELD_CONTROL, useExisting: TmNumber }],
  host: {
    class: 'tm-input tm-number',
    inputmode: 'decimal',
    '[id]': 'controlId()',
    '[class.tm-input--in-field]': '!!formField',
    '[disabled]': 'disabled()',
    '[readOnly]': 'readonly()',
    '[required]': 'required()',
    '[placeholder]': 'placeholder()',
    '[attr.aria-invalid]': 'showsInvalid() ? "true" : null',
    '[attr.aria-describedby]': 'describedByAttr()',
    '[attr.aria-busy]': 'pending() ? "true" : null',
    '(input)': 'onInput()',
    '(focus)': 'onFocus()',
    '(blur)': 'onBlur()',
  },
})
export class TmNumber implements TmFormFieldControl, TmCellEditor<number | null> {
  private readonly element = inject<ElementRef<HTMLInputElement>>(ElementRef).nativeElement;
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly errorDisplay = inject(TM_ERROR_DISPLAY);
  private readonly l10n = inject(TmL10n);
  /** The enclosing grid cell's registration sink, if any — absent standalone. */
  private readonly cellHost = inject(TM_CELL_EDITOR_HOST, { optional: true });
  /** The enclosing field, if any — used only to flag `--in-field` so the input
   * inherits the field's chrome and sizing. Form state flows via `[formField]`. */
  protected readonly formField = inject(TmFormField, { optional: true });

  // ---- FormValueControl<number | null> + the optional state inputs ----
  /** The typed field value (the FormValueControl model) — THE source of truth. */
  readonly value = model<number | null>(null);
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
  /** The bound field's `min` bound (validated by the schema; surfaced via the `min` kind). */
  readonly min = input<number | undefined>(undefined);
  /** The bound field's `max` bound (validated by the schema; surfaced via the `max` kind). */
  readonly max = input<number | undefined>(undefined);
  /** The raw framework errors, bound by [formField] and localized into `localizedErrors`. */
  readonly errors = input<readonly ValidationError.WithOptionalFieldTree[]>([]);
  /** Touch reporting on native blur — `debounce('blur')` relies on it. */
  readonly touch = output<void>();

  // ---- Own API ----
  /** Minimum displayed fraction digits (zero-padded). Default 0. */
  readonly minDecimals = input<number | undefined>(undefined);
  /**
   * Maximum fraction digits — the display's rounding scale AND the commit
   * normalization scale (model = display on commit). Default 20 ("as many
   * as needed"), floored at `minDecimals`. Set it to the storage scale on
   * entry fields; the server remains the authority on storage scale.
   */
  readonly maxDecimals = input<number | undefined>(undefined);
  /** Percent mode: display `0.75` as `75%`; parse `75`/`75%`/`٧٥٪` to `0.75`. */
  readonly percent = input(false, { transform: booleanAttribute });
  /** Placeholder text shown while the input is empty. */
  readonly placeholder = input('');
  /**
   * Author-supplied describedby ids (space-separated). Preserved — the
   * enclosing field's hint/error ids are merged AFTER them, never over
   * them.
   */
  readonly ariaDescribedby = input<string | null>(null, { alias: 'aria-describedby' });

  /** Stable generated id of the input — the `<label for>` and aria wiring target. */
  readonly controlId = signal(`tm-number-${nextUniqueId++}`).asReadonly();

  /** The codec options derived from the display inputs. */
  private readonly formatOptions: Signal<TmNumberFormatOptions> = computed(() => ({
    minDecimals: this.minDecimals(),
    maxDecimals: this.maxDecimals(),
    percent: this.percent(),
  }));

  /** Whether the input currently has focus (gates display rewrites). */
  private readonly focused = signal(false);
  /** The text present when focus was gained — the commit-only-when-dirty baseline. */
  private textAtFocus = '';

  /** Cell-editor revert baseline: the value `cancel()` returns to. */
  private lastCommitted: number | null = null;
  /**
   * The value this control itself just wrote (via a parse of its own raw
   * text, or `cancel()`), so the baseline effect can tell its own echo
   * from an EXTERNAL write — only external writes and `commit()` move the
   * revert baseline.
   */
  private selfWrite: { readonly value: number | null } | null = null;

  /**
   * Set while {@link reformatDisplay} pushes canonical text through the
   * raw channel, so its parse writes nothing back to the model.
   */
  private displayOnly = false;

  /**
   * A locale/option switch arrived while the user was typing; the display
   * still shows the old locale's text and owes a reformat at blur.
   */
  private reformatDeferred = false;

  /**
   * The raw text channel, synchronized with the typed `value` model via the
   * locale codec. Parse errors report to the nearest Signal Forms field
   * automatically; `parseErrors` also feeds the standalone invalid state.
   */
  private readonly rawText = transformedValue<number | null, string>(this.value, {
    parse: (text) => this.parseText(text),
    format: (value) =>
      value === null
        ? ''
        : tmFormatNumber(value, untracked(this.l10n.locale), untracked(this.formatOptions)),
  });

  constructor() {
    // The native input must never be type="number" — its spinner,
    // scroll-to-increment, and locale quirks are what this control exists
    // to avoid. The guard is imperative so an authored type cannot race it.
    if (isDevMode() && this.element.getAttribute('type') === 'number') {
      console.warn(
        'tmNumber: type="number" is overridden to type="text" — the directive owns numeric semantics.',
        this.element,
      );
    }
    this.element.setAttribute('type', 'text');

    this.cellHost?.register(this);

    // External value writes move the revert baseline; the control's own
    // parse-driven writes (marked via `selfWrite` inside parseText) do not.
    effect(() => {
      const value = this.value();
      const self = this.selfWrite;
      this.selfWrite = null;
      if (!self || !Object.is(self.value, value)) {
        this.lastCommitted = value;
      }
    });

    // Reflect the raw text into the native input — only while unfocused:
    // the user's in-progress editing is never rewritten (stable caret; the
    // canonical reformat happens on blur).
    effect(() => {
      const text = this.rawText();
      if (!this.focused() && this.element.value !== text) {
        this.element.value = text;
      }
    });

    // Locale (or display-option) switches reformat the display in place —
    // while unfocused; a focused instance defers to its blur.
    effect(() => {
      const locale = this.l10n.locale();
      const options = this.formatOptions();
      untracked(() => {
        if (this.focused()) {
          // The new locale is owed once the user leaves (see `onBlur`).
          this.reformatDeferred = true;
          return;
        }
        this.reformatDisplay(locale, options);
      });
    });
  }

  /**
   * Rewrites the displayed text under `locale`/`options` WITHOUT touching
   * the model. A programmatic value may carry more precision than the
   * display scale (a server-computed figure, or a column scale above the
   * entry policy); re-parsing the rounded text would silently persist the
   * rounding, and a locale switch is not a user edit.
   */
  private reformatDisplay(locale: string, options: TmNumberFormatOptions): void {
    const value = this.value();
    if (value === null && this.rawText.parseErrors().length > 0) {
      // Unreadable text is KEPT for correction — a locale switch must
      // not silently erase it (the error stays live).
      return;
    }
    const text = value === null ? '' : tmFormatNumber(value, locale, options);
    if (text === this.rawText()) {
      return;
    }
    this.displayOnly = true;
    try {
      this.rawText.set(text);
    } finally {
      this.displayOnly = false;
    }
  }

  /**
   * The raw→model parse: empty → null; unparseable → null + kind `parse`
   * (message names a locale-true example); a value whose ROUNDED form
   * exceeds the digit envelope → null + kind `numberPrecision`. A valid
   * keystroke updates the model live with the unrounded value — rounding
   * to display scale is the blur commit's normalization.
   */
  private parseText(text: string): ParseResult<number | null> {
    if (this.displayOnly) {
      // A re-render of the SAME value in a new locale: omitting `value`
      // is the framework's documented "do not update the model" shape.
      return {};
    }
    const locale = untracked(this.l10n.locale);
    const options = untracked(this.formatOptions);
    if (text.trim() === '') {
      this.selfWrite = { value: null };
      return { value: null };
    }
    const parsed = tmParseNumber(text, locale, options);
    if (parsed === TM_PARSE_ERROR || parsed === null) {
      this.selfWrite = { value: null };
      const example = tmFormatNumber(options.percent === true ? 0.155 : 1234.5, locale, options);
      return { value: null, error: { kind: 'parse', example } as ValidationError.WithoutFieldTree };
    }
    // The envelope is checked against the value a commit would persist —
    // the display-rounded one — so it composes with any maxDecimals.
    const rounded = tmParseNumber(tmFormatNumber(parsed, locale, options), locale, options);
    if (typeof rounded === 'number' && tmNumberDigitCount(rounded) > TM_NUMBER_MAX_DIGITS) {
      this.selfWrite = { value: null };
      return {
        value: null,
        error: {
          kind: 'numberPrecision',
          maxDigits: TM_NUMBER_MAX_DIGITS,
        } as ValidationError.WithoutFieldTree,
      };
    }
    this.selfWrite = { value: parsed };
    return { value: parsed };
  }

  // ---- TmFormFieldControl ----
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
   * aria-invalid follows the error-DISPLAY policy. A standalone (unbound)
   * control still surfaces its own parse errors: they count as invalid and
   * the control's own blur as touched.
   */
  protected readonly showsInvalid = computed(() =>
    this.errorDisplay({
      invalid: this.invalid() || this.rawText.parseErrors().length > 0,
      touched: this.touched() || this.touchedSelf(),
      dirty: this.dirty(),
      pending: this.pending(),
    }),
  );
  private readonly touchedSelf = signal(false);

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

  // ---- TmCellEditor<number | null> ----
  /** The committed-text view: the raw text — never `null` (parsing is the host's concern). */
  readonly text: Signal<string | null> = computed(() => this.rawText());

  /** Accepts the current value as the revert baseline. */
  commit(): void {
    this.lastCommitted = untracked(this.value);
  }

  /** Reverts to the last committed value (a grid host's second Esc). */
  cancel(): void {
    this.selfWrite = { value: this.lastCommitted };
    this.value.set(this.lastCommitted);
  }

  /** Type-to-edit seed: replaces the content with `text`, caret at the end. */
  seed(text: string): void {
    this.rawText.set(text);
    // Write the native value now (not at effect flush) so the caret can be
    // placed synchronously — the user's next keystroke must append.
    this.element.value = text;
    this.element.setSelectionRange(text.length, text.length);
  }

  // ---- The format/parse loop ----
  /** Mirrors keystrokes into the raw channel (live parse, live validation). */
  protected onInput(): void {
    this.rawText.set(this.element.value);
  }

  /** Captures the commit-only-when-dirty baseline. */
  protected onFocus(): void {
    this.textAtFocus = this.element.value;
    this.focused.set(true);
  }

  /**
   * The commit: reformat to the canonical display form and round the model
   * to the display scale — only when the text actually changed (focusing
   * and leaving never rewrites the model). Grid-hosted, the grid owns the
   * commit and parse; the control's own normalization stands down.
   */
  protected onBlur(): void {
    const shouldNormalize = this.cellHost === null && this.element.value !== this.textAtFocus;
    if (shouldNormalize) {
      const locale = untracked(this.l10n.locale);
      const options = untracked(this.formatOptions);
      const parsed = tmParseNumber(this.element.value, locale, options);
      if (typeof parsed === 'number') {
        const canonical = tmFormatNumber(parsed, locale, options);
        // Setting the canonical text re-parses it: the model becomes
        // exactly the value the shown text denotes (model = display), and
        // the envelope guard applies to that committed value.
        this.rawText.set(canonical);
      }
      // Unparseable text stays put for correction (the parse error is
      // already reported from the keystroke channel).
    }
    this.focused.set(false);
    if (this.reformatDeferred) {
      // A locale switch landed mid-edit. Even a PRISTINE field owes the
      // new locale's text — otherwise stale-locale digits sit there and
      // the next edit re-parses them under the new locale's separators.
      this.reformatDeferred = false;
      this.reformatDisplay(untracked(this.l10n.locale), untracked(this.formatOptions));
    }
    this.touchedSelf.set(true);
    this.touch.emit();
  }
}
