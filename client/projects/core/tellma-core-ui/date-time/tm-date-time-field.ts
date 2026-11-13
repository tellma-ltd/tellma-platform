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

import {
  TM_NOW,
  TM_UI_TRANSLATE,
  TmL10n,
  ɵtmControlSize,
  type TmControlSize,
} from '@tellma/core-ui';
import { TmDatePicker } from '@tellma/core-ui/date-picker';
import { TmFormField } from '@tellma/core-ui/form-field';
import { tmFormatZoneOffset, type TmCalendar } from '@tellma/core-ui/l10n';

import {
  ɵtmFormatOffsetInstant,
  ɵtmInstantOfWallTime,
  ɵtmReadInstant,
  ɵtmWallTimeOf,
} from './internal/tm-zone-wall-time';
import { TmTimeField } from './tm-time-field';

let nextUniqueId = 0;

/** The two parts the field edits: a date and a time of day, each possibly empty. */
interface DateTimeParts {
  /** The ISO date, or `null` while empty. */
  readonly date: string | null;
  /** The `HH:mm` time of day, or `null` while empty. */
  readonly time: string | null;
}

/** Which part an error belongs to, carried on the errors this field raises itself. */
type Part = 'date' | 'time';

/** An error this field raises itself, tagged with the part it is shown on. */
type PartError = ValidationError.WithoutFieldTree & { readonly part: Part };

/** The error kinds this field's own parse raises (the bound field echoes them back). */
const OWN_KINDS: ReadonlySet<string> = new Set(['dateTimeIncomplete', 'parse']);

/** Milliseconds in a minute: instants are compared at the minute the parts show. */
const MINUTE = 60_000;

/**
 * Edits an instant as a date and a time of day in an explicit time zone,
 * and always names the zone: a `role="group"` holding a field with a
 * `tm-date-picker`, a field with a `tm-time-field`, and the zone's offset
 * ("GMT+3") at the inline end, where a change of its text moves nothing
 * before it.
 *
 * The two parts are read as wall time in `timeZone`:
 *
 * - a time that does not exist (inside a forward daylight-saving change)
 *   becomes the earliest valid instant after it, the change itself;
 * - a time that occurs twice (inside a backward change) takes the earlier
 *   instant;
 * - one part without the other is the `dateTimeIncomplete` error, shown on
 *   the empty part, and the value is `null`.
 *
 * The zone label shows the zone's offset at the instant, or at the current
 * time while the value is empty; it never hides, because "2 pm" means
 * different instants to people in different zones.
 *
 * The value is written only when the instant the parts read differs from
 * the bound value at the minute the parts show: a blur, a reformat or a
 * calendar pick that re-reads the bound instant writes nothing, so a
 * server value such as `2026-11-20T11:00:00.0000000+00:00` stays as sent.
 * A write is always `yyyy-MM-ddTHH:mm:ss±HH:mm`: the zone's offset at that
 * instant, and no fraction.
 *
 * Place it directly, never inside a `tm-form-field`. Signal Forms native:
 * implements `FormValueControl<string | null>`; reports touch when focus
 * leaves the group.
 *
 * @tmGroup form-control
 * @tmA11yNotes A labelled group of two native text inputs (one tab stop
 *   each, plus the date picker's button); the date picker keeps its keys;
 *   Up and Down step the time; the zone label is plain text in the group.
 */
@Component({
  selector: 'tm-date-time-field',
  imports: [TmDatePicker, TmFormField, TmTimeField],
  template: `
    @if (label(); as label) {
      <span class="tm-date-time-field__label" [id]="labelId">{{ label }}</span>
    }
    <!-- The row: the date field, the time field, then the zone label at the
         inline end, aligned with the input boxes. -->
    <div class="tm-date-time-field__row">
      <tm-form-field class="tm-date-time-field__date" [label]="dateText()" [size]="resolvedSize()">
        <tm-date-picker
          [value]="parts().date"
          [calendar]="calendar()"
          [minDate]="minDate()"
          [maxDate]="maxDate()"
          [disabled]="disabled()"
          [readonly]="readonly()"
          [required]="required()"
          [invalid]="dateInvalid()"
          [touched]="touchedShown()"
          [dirty]="dirty()"
          [pending]="pending()"
          [errors]="dateErrors()"
          (valueChange)="onPartChange('date', $event)"
          (picked)="syncParts()"
          (touch)="syncParts()"
        />
      </tm-form-field>
      <tm-form-field class="tm-date-time-field__time" [label]="timeText()" [size]="resolvedSize()">
        <tm-time-field
          [value]="parts().time"
          [disabled]="disabled()"
          [readonly]="readonly()"
          [required]="required()"
          [invalid]="timeInvalid()"
          [touched]="touchedShown()"
          [dirty]="dirty()"
          [pending]="pending()"
          [errors]="timeErrors()"
          [ɵclockZone]="zone()"
          (valueChange)="onPartChange('time', $event)"
          (touch)="syncParts()"
        />
      </tm-form-field>
      <span class="tm-date-time-field__zone" [attr.data-zone]="zone()">{{ zoneLabel() }}</span>
    </div>
  `,
  styleUrl: './tm-date-time-field.css',
  host: {
    class: 'tm-date-time-field',
    role: 'group',
    '[attr.aria-labelledby]': 'label() ? labelId : null',
    '[class.tm-date-time-field--sm]': 'resolvedSize() === "sm"',
    '[class.tm-date-time-field--lg]': 'resolvedSize() === "lg"',
    '(focusout)': 'onFocusOut($event)',
  },
})
export class TmDateTimeField {
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly now = inject(TM_NOW);
  /** The reactive formatting facade (the active locale and the display zone). */
  private readonly l10n = inject(TmL10n);
  private readonly hostElement = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  // ---- FormValueControl<string | null> + the optional state inputs ----
  /** The instant: ISO 8601 with the zone's offset at it; `null` when both parts are empty. */
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
  /** The raw framework errors, bound by [formField]; each is shown on the part it concerns. */
  readonly errors = input<readonly ValidationError.WithOptionalFieldTree[]>([]);
  /** Emits when focus leaves the group — touch reporting for the bound field. */
  readonly touch = output<void>();

  // ---- Own API ----
  /** The IANA time zone the parts are read in; default the display zone (`TM_TIME_ZONE`). */
  readonly timeZone = input<string | undefined>(undefined);
  /** The date picker's display calendar; default the app-ambient one. */
  readonly calendar = input<TmCalendar | undefined>(undefined);
  /** The group's visible label (its accessible name). */
  readonly label = input<string | undefined>(undefined);
  /** The date part's label; default "Date". */
  readonly dateLabel = input<string | undefined>(undefined);
  /** The time part's label; default "Time". */
  readonly timeLabel = input<string | undefined>(undefined);
  /** The inclusive lower ISO date bound of the date picker. */
  readonly minDate = input<string | undefined>(undefined);
  /** The inclusive upper ISO date bound of the date picker. */
  readonly maxDate = input<string | undefined>(undefined);
  /** The size step of both parts; unset, the workspace default (the touch size on a phone). */
  readonly size = input<TmControlSize | undefined>(undefined);

  /** The group label's element id. */
  protected readonly labelId = `tm-date-time-field-label-${nextUniqueId++}`;
  /** The size both parts render at. */
  protected readonly resolvedSize = ɵtmControlSize(this.size);
  /** The zone in effect: the input, else the display zone. */
  protected readonly zone = computed(() => this.timeZone() ?? this.l10n.timeZone());
  /** The date part's label in effect. */
  protected readonly dateText = computed(
    () => this.dateLabel() ?? this.translate('dateTime.date')(),
  );
  /** The time part's label in effect. */
  protected readonly timeText = computed(
    () => this.timeLabel() ?? this.translate('dateTime.time')(),
  );

  private readonly datePicker = viewChild.required(TmDatePicker);
  private readonly timeField = viewChild.required(TmTimeField);

  /**
   * The raw channel: the two parts, read as one instant. Its parse errors
   * (an incomplete pair, or a part's unreadable text) are this field's
   * own, and reach a bound form as such.
   */
  protected readonly parts = transformedValue<string | null, DateTimeParts>(this.value, {
    parse: (parts) => this.readParts(parts),
    format: (value) => this.partsOf(value),
  });

  /** Touched by this group's own focus leaving, for a field used without a bound state. */
  private readonly touchedSelf = signal(false);
  /** The touched state the parts show errors by. */
  protected readonly touchedShown = computed(() => this.touched() || this.touchedSelf());

  /**
   * Every error the field shows: its own parse errors, then the bound
   * field's other errors (the bound field echoes the own ones back, which
   * are dropped there by kind).
   */
  private readonly allErrors = computed<readonly ValidationError.WithOptionalFieldTree[]>(() => [
    ...this.parts.parseErrors(),
    ...this.errors().filter((error) => !OWN_KINDS.has(error.kind)),
  ]);
  /** The errors shown on the date part: its own, and every error not about the time. */
  protected readonly dateErrors = computed(() =>
    this.allErrors().filter((error) => (error as Partial<PartError>).part !== 'time'),
  );
  /** The errors shown on the time part: the incomplete pair's when the time is missing, and its parse error. */
  protected readonly timeErrors = computed(() =>
    this.allErrors().filter((error) => (error as Partial<PartError>).part === 'time'),
  );
  /** Whether the date part shows as invalid (an unbound `invalid` marks it when the time has no error). */
  protected readonly dateInvalid = computed(
    () => this.dateErrors().length > 0 || (this.invalid() && this.timeErrors().length === 0),
  );
  /** Whether the time part shows as invalid. */
  protected readonly timeInvalid = computed(() => this.timeErrors().length > 0);

  /**
   * The zone's offset in the locale's words: at the instant, or at the
   * current time while the value is empty.
   */
  protected readonly zoneLabel = computed(() => {
    const at = ɵtmReadInstant(this.value()) ?? this.now();
    return tmFormatZoneOffset(at, this.l10n.locale(), this.zone());
  });

  constructor() {
    // A part's unreadable text arrives after its value (the child resets
    // and re-raises its parse errors around its own write), so the pair is
    // re-read whenever either part's parse errors change.
    // The first run only records the starting errors: re-reading the
    // parts unprompted could rewrite a bound value nobody touched.
    let previous: readonly unknown[] | undefined;
    effect(() => {
      const current = [this.datePicker().parseErrors(), this.timeField().parseErrors()];
      const changed =
        previous !== undefined && (current[0] !== previous[0] || current[1] !== previous[1]);
      previous = current;
      if (changed) {
        untracked(() => this.parts.set({ ...this.parts() }));
      }
    });
    // A zone change shows the same instant on the new zone's clock; the
    // value never changes.
    let previousZone: string | undefined;
    effect(() => {
      const zone = this.zone();
      if (previousZone !== undefined && previousZone !== zone) {
        untracked(() => this.syncParts());
      }
      previousZone = zone;
    });
  }

  /** Focuses the date part; Signal Forms calls this when asked to focus the field. */
  focus(options?: FocusOptions): void {
    this.datePicker().focus(options);
  }

  /** A part's value changed: the pair is read again. */
  protected onPartChange(part: Part, value: string | null): void {
    this.parts.set({ ...untracked(this.parts), [part]: value });
  }

  /**
   * Shows the parts of the current value again (a part blurred, the
   * calendar committed, or the zone changed): a time inside a forward
   * daylight-saving change reads back as the change itself. Re-reading the
   * same instant writes nothing.
   */
  protected syncParts(): void {
    const value = untracked(this.value);
    if (value === null || ɵtmReadInstant(value) === null) {
      return;
    }
    const shown = this.partsOf(value);
    const current = untracked(this.parts);
    if (shown.date !== current.date || shown.time !== current.time) {
      this.parts.set(shown);
    }
  }

  /** Reports touch when focus leaves the group (not when it moves between the parts). */
  protected onFocusOut(event: FocusEvent): void {
    const next = event.relatedTarget;
    if (!(next instanceof Node) || !this.hostElement.contains(next)) {
      this.touchedSelf.set(true);
      this.touch.emit();
    }
  }

  /** The parts an instant shows in the zone; an empty or unreadable value shows none. */
  private partsOf(value: string | null): DateTimeParts {
    const at = ɵtmReadInstant(value);
    if (at === null) {
      return { date: null, time: null };
    }
    return ɵtmWallTimeOf(at, untracked(this.zone));
  }

  /**
   * The parts→instant read: a part's unreadable text is its own error;
   * both empty is `null`; one empty is `dateTimeIncomplete`, on the empty
   * part; both present is the instant they name, written only when it
   * differs from the bound value at the minute the parts show.
   */
  private readParts(parts: DateTimeParts): ParseResult<string | null> {
    const partErrors: PartError[] = [
      ...this.partErrors('date', untracked(this.datePicker).parseErrors()),
      ...this.partErrors('time', untracked(this.timeField).parseErrors()),
    ];
    if (partErrors.length > 0) {
      return { value: null, error: partErrors };
    }
    if (parts.date === null && parts.time === null) {
      return { value: null };
    }
    if (parts.date === null || parts.time === null) {
      const error: PartError = {
        kind: 'dateTimeIncomplete',
        part: parts.date === null ? 'date' : 'time',
      } as PartError;
      return { value: null, error };
    }
    const zone = untracked(this.zone);
    const at = ɵtmInstantOfWallTime({ date: parts.date, time: parts.time }, zone);
    const bound = ɵtmReadInstant(untracked(this.value));
    if (bound !== null && Math.floor(bound / MINUTE) * MINUTE === at) {
      return {}; // the bound instant, re-read: no write
    }
    return { value: ɵtmFormatOffsetInstant(at, zone) };
  }

  /** A part's own parse errors, tagged with the part. */
  private partErrors(part: Part, errors: readonly ValidationError.WithoutFieldTree[]): PartError[] {
    return errors.map((error) => ({ ...error, part }) as PartError);
  }
}
