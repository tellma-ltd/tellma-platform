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
} from '@angular/core';
import { transformedValue, type ParseResult, type ValidationError } from '@angular/forms/signals';

import { TM_UI_TRANSLATE, ɵtmControlSize, type TmControlSize } from '@tellma/core-ui';
import { TmDatePicker } from '@tellma/core-ui/date-picker';
import { TmFormField } from '@tellma/core-ui/form-field';
import type { TmCalendar } from '@tellma/core-ui/l10n';

import type { TmDateRangeValue } from './tm-date-range-order';

let nextUniqueId = 0;

/** Which end of the range an error belongs to. */
type End = 'from' | 'to';

/** An error this control raises itself, tagged with the end it is shown on. */
type EndError = ValidationError.WithoutFieldTree & { readonly end: End };

/** The empty range. */
const EMPTY: TmDateRangeValue = { from: null, to: null };

/**
 * Edits a from–to pair of dates as two coupled date pickers in a labelled
 * `role="group"`. The "from" picker's calendar greys out days after the
 * current "to", and the "to" picker's days before the current "from", so
 * the popups offer only ordered ranges; typed text can still be out of
 * order, which `tmDateRangeOrder` reports and this control shows on the
 * "to" field.
 *
 * A part's unreadable text is the control's own `parse` error, shown on
 * that part. Other errors of the bound field show on the "from" part.
 * There is no range calendar: each end has its own popup.
 *
 * Place it directly, never inside a `tm-form-field`. Signal Forms native:
 * implements `FormValueControl<TmDateRangeValue>`; reports touch when focus
 * leaves the group.
 *
 * @tmGroup form-control
 * @tmA11yNotes A labelled group of two date pickers, each with its own
 *   label and the date picker's keys; the order error is announced on the
 *   "to" field.
 */
@Component({
  selector: 'tm-date-range',
  imports: [TmDatePicker, TmFormField],
  template: `
    @if (label(); as label) {
      <span class="tm-date-range__label" [id]="labelId">{{ label }}</span>
    }
    <div class="tm-date-range__parts">
      <tm-form-field class="tm-date-range__part" [label]="fromText()" [size]="resolvedSize()">
        <tm-date-picker
          #fromPicker
          [value]="parts().from"
          [calendar]="calendar()"
          [minDate]="minDate()"
          [maxDate]="fromMax()"
          [disabled]="disabled()"
          [readonly]="readonly()"
          [required]="required()"
          [invalid]="fromInvalid()"
          [touched]="touchedShown()"
          [dirty]="dirty()"
          [pending]="pending()"
          [errors]="fromErrors()"
          (valueChange)="onEndChange('from', $event)"
        />
      </tm-form-field>
      <tm-form-field class="tm-date-range__part" [label]="toText()" [size]="resolvedSize()">
        <tm-date-picker
          #toPicker
          [value]="parts().to"
          [calendar]="calendar()"
          [minDate]="toMin()"
          [maxDate]="maxDate()"
          [disabled]="disabled()"
          [readonly]="readonly()"
          [required]="required()"
          [invalid]="toErrors().length > 0"
          [touched]="touchedShown()"
          [dirty]="dirty()"
          [pending]="pending()"
          [errors]="toErrors()"
          (valueChange)="onEndChange('to', $event)"
        />
      </tm-form-field>
    </div>
  `,
  styleUrl: './tm-date-range.css',
  host: {
    class: 'tm-date-range',
    role: 'group',
    '[attr.aria-labelledby]': 'label() ? labelId : null',
    '[class.tm-date-range--inline]': 'layout() === "inline"',
    '(focusout)': 'onFocusOut($event)',
  },
})
export class TmDateRange {
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly hostElement = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  // ---- FormValueControl<TmDateRangeValue> + the optional state inputs ----
  /** The range: two ISO dates, either of which may be `null`. */
  readonly value = model<TmDateRangeValue>(EMPTY);
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
  /** The raw framework errors, bound by [formField]; each is shown on the end it concerns. */
  readonly errors = input<readonly ValidationError.WithOptionalFieldTree[]>([]);
  /** Emits when focus leaves the group — touch reporting for the bound field. */
  readonly touch = output<void>();

  // ---- Own API ----
  /** The group's visible label (its accessible name). */
  readonly label = input<string | undefined>(undefined);
  /** The "from" part's label; default "From". */
  readonly fromLabel = input<string | undefined>(undefined);
  /** The "to" part's label; default "To". */
  readonly toLabel = input<string | undefined>(undefined);
  /** `stacked` (the default): one part above the other, as in a filter popover; `inline`: side by side. */
  readonly layout = input<'stacked' | 'inline'>('stacked');
  /** The inclusive lower ISO bound of both pickers. */
  readonly minDate = input<string | undefined>(undefined);
  /** The inclusive upper ISO bound of both pickers. */
  readonly maxDate = input<string | undefined>(undefined);
  /** The pickers' display calendar; default the app-ambient one. */
  readonly calendar = input<TmCalendar | undefined>(undefined);
  /** The size step of both parts; unset, the workspace default (the touch size on a phone). */
  readonly size = input<TmControlSize | undefined>(undefined);

  /** The group label's element id. */
  protected readonly labelId = `tm-date-range-label-${nextUniqueId++}`;
  /** The size both parts render at. */
  protected readonly resolvedSize = ɵtmControlSize(this.size);
  /** The "from" part's label in effect. */
  protected readonly fromText = computed(
    () => this.fromLabel() ?? this.translate('dateRange.from')(),
  );
  /** The "to" part's label in effect. */
  protected readonly toText = computed(() => this.toLabel() ?? this.translate('dateRange.to')());

  private readonly fromPicker = viewChild.required<TmDatePicker>('fromPicker');
  private readonly toPicker = viewChild.required<TmDatePicker>('toPicker');

  /**
   * The raw channel: the two ends as the pickers hold them. Its parse
   * errors (an end's unreadable text) are this control's own, and reach a
   * bound form as such.
   */
  protected readonly parts = transformedValue<TmDateRangeValue, TmDateRangeValue>(this.value, {
    parse: (parts) => this.readParts(parts),
    format: (value) => value ?? EMPTY,
  });

  /** The "from" picker's upper bound: the current "to", within `maxDate`. */
  protected readonly fromMax = computed(() => earlier(this.maxDate(), this.parts().to));
  /** The "to" picker's lower bound: the current "from", within `minDate`. */
  protected readonly toMin = computed(() => later(this.minDate(), this.parts().from));

  /** Touched by this group's own focus leaving, for a control used without a bound state. */
  private readonly touchedSelf = signal(false);
  /** The touched state the parts show errors by. */
  protected readonly touchedShown = computed(() => this.touched() || this.touchedSelf());

  /** Every error shown: the own parse errors, then the bound field's other errors. */
  private readonly allErrors = computed<readonly ValidationError.WithOptionalFieldTree[]>(() => [
    ...this.parts.parseErrors(),
    ...this.errors().filter((error) => error.kind !== 'parse'),
  ]);
  /** The errors shown on the "to" part: the order error and its own parse error. */
  protected readonly toErrors = computed(() =>
    this.allErrors().filter(
      (error) => error.kind === 'dateRangeOrder' || (error as Partial<EndError>).end === 'to',
    ),
  );
  /** The errors shown on the "from" part: every other one. */
  protected readonly fromErrors = computed(() =>
    this.allErrors().filter(
      (error) => error.kind !== 'dateRangeOrder' && (error as Partial<EndError>).end !== 'to',
    ),
  );
  /** Whether the "from" part shows as invalid (an unbound `invalid` marks it when "to" has no error). */
  protected readonly fromInvalid = computed(
    () => this.fromErrors().length > 0 || (this.invalid() && this.toErrors().length === 0),
  );

  constructor() {
    // An end's unreadable text arrives after its value (the picker resets
    // and re-raises its parse errors around its own write), so the range is
    // re-read whenever either end's parse errors change. The first run only
    // records the starting errors.
    let previous: readonly unknown[] | undefined;
    effect(() => {
      const current = [this.fromPicker().parseErrors(), this.toPicker().parseErrors()];
      const changed =
        previous !== undefined && (current[0] !== previous[0] || current[1] !== previous[1]);
      previous = current;
      if (changed) {
        untracked(() => this.parts.set({ ...this.parts() }));
      }
    });
  }

  /** Focuses the "from" part; Signal Forms calls this when asked to focus the field. */
  focus(options?: FocusOptions): void {
    this.fromPicker().focus(options);
  }

  /** An end's value changed: the range is read again. */
  protected onEndChange(end: End, value: string | null): void {
    this.parts.set({ ...untracked(this.parts), [end]: value });
  }

  /** Reports touch when focus leaves the group (not when it moves between the parts). */
  protected onFocusOut(event: FocusEvent): void {
    const next = event.relatedTarget;
    if (!(next instanceof Node) || !this.hostElement.contains(next)) {
      this.touchedSelf.set(true);
      this.touch.emit();
    }
  }

  /**
   * The parts→range read: an end's unreadable text is its own error; the
   * range is written only when an end differs from the bound value, so a
   * re-read writes nothing.
   */
  private readParts(parts: TmDateRangeValue): ParseResult<TmDateRangeValue> {
    const errors: EndError[] = [
      ...this.endErrors('from', untracked(this.fromPicker).parseErrors()),
      ...this.endErrors('to', untracked(this.toPicker).parseErrors()),
    ];
    const bound = untracked(this.value) ?? EMPTY;
    const unchanged = bound.from === parts.from && bound.to === parts.to;
    return {
      ...(unchanged ? {} : { value: { from: parts.from, to: parts.to } }),
      ...(errors.length > 0 ? { error: errors } : {}),
    };
  }

  /** An end's own parse errors, tagged with the end. */
  private endErrors(end: End, errors: readonly ValidationError.WithoutFieldTree[]): EndError[] {
    return errors.map((error) => ({ ...error, end }) as EndError);
  }
}

/** The earlier of two optional ISO dates. */
function earlier(a: string | undefined, b: string | null): string | undefined {
  return b === null ? a : a === undefined || b < a ? b : a;
}

/** The later of two optional ISO dates. */
function later(a: string | undefined, b: string | null): string | undefined {
  return b === null ? a : a === undefined || b > a ? b : a;
}
