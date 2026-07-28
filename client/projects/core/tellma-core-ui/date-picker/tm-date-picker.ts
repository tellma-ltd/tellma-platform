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
  output,
  signal,
  untracked,
  viewChild,
  type Signal,
} from '@angular/core';
import { CdkConnectedOverlay, OverlayModule } from '@angular/cdk/overlay';
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
  tmDatePlaceholder,
  tmFormatDate,
  tmParseDate,
  type TmCalendar,
  type TmDateStyle,
} from '@tellma/core-ui/l10n';
import { tmCreateAnchoredOverlay, tmLogicalPositions } from '@tellma/core-ui/private';

import { ɵTmDatePopup } from './internal/tm-date-popup';

let nextUniqueId = 0;

/**
 * Date field: a free-text input (the primary entry path) plus a calendar
 * popup. The value is always an ISO `YYYY-MM-DD` string — Gregorian
 * regardless of the display calendar, with no `Date` objects in the API
 * (no time-zone ambiguity) — bounded to `0001-01-01`–`9999-12-31`.
 *
 * Typed text commits on blur and on Enter through the shared date engine:
 * locale field order, month names, completion-from-today, the two-digit-
 * year window — deterministic, and segments are never reordered to force
 * a match. The displayed text re-renders reactively on locale or calendar
 * switch; the model never changes.
 *
 * The calendar button is NOT a tab stop (one Tab per field — the
 * data-entry contract, in forms and grid cells alike); the keyboard path
 * to the popup is `Alt+ArrowDown` (plus plain `ArrowDown` standalone —
 * inside a grid, plain arrows belong to the grid). `Alt+ArrowUp` and
 * `Esc` close. Opening first commits any pending typed text, then moves
 * focus into the calendar; `Esc` closes without committing and returns
 * focus to the input.
 *
 * Inside `tm-form-field` the field supplies the bordered box and wiring
 * (`ownsChrome: false`); in a grid cell the bare input + button fill the
 * host and the grid owns commit and parse.
 *
 * @tmGroup form-control
 * @tmA11yNotes The input carries aria-haspopup="dialog"/aria-expanded;
 *   the popup is a non-modal dialog implementing the APG date-picker
 *   grid keyboard model with a politely announced month/year heading;
 *   focus returns to the input on close.
 */
@Component({
  selector: 'tm-date-picker',
  imports: [OverlayModule, ɵTmDatePopup],
  providers: [{ provide: TM_FORM_FIELD_CONTROL, useExisting: TmDatePicker }],
  template: `
    <input
      #textInput
      type="text"
      class="tm-input tm-date-picker__input"
      dir="auto"
      autocomplete="off"
      role="combobox"
      [id]="controlId()"
      [class.tm-input--in-field]="!!formField"
      [disabled]="disabled()"
      [readOnly]="readonly()"
      [required]="required()"
      [placeholder]="effectivePlaceholder()"
      aria-haspopup="dialog"
      [attr.aria-expanded]="popupOpen()"
      [attr.aria-controls]="popupId"
      [attr.aria-label]="ariaLabel()"
      [attr.aria-invalid]="showsInvalid() ? 'true' : null"
      [attr.aria-describedby]="describedByAttr()"
      [attr.aria-busy]="pending() ? 'true' : null"
      (input)="onInput()"
      (focus)="onFocus()"
      (blur)="onBlur()"
      (keydown)="onInputKeydown($event)"
    />
    <button
      type="button"
      class="tm-date-picker__toggle"
      tabindex="-1"
      [disabled]="disabled() || readonly()"
      [attr.aria-label]="toggleLabel()"
      (click)="onToggleClick()"
    >
      <svg viewBox="0 0 16 16" fill="none" aria-hidden="true">
        <rect x="2" y="3" width="12" height="11" rx="1.5" stroke="currentColor" stroke-width="1.5" />
        <path d="M2 6.5h12" stroke="currentColor" stroke-width="1.5" />
        <path d="M5.5 1.5v3M10.5 1.5v3" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" />
      </svg>
    </button>

    <ng-template
      [cdkConnectedOverlay]="anchored.overlayConfig()"
      [cdkConnectedOverlayOpen]="popupOpen()"
      (attach)="anchored.handleAttach()"
      (detach)="anchored.handleDetach()"
      (overlayOutsideClick)="closePopup(false)"
    >
      @defer (on immediate) {
        <tm-date-popup
          [id]="popupId"
          [calendar]="activeCalendar()"
          [locale]="l10n.locale()"
          [value]="value()"
          [min]="minDate()"
          [max]="maxDate()"
          (selected)="onPopupSelect($event)"
          (cancelled)="closePopup(true)"
          (rendered)="anchored.reanchor()"
        />
      }
    </ng-template>
  `,
  styleUrl: './tm-date-picker.css',
  host: {
    class: 'tm-date-picker',
    '[class.tm-date-picker--disabled]': 'disabled()',
  },
})
export class TmDatePicker implements TmFormFieldControl, TmCellEditor<string | null> {
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly errorDisplay = inject(TM_ERROR_DISPLAY);
  /** The reactive formatting facade (active locale + ambient calendar). */
  protected readonly l10n = inject(TmL10n);
  private readonly hostElement = inject(ElementRef).nativeElement as HTMLElement;
  /** The enclosing grid cell's registration sink, if any — absent standalone. */
  private readonly cellHost = inject(TM_CELL_EDITOR_HOST, { optional: true });
  /** The enclosing field, if any — flags `--in-field` chrome dissolution. */
  protected readonly formField = inject(TmFormField, { optional: true });

  // ---- FormValueControl<string | null> + the optional state inputs ----
  /** The ISO `YYYY-MM-DD` value (the FormValueControl model) — THE source of truth. */
  readonly value = model<string | null>(null);
  /** Non-form usage only — the bound field is authoritative when bound via [formField]. */
  readonly disabled = input(false, { transform: booleanAttribute });
  /** Readonly state — also disables the calendar button and popup. */
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
  /**
   * The inclusive lower ISO bound: popup navigation clamps to it (pair
   * with the `tmMinDate` validator for the matching message). Named
   * `minDate` because `min`/`max` bindings on a `[formField]` node are
   * reserved for the framework's own (number-typed) state inputs.
   */
  readonly minDate = input<string | undefined>(undefined);
  /** The inclusive upper ISO bound: popup navigation clamps to it (pair with `tmMaxDate`). */
  readonly maxDate = input<string | undefined>(undefined);
  /** The raw framework errors, bound by [formField] and localized into `localizedErrors`. */
  readonly errors = input<readonly ValidationError.WithOptionalFieldTree[]>([]);
  /** Touch reporting on native blur — `debounce('blur')` relies on it. */
  readonly touch = output<void>();

  // ---- Own API ----
  /** Per-instance display calendar; defaults to the app-ambient `TM_CALENDAR`. */
  readonly calendar = input<TmCalendar | undefined>(undefined);
  /** The display style of the formatted text. Default `numeric`. */
  readonly dateStyle = input<TmDateStyle>('numeric');
  /** Placeholder; defaults to the locale's field-order hint (e.g. `dd/mm/yyyy`). */
  readonly placeholder = input<string | undefined>(undefined);
  /** Accessible name for a picker used WITHOUT tm-form-field. */
  readonly ariaLabel = input<string | null>(null, { alias: 'aria-label' });
  /**
   * Author-supplied describedby ids (space-separated). Preserved — the
   * enclosing field's hint/error ids are merged AFTER them, never over
   * them.
   */
  readonly ariaDescribedby = input<string | null>(null, { alias: 'aria-describedby' });

  /** Stable generated id of the input — the `<label for>` and aria wiring target. */
  readonly controlId = signal(`tm-date-picker-${nextUniqueId++}`).asReadonly();
  /** The popup's element id — the combobox's aria-controls target. */
  protected readonly popupId = `${this.controlId()}-popup`;

  /** The display calendar in effect: the instance input, else the ambient one. */
  protected readonly activeCalendar = computed(() => this.calendar() ?? this.l10n.calendar());
  /** The localized calendar-button label. */
  protected readonly toggleLabel = computed(() => this.translate('datePicker.chooseDate')());
  /** The placeholder in effect: the input, else the locale's field-order hint. */
  protected readonly effectivePlaceholder = computed(
    () =>
      this.placeholder() ??
      tmDatePlaceholder(this.l10n.locale(), { calendar: this.activeCalendar() }),
  );

  /** Whether the calendar popup is open. */
  protected readonly popupOpen = signal(false);

  private readonly textInput = viewChild<ElementRef<HTMLInputElement>>('textInput');
  private readonly overlay = viewChild(CdkConnectedOverlay);

  /** The shared anchored-overlay wiring (popup anchored to the whole host). */
  protected readonly anchored = tmCreateAnchoredOverlay({
    overlay: () => this.overlay(),
    origin: () => this.hostElement,
    positions: tmLogicalPositions('block-end', 'start'),
    remeasure: 'macrotask',
  });

  /** Whether the input currently has focus (gates display rewrites). */
  private readonly focused = signal(false);
  /** The text present when focus was gained — the commit-only-when-dirty baseline. */
  private textAtFocus = '';
  /** Cell-editor revert baseline: the value `cancel()` returns to. */
  private lastCommitted: string | null = null;
  /** Marks this control's own model writes (see the baseline effect). */
  private selfWrite: { readonly value: string | null } | null = null;
  /** Picker-authored text with its known value; see `setCanonicalText`. */
  private displayOverride: { readonly text: string; readonly value: string | null } | null = null;

  /** The raw text channel over the date engine (see `tmParseDate`). */
  private readonly rawText = transformedValue<string | null, string>(this.value, {
    parse: (text) => this.parseText(text),
    format: (value) =>
      value === null
        ? ''
        : tmFormatDate(value, untracked(this.l10n.locale), {
            calendar: untracked(this.activeCalendar),
            dateStyle: untracked(this.dateStyle),
          }),
  });

  constructor() {
    this.cellHost?.register(this);

    // External value writes move the revert baseline; the control's own
    // parse-driven writes (marked inside parseText) do not.
    effect(() => {
      const value = this.value();
      const self = this.selfWrite;
      this.selfWrite = null;
      if (!self || !Object.is(self.value, value)) {
        this.lastCommitted = value;
      }
    });

    // Reflect the raw text into the native input — only while unfocused.
    // The viewChild is tracked, so the initial format lands as soon as the
    // input renders.
    effect(() => {
      const text = this.rawText();
      const element = this.textInput()?.nativeElement;
      if (element !== undefined && !this.focused() && element.value !== text) {
        element.value = text;
      }
    });

    // Locale/calendar/style switches re-render the display in place; the
    // model never changes (the display override pins the value, so the
    // reformat can never corrupt it through a lossy re-parse).
    effect(() => {
      const locale = this.l10n.locale();
      const calendar = this.activeCalendar();
      const dateStyle = this.dateStyle();
      untracked(() => {
        // The override is keyed to the formatting context that produced
        // it: a locale/calendar/style change retires it on EVERY path,
        // including the two early returns below — a stale override would
        // otherwise pin freshly typed text to the dead context's value.
        this.displayOverride = null;
        if (this.focused()) {
          return;
        }
        const value = this.value();
        if (value === null && this.rawText.parseErrors().length > 0) {
          // Unreadable text is KEPT for correction — a locale switch must
          // not silently erase it (the error stays live).
          return;
        }
        const text = value === null ? '' : tmFormatDate(value, locale, { calendar, dateStyle });
        if (text !== this.rawText()) {
          this.setCanonicalText(text, value);
        }
      });
    });
  }

  /**
   * Writes picker-authored display text whose VALUE is already known —
   * canonical reformat, locale switch, popup selection. The override makes
   * the accompanying parse an identity by construction: formatted output
   * is not universally re-parseable (two-digit-year pivots, exotic
   * locale/calendar pairs), and a lossy re-parse here would corrupt or
   * wipe the model.
   */
  private setCanonicalText(text: string, value: string | null): void {
    this.displayOverride = { text, value };
    this.rawText.set(text);
  }

  /** The rendered input — event handlers only run once the view exists. */
  private get elementOrThrow(): HTMLInputElement {
    return this.textInput()!.nativeElement;
  }

  /**
   * The raw→model parse through the date engine: empty → null;
   * unreadable → null + kind `parse` with a localized expected-pattern
   * example.
   */
  private parseText(text: string): ParseResult<string | null> {
    const override = this.displayOverride;
    if (override !== null && override.text === text) {
      this.selfWrite = { value: override.value };
      return { value: override.value };
    }
    const locale = untracked(this.l10n.locale);
    const calendar = untracked(this.activeCalendar);
    if (text.trim() === '') {
      this.selfWrite = { value: null };
      return { value: null };
    }
    const parsed = tmParseDate(text, locale, { calendar });
    if (parsed === TM_PARSE_ERROR || parsed === null) {
      this.selfWrite = { value: null };
      const example = tmFormatDate(calendar.today(), locale, {
        calendar,
        dateStyle: untracked(this.dateStyle),
      });
      // The message ships inline (it wins over the kind table): the `parse`
      // kind's table entry belongs to the number control, and a fresh
      // message is generated on every parse so the locale stays current.
      const message = untracked(this.translate('errors.parseDate', { example }));
      return {
        value: null,
        error: { kind: 'parse', message, example } as ValidationError.WithoutFieldTree,
      };
    }
    this.selfWrite = { value: parsed };
    return { value: parsed };
  }

  // ---- TmFormFieldControl ----
  /** The field renders the bordered box around this bare anatomy. */
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
  /** aria-invalid follows the error-DISPLAY policy; own parse errors count. */
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
    this.elementOrThrow.focus(options);
  }

  // ---- TmCellEditor<string | null> ----
  /** The committed-text view: the raw input text — parsing is the host's concern. */
  readonly text: Signal<string | null> = computed(() => this.rawText());

  /** Accepts the current value as the revert baseline and closes the popup. */
  commit(): void {
    this.lastCommitted = untracked(this.value);
    this.popupOpen.set(false);
  }

  /** Reverts to the last committed value (a grid host's second Esc). */
  cancel(): void {
    this.selfWrite = { value: this.lastCommitted };
    this.value.set(this.lastCommitted);
    this.popupOpen.set(false);
  }

  /** Type-to-edit seed: replaces the content with `text`, caret at the end. */
  seed(text: string): void {
    // Host-authored text with no value attached — any pin from an earlier
    // session must not claim it.
    this.displayOverride = null;
    this.rawText.set(text);
    const element = this.elementOrThrow;
    element.value = text;
    element.setSelectionRange(text.length, text.length);
  }

  // ---- The text loop ----
  /** Mirrors keystrokes into the raw channel (live parse, live validation). */
  protected onInput(): void {
    this.rawText.set(this.elementOrThrow.value);
  }

  /** Captures the commit-only-when-dirty baseline. */
  protected onFocus(): void {
    this.textAtFocus = this.elementOrThrow.value;
    this.focused.set(true);
  }

  /** Blur commit: canonical reformat, only when the text actually changed. */
  protected onBlur(): void {
    if (this.cellHost === null && this.elementOrThrow.value !== this.textAtFocus) {
      this.commitTypedText();
    }
    this.focused.set(false);
    this.touchedSelf.set(true);
    this.touch.emit();
  }

  /**
   * Keyboard: Enter commits typed text (standalone; a grid owns Enter);
   * `Alt+ArrowDown` — and plain `ArrowDown` standalone — opens the popup;
   * `Alt+ArrowUp` and `Esc` close it.
   */
  protected onInputKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && this.cellHost === null) {
      this.commitTypedText();
      return;
    }
    const plainArrowsOpen = this.cellHost === null;
    if (
      (event.key === 'ArrowDown' && (event.altKey || plainArrowsOpen)) ||
      (event.key === 'ArrowUp' && event.altKey && untracked(this.popupOpen))
    ) {
      event.preventDefault();
      if (event.key === 'ArrowDown') {
        this.openPopup();
      } else {
        this.closePopup(true);
      }
      return;
    }
    if (event.key === 'Escape' && untracked(this.popupOpen)) {
      // Stage-1 Esc: close the popup only; a grid's second Esc cancels the
      // session (its handler acts only on keys the editor left alone).
      event.preventDefault();
      this.closePopup(true);
    }
  }

  /** Normalizes valid typed text to the canonical display form. */
  private commitTypedText(): void {
    const element = this.elementOrThrow;
    const locale = untracked(this.l10n.locale);
    const calendar = untracked(this.activeCalendar);
    const override = this.displayOverride;
    if (override !== null && override.text === element.value) {
      // Picker-authored text with a known value — re-parsing it here
      // would undo the very pinning the override exists for (Enter and
      // popup-open both commit unconditionally, including pristine text).
      this.selfWrite = { value: override.value };
      this.value.set(override.value);
      this.textAtFocus = override.text;
      return;
    }
    const parsed = tmParseDate(element.value, locale, { calendar });
    if (typeof parsed === 'string') {
      const canonical = tmFormatDate(parsed, locale, {
        calendar,
        dateStyle: untracked(this.dateStyle),
      });
      this.setCanonicalText(canonical, parsed);
      element.value = canonical;
      this.textAtFocus = canonical;
    } else if (parsed === null) {
      this.setCanonicalText('', null);
      element.value = '';
      this.textAtFocus = '';
    }
    // Unreadable text stays put for correction (the error is already live).
  }

  /** Whether the popup is open — a grid host's dropdown gate. */
  isPopupOpen(): boolean {
    return untracked(this.popupOpen);
  }

  /** Opens the popup (the grid's `Alt+ArrowDown` path calls this too). */
  openPopup(): void {
    if (this.disabled() || this.readonly()) {
      return;
    }
    // Opening commits pending typed text first, so the calendar opens on
    // the day the field now holds.
    if (this.elementOrThrow.value !== this.rawText() || this.cellHost === null) {
      this.commitTypedText();
    }
    this.popupOpen.set(true);
  }

  /** Closes the popup; `refocus` returns focus to the input (Esc, selection). */
  protected closePopup(refocus: boolean): void {
    if (!untracked(this.popupOpen)) {
      return;
    }
    this.popupOpen.set(false);
    if (refocus) {
      this.focus();
    }
  }

  /** The calendar button: toggles the popup (pointer/AT path; never a tab stop). */
  protected onToggleClick(): void {
    if (untracked(this.popupOpen)) {
      this.closePopup(true);
    } else {
      this.openPopup();
    }
  }

  /** A day was selected in the popup: commit, close, refocus the input. */
  protected onPopupSelect(iso: string | null): void {
    this.selfWrite = { value: iso };
    this.value.set(iso);
    const text =
      iso === null
        ? ''
        : tmFormatDate(iso, untracked(this.l10n.locale), {
            calendar: untracked(this.activeCalendar),
            dateStyle: untracked(this.dateStyle),
          });
    this.setCanonicalText(text, iso);
    const element = this.elementOrThrow;
    element.value = text;
    this.textAtFocus = text;
    this.closePopup(true);
  }
}
