// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The calendar popup: a non-modal role="dialog" implementing the APG
// date-picker-dialog pattern — day grid → month grid → year grid, cycled
// by the header button; the popup takes real focus on open, so arrow keys
// navigate days only while focus is in the calendar.

import {
  afterNextRender,
  afterRenderEffect,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { Directionality } from '@angular/cdk/bidi';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';
import {
  tmFirstDayOfWeek,
  ɵtmFormatWithoutEra,
  ɵtmParseIsoDate,
  type TmCalendar,
  type TmCalendarParts,
} from '@tellma/core-ui/l10n';

/** One cell of the month view; `null` pads the fixed five-row layout. */
type MonthCell = { month: number; label: string; disabled: boolean } | null;

/** The hard ISO window (the intersection of ISO 8601 with SQL/BCL dates). */
const MIN_ISO = '0001-01-01';
const MAX_ISO = '9999-12-31';

/** One cell of the day grid. */
interface DayCell {
  /** The 1-based in-calendar day, or null for a blank (out-of-month) cell. */
  readonly day: number | null;
  /** The cell's ISO date (null for blanks). */
  readonly iso: string | null;
  /** Whether the cell is outside the min/max/hard bounds. */
  readonly disabled: boolean;
  /** Whether the cell is the user's local today. */
  readonly isToday: boolean;
  /** Whether the cell is the field's committed value. */
  readonly isSelected: boolean;
  /** Whether the cell holds the roving focus. */
  readonly isFocused: boolean;
}

/**
 * The date-picker's calendar dialog (internal). The parent owns the
 * overlay and the open state; this component owns the views, the roving
 * focus, and the APG keyboard model. Selection at day level emits
 * `selected`; `Esc` emits `cancelled` after consuming the key (stage one
 * of a grid's two-stage Esc).
 */
@Component({
  selector: 'tm-date-popup',
  template: `
    <div
      class="tm-date-popup"
      role="dialog"
      [attr.aria-label]="dialogLabel()"
      (keydown)="onKeydown($event)"
    >
      <div class="tm-date-popup__header">
        <button
          type="button"
          class="tm-date-popup__nav"
          [attr.aria-label]="prevLabel()"
          (click)="page(-1)"
        >
          <svg viewBox="0 0 16 16" fill="none" aria-hidden="true" class="tm-date-popup__nav-glyph">
            <polyline
              points="10,3 5,8 10,13"
              stroke="currentColor"
              stroke-width="1.5"
              stroke-linecap="round"
              stroke-linejoin="round"
            />
          </svg>
        </button>
        <button
          type="button"
          class="tm-date-popup__view-switch"
          [attr.aria-label]="switchLabel() + ', ' + headerLabel()"
          (click)="cycleView()"
        >
          <span aria-live="polite">{{ headerLabel() }}</span>
        </button>
        <button
          type="button"
          class="tm-date-popup__nav"
          [attr.aria-label]="nextLabel()"
          (click)="page(1)"
        >
          <svg viewBox="0 0 16 16" fill="none" aria-hidden="true" class="tm-date-popup__nav-glyph">
            <polyline
              points="6,3 11,8 6,13"
              stroke="currentColor"
              stroke-width="1.5"
              stroke-linecap="round"
              stroke-linejoin="round"
            />
          </svg>
        </button>
      </div>

      @switch (view()) {
        @case ('day') {
          <div class="tm-date-popup__grid" role="grid" [attr.aria-label]="headerLabel()">
            <div class="tm-date-popup__weekdays" role="row">
              @for (name of weekdayNames(); track $index) {
                <span class="tm-date-popup__weekday" role="columnheader">{{ name }}</span>
              }
            </div>
            @for (week of dayGrid(); track $index) {
              <div class="tm-date-popup__week" role="row">
                @for (cell of week; track $index) {
                  @if (cell.day === null) {
                    <span class="tm-date-popup__cell tm-date-popup__cell--blank" role="gridcell"></span>
                  } @else {
                    <button
                      type="button"
                      class="tm-date-popup__cell tm-date-popup__day"
                      role="gridcell"
                      [tabindex]="cell.isFocused ? 0 : -1"
                      [class.tm-date-popup__day--selected]="cell.isSelected"
                      [class.tm-date-popup__day--today]="cell.isToday"
                      [disabled]="cell.disabled"
                      [attr.aria-disabled]="cell.disabled ? 'true' : null"
                      [attr.aria-selected]="cell.isSelected ? 'true' : 'false'"
                      [attr.aria-current]="cell.isToday ? 'date' : null"
                      [attr.aria-label]="cellLabel(cell)"
                      [attr.data-tm-day]="cell.day"
                      (click)="selectDay(cell)"
                    >
                      {{ dayLabel(cell.day) }}
                    </button>
                  }
                }
              </div>
            }
          </div>
        }
        @case ('month') {
          <div class="tm-date-popup__months" role="grid" [attr.aria-label]="headerLabel()">
            @for (row of monthGrid(); track $index) {
              <div class="tm-date-popup__month-row" role="row">
                @for (cell of row; track $index) {
                  @if (cell === null) {
                    <span class="tm-date-popup__cell tm-date-popup__cell--blank" role="gridcell"></span>
                  } @else {
                    <button
                      type="button"
                      class="tm-date-popup__cell tm-date-popup__month"
                      role="gridcell"
                      [tabindex]="cell.month === focused().month ? 0 : -1"
                      [disabled]="cell.disabled"
                      [attr.aria-disabled]="cell.disabled ? 'true' : null"
                      [attr.aria-selected]="
                        cell.month === committedMonth() && focused().year === committedYear()
                          ? 'true'
                          : 'false'
                      "
                      [attr.data-tm-month]="cell.month"
                      (click)="selectMonth(cell.month)"
                    >
                      {{ cell.label }}
                    </button>
                  }
                }
              </div>
            }
          </div>
        }
        @case ('year') {
          <div class="tm-date-popup__years" role="grid" [attr.aria-label]="headerLabel()">
            @for (row of yearGrid(); track $index) {
              <div class="tm-date-popup__year-row" role="row">
                @for (cell of row; track $index) {
                  <button
                    type="button"
                    class="tm-date-popup__cell tm-date-popup__year"
                    role="gridcell"
                    [tabindex]="cell.year === focused().year ? 0 : -1"
                    [disabled]="cell.disabled"
                    [attr.aria-disabled]="cell.disabled ? 'true' : null"
                    [attr.aria-selected]="cell.year === committedYear() ? 'true' : 'false'"
                    [attr.data-tm-year]="cell.year"
                    (click)="selectYear(cell.year)"
                  >
                    {{ cell.label }}
                  </button>
                }
              </div>
            }
          </div>
        }
      }

      <div class="tm-date-popup__footer">
        <button
          type="button"
          class="tm-date-popup__action"
          [disabled]="todayDisabled()"
          [attr.aria-disabled]="todayDisabled() ? 'true' : null"
          (click)="selectToday()"
        >
          {{ todayLabel() }}
        </button>
        <button type="button" class="tm-date-popup__action" (click)="clear()">
          {{ clearLabel() }}
        </button>
      </div>
    </div>
  `,
  styleUrl: './tm-date-popup.css',
  host: { class: 'tm-date-popup-host' },
})
export class ɵTmDatePopup {
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly directionality = inject(Directionality);
  private readonly hostElement = inject(ElementRef).nativeElement as HTMLElement;

  /** The display calendar. */
  readonly calendar = input.required<TmCalendar>();
  /** The formatting locale. */
  readonly locale = input.required<string>();
  /** The field's committed ISO value, or null. */
  readonly value = input<string | null>(null);
  /** The inclusive lower ISO bound. */
  readonly min = input<string | undefined>(undefined);
  /** The inclusive upper ISO bound. */
  readonly max = input<string | undefined>(undefined);

  /** A day (or Today/Clear) was chosen; the parent commits and closes. */
  readonly selected = output<string | null>();
  /** Esc — close without committing. */
  readonly cancelled = output<void>();
  /**
   * The first render completed at full size. The parent's overlay attached
   * around the `@defer` placeholder (near-zero height, so CDK kept the
   * primary below-the-field position even at the viewport bottom) and must
   * re-measure now or the popup can hang past the fold.
   */
  readonly rendered = output<void>();

  /**
   * The committed value's month/year in display-calendar terms, or null
   * when there is none. The month and year views are DRILL-DOWN
   * navigation, so `aria-selected` there must track the committed value —
   * announcing the merely-focused cell as selected would tell a screen
   * reader the value changed on every arrow press.
   */
  private readonly committedParts = computed<TmCalendarParts | null>(() => {
    const value = this.value();
    return value === null ? null : this.calendar().toParts(value);
  });
  /**
   * Whether today lies outside the effective bounds. Committing a CLAMPED
   * bound under a button labelled "Today" would write a date the user
   * never chose, so the affordance is disabled instead.
   */
  protected readonly todayDisabled = computed(() => {
    const today = this.calendar().today();
    return today < this.lowerBound() || today > this.upperBound();
  });

  protected readonly committedMonth = computed(() => this.committedParts()?.month ?? null);
  protected readonly committedYear = computed(() => this.committedParts()?.year ?? null);

  /** The active view of the ladder. Always opens on the day view. */
  protected readonly view = signal<'day' | 'month' | 'year'>('day');
  /** The roving-focus position, in display-calendar parts. */
  protected readonly focused = signal<TmCalendarParts>({ year: 1, month: 1, day: 1 });
  /** Focus the roving cell after the next render (open, navigation). */
  private readonly pendingFocus = signal(0);

  protected readonly dialogLabel = computed(() => this.translate('datePicker.dialogLabel')());
  protected readonly todayLabel = computed(() => this.translate('datePicker.today')());
  protected readonly clearLabel = computed(() => this.translate('datePicker.clear')());
  protected readonly prevLabel = computed(() =>
    this.translate('datePicker.previous', { view: this.view() })(),
  );
  protected readonly nextLabel = computed(() =>
    this.translate('datePicker.next', { view: this.view() })(),
  );
  protected readonly switchLabel = computed(() =>
    this.translate('datePicker.switchView', { view: this.view() })(),
  );

  /** The effective bounds: min/max intersected with the hard ISO window. */
  private readonly lowerBound = computed(() => {
    const min = this.min();
    return min !== undefined && min > MIN_ISO ? min : MIN_ISO;
  });
  private readonly upperBound = computed(() => {
    const max = this.max();
    return max !== undefined && max < MAX_ISO ? max : MAX_ISO;
  });

  constructor() {
    afterNextRender(() => this.rendered.emit());

    // Open on the committed value, else today — clamped into bounds.
    effect(() => {
      const calendar = this.calendar();
      const value = this.value();
      untracked(() => {
        const iso = this.clampIso(value ?? calendar.today());
        this.focused.set(calendar.toParts(iso));
      });
    });

    // Focus follows the roving cell: on open and after every navigation.
    afterRenderEffect(() => {
      // ONLY the explicit request moves focus. Tracking `focused()` here
      // would also fire for the header's paging buttons, yanking focus
      // out of the button the user is clicking — so a second Enter would
      // do nothing until they Tab back. Every path that genuinely wants
      // focus in the grid (open, arrow keys, drilling down a view) bumps
      // `pendingFocus` itself.
      this.pendingFocus();
      untracked(() => {
        const target = this.hostElement.querySelector<HTMLElement>('[tabindex="0"]');
        // A disabled (or absent) roving cell would silently drop focus to
        // <body>, where the dialog's own Escape handler no longer hears
        // anything — fall back to the header so focus stays in the popup.
        if (target !== null && !(target as HTMLButtonElement).disabled) {
          target.focus();
          return;
        }
        this.hostElement
          .querySelector<HTMLElement>('.tm-date-popup__view-switch:not([disabled])')
          ?.focus();
      });
    });
  }

  // ---- Header ----

  /**
   * A day cell's accessible name: the full localized date. The visible
   * text is a bare number, which out of the grid's visual context tells a
   * screen-reader user nothing about which month or year they are in.
   */
  protected cellLabel(cell: DayCell): string | null {
    const iso = cell.iso;
    if (iso === null) {
      return null;
    }
    const calendar = this.calendar();
    return ɵtmFormatWithoutEra(
      new Intl.DateTimeFormat(this.locale(), {
        dateStyle: 'long',
        calendar: calendar.id,
        timeZone: 'UTC',
      }),
      this.utcOf(iso),
    );
  }

  /** The header label: "month year" (day view), "year", or the 24-year range. */
  protected readonly headerLabel = computed(() => {
    const calendar = this.calendar();
    const locale = this.locale();
    const focused = this.focused();
    if (this.view() === 'day') {
      const iso = calendar.fromParts({ ...focused, day: 1 }) ?? calendar.today();
      return ɵtmFormatWithoutEra(
        new Intl.DateTimeFormat(locale, {
          year: 'numeric',
          month: 'long',
          calendar: calendar.id,
          timeZone: 'UTC',
        }),
        this.utcOf(iso),
      );
    }
    if (this.view() === 'month') {
      return this.yearName(focused.year);
    }
    const base = this.yearBlockStart(focused.year);
    return `${this.yearName(base)}–${this.yearName(base + 23)}`;
  });

  /** The localized year label (calendar numbering, locale digits). */
  private yearName(year: number): string {
    const calendar = untracked(this.calendar);
    const iso = calendar.fromParts({ year, month: 1, day: 1 });
    if (iso === null) {
      return String(year);
    }
    return ɵtmFormatWithoutEra(
      new Intl.DateTimeFormat(untracked(this.locale), {
        year: 'numeric',
        calendar: calendar.id,
        timeZone: 'UTC',
      }),
      this.utcOf(iso),
    );
  }

  private utcOf(iso: string): Date {
    const parts = ɵtmParseIsoDate(iso)!;
    const date = new Date(Date.UTC(2000, parts.month - 1, parts.day, 12));
    date.setUTCFullYear(parts.year);
    return date;
  }

  /** Cycles day → month → year → day (the view-switch header button). */
  protected cycleView(): void {
    this.view.update((view) => (view === 'day' ? 'month' : view === 'month' ? 'year' : 'day'));
    this.pendingFocus.update((n) => n + 1);
  }

  /** Pages the active view: ±1 month, ±1 year, or ±24 years. */
  protected page(direction: 1 | -1): void {
    const view = untracked(this.view);
    if (view === 'day') {
      this.moveMonths(direction);
    } else if (view === 'month') {
      this.moveYears(direction);
    } else {
      this.moveYears(direction * 24);
    }
    // Focus stays on the pressed button so it can be pressed again —
    // paging is the button's job, not a request to enter the grid.
  }

  // ---- Day view ----

  /**
   * Weekday header names, ordered from the locale's first day of week.
   *
   * `short` where CLDR actually abbreviates (`Sun`), `narrow` where it
   * does not — Arabic's short form IS the full name (`الأحد`), which
   * would force the columns far wider than the cells they head. The
   * choice is made from the data, never from a hand-written table: the
   * engine ships no name lists.
   */
  protected readonly weekdayNames = computed(() => {
    const locale = this.locale();
    const first = tmFirstDayOfWeek(locale);
    // 2024-01-01 is a Monday (ISO day 1).
    const dayOf = (isoDay: number): Date => new Date(Date.UTC(2024, 0, isoDay, 12));
    const named = (weekday: 'long' | 'short' | 'narrow'): string[] => {
      const formatter = new Intl.DateTimeFormat(locale, { weekday, timeZone: 'UTC' });
      return Array.from({ length: 7 }, (_, i) => formatter.format(dayOf(((first - 1 + i) % 7) + 1)));
    };
    const short = named('short');
    const long = named('long');
    return short.every((name, i) => name === long[i]) ? named('narrow') : short;
  });

  /**
   * The FIXED six-week day grid of the focused display-calendar month.
   * Sizing to the month instead would move everything the user is aiming
   * at: a popup that opens upward keeps its bottom edge, so its header
   * arrows would jump vertically on every page, and one that fitted the
   * viewport when it opened could grow past the edge. A blank row on a
   * short month is the cheaper compromise.
   */
  protected readonly dayGrid = computed<DayCell[][]>(() => {
    const calendar = this.calendar();
    const focused = this.focused();
    const value = this.value();
    const today = calendar.today();
    const lower = this.lowerBound();
    const upper = this.upperBound();
    const firstOfWeek = tmFirstDayOfWeek(this.locale());
    const days = calendar.daysInMonth(focused.year, focused.month);
    const firstIso = calendar.fromParts({ year: focused.year, month: focused.month, day: 1 });
    // The month's first day's ISO weekday (1 = Monday … 7 = Sunday).
    let leading = 0;
    if (firstIso !== null) {
      const utcDay = this.utcOf(firstIso).getUTCDay(); // 0 = Sunday
      const isoWeekday = utcDay === 0 ? 7 : utcDay;
      leading = (isoWeekday - firstOfWeek + 7) % 7;
    }
    const cells: DayCell[] = [];
    for (let i = 0; i < 42; i++) {
      const day = i - leading + 1;
      if (day < 1 || day > days) {
        cells.push({
          day: null,
          iso: null,
          disabled: true,
          isToday: false,
          isSelected: false,
          isFocused: false,
        });
        continue;
      }
      const iso = calendar.fromParts({ year: focused.year, month: focused.month, day });
      cells.push({
        day,
        iso,
        disabled: iso === null || iso < lower || iso > upper,
        isToday: iso === today,
        isSelected: iso !== null && iso === value,
        isFocused: day === focused.day,
      });
    }
    const weeks: DayCell[][] = [];
    for (let i = 0; i < 6; i++) {
      weeks.push(cells.slice(i * 7, i * 7 + 7));
    }
    return weeks;
  });

  /** The localized digits of an in-calendar day number. */
  protected dayLabel(day: number): string {
    return new Intl.NumberFormat(untracked(this.locale), { useGrouping: false }).format(day);
  }

  /** A day cell was activated: emit at day level (the commit). */
  protected selectDay(cell: DayCell): void {
    if (!cell.disabled && cell.iso !== null) {
      this.selected.emit(cell.iso);
    }
  }

  // ---- Month view ----

  /** The month grid (three per row; 13-capable — Pagume is selectable). */
  protected readonly monthGrid = computed<MonthCell[][]>(() => {
    const calendar = this.calendar();
    const locale = this.locale();
    const focused = this.focused();
    const months = calendar.monthsInYear(focused.year);
    const formatter = new Intl.DateTimeFormat(locale, {
      month: 'short',
      calendar: calendar.id,
      timeZone: 'UTC',
    });
    const lower = this.lowerBound();
    const upper = this.upperBound();
    const cells: MonthCell[] = [];
    for (let month = 1; month <= months; month++) {
      const iso = calendar.fromParts({ year: focused.year, month, day: 1 });
      // Out of range only when the WHOLE month lies outside the bounds —
      // the day view still disables the individual days at the edges.
      const last = calendar.fromParts({
        year: focused.year,
        month,
        day: calendar.daysInMonth(focused.year, month),
      });
      cells.push({
        month,
        label: iso === null ? String(month) : ɵtmFormatWithoutEra(formatter, this.utcOf(iso)),
        // `last === null` means the month's END is past what the calendar
        // can represent (the ISO ceiling) — its earlier days are still
        // reachable, so that alone must not disable it.
        disabled: iso === null || iso > upper || (last !== null && last < lower),
      });
    }
    // A fixed five-row layout holds 13 months (Ethiopic) without a size
    // change between years or calendars; short years pad with blanks.
    while (cells.length < 15) {
      cells.push(null);
    }
    const rows: MonthCell[][] = [];
    for (let i = 0; i < 5; i++) {
      rows.push(cells.slice(i * 3, i * 3 + 3));
    }
    return rows;
  });

  /** A month drills down to the day view — never past the bounds. */
  protected selectMonth(month: number): void {
    this.focused.update((focused) =>
      this.clampParts(this.constrainDay({ ...focused, month }), Math.sign(month - focused.month)),
    );
    this.view.set('day');
    this.pendingFocus.update((n) => n + 1);
  }

  // ---- Year view ----

  /** The fixed 24-year grid, paged in 24-year blocks. */
  protected readonly yearGrid = computed<{ year: number; label: string; disabled: boolean }[][]>(
    () => {
      const calendar = this.calendar();
      const focused = this.focused();
      const lower = calendar.toParts(this.lowerBound()).year;
      const upper = calendar.toParts(this.upperBound()).year;
      const base = this.yearBlockStart(focused.year);
      const cells = Array.from({ length: 24 }, (_, i) => {
        const year = base + i;
        return {
          year,
          label: this.yearName(year),
          disabled: year < lower || year > upper,
        };
      });
      const rows: { year: number; label: string; disabled: boolean }[][] = [];
      for (let i = 0; i < 6; i++) {
        rows.push(cells.slice(i * 4, i * 4 + 4));
      }
      return rows;
    },
  );

  private yearBlockStart(year: number): number {
    return Math.floor((year - 1) / 24) * 24 + 1;
  }

  /** A year drills down to the month view. */
  protected selectYear(year: number): void {
    this.focused.update((focused) => this.constrainDay({ ...focused, year }));
    this.view.set('month');
    this.pendingFocus.update((n) => n + 1);
  }

  // ---- Footer ----

  /**
   * Keeps Tab inside the dialog (the APG dialog pattern wraps). Tabbing
   * out would leave the calendar open over unrelated content while the
   * input still reports itself expanded — the state a screen reader
   * announces would stop matching what is on screen.
   */
  private wrapTab(event: KeyboardEvent): void {
    const stops = [...this.hostElement.querySelectorAll<HTMLElement>('button:not([disabled])')];
    const first = stops[0];
    const last = stops[stops.length - 1];
    if (first === undefined || last === undefined) {
      return;
    }
    // The roving grid exposes ONE cell as a tab stop, so the focused
    // element is always one of `stops` while focus is inside.
    const active = document.activeElement;
    if (event.shiftKey && active === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  }

  /** Today: commit the user's local date, or nothing when out of bounds. */
  protected selectToday(): void {
    if (untracked(this.todayDisabled)) {
      return;
    }
    this.selected.emit(untracked(this.calendar).today());
  }

  /** Clear: commit null. */
  protected clear(): void {
    this.selected.emit(null);
  }

  // ---- Keyboard ----

  /** The APG keyboard model; Esc is consumed here (stage-1 of a grid's two-stage Esc). */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.cancelled.emit();
      return;
    }
    if (event.key === 'Tab') {
      this.wrapTab(event);
      return;
    }
    if (untracked(this.view) !== 'day') {
      this.onLadderKeydown(event);
      return;
    }
    const rtl = this.directionality.value === 'rtl';
    switch (event.key) {
      case 'ArrowLeft':
        this.moveDays(rtl ? 1 : -1);
        break;
      case 'ArrowRight':
        this.moveDays(rtl ? -1 : 1);
        break;
      case 'ArrowUp':
        this.moveDays(-7);
        break;
      case 'ArrowDown':
        this.moveDays(7);
        break;
      case 'Home':
        this.moveToWeekEdge('start');
        break;
      case 'End':
        this.moveToWeekEdge('end');
        break;
      case 'PageUp':
        if (event.shiftKey) {
          this.moveYears(-1);
        } else {
          this.moveMonths(-1);
        }
        break;
      case 'PageDown':
        if (event.shiftKey) {
          this.moveYears(1);
        } else {
          this.moveMonths(1);
        }
        break;
      default:
        return;
    }
    event.preventDefault();
    this.pendingFocus.update((n) => n + 1);
  }

  /** Arrow navigation in the month/year drill-down grids. */
  private onLadderKeydown(event: KeyboardEvent): void {
    const view = untracked(this.view);
    const columns = view === 'month' ? 3 : 4;
    const rtl = this.directionality.value === 'rtl';
    let delta: number;
    switch (event.key) {
      case 'ArrowLeft':
        delta = rtl ? 1 : -1;
        break;
      case 'ArrowRight':
        delta = rtl ? -1 : 1;
        break;
      case 'ArrowUp':
        delta = -columns;
        break;
      case 'ArrowDown':
        delta = columns;
        break;
      default:
        return;
    }
    event.preventDefault();
    if (view === 'month') {
      const focused = untracked(this.focused);
      const months = untracked(this.calendar).monthsInYear(focused.year);
      const month = Math.min(Math.max(focused.month + delta, 1), months);
      // Through clampParts, like the year branch: roving onto a month
      // wholly outside the bounds would strand focus on a disabled grid.
      this.focused.set(this.clampParts(this.constrainDay({ ...focused, month }), Math.sign(delta)));
    } else {
      this.moveYears(delta);
    }
    this.pendingFocus.update((n) => n + 1);
  }

  // ---- Navigation arithmetic (bounds-clamped) ----

  private moveDays(days: number): void {
    const calendar = untracked(this.calendar);
    const focused = untracked(this.focused);
    const iso = calendar.fromParts(focused);
    if (iso === null) {
      return;
    }
    const parts = ɵtmParseIsoDate(iso)!;
    const date = new Date(Date.UTC(2000, parts.month - 1, parts.day, 12));
    date.setUTCFullYear(parts.year);
    date.setUTCDate(date.getUTCDate() + days);
    // Clamp on the NUMERIC year, before serializing: a 5-digit year would
    // slip through `clampIso`'s lexicographic compare ('10000-01-01' is
    // neither < the floor nor > the ceiling) and then throw in `toParts`.
    const year = date.getUTCFullYear();
    if (year > 9999 || year < 1) {
      const bound = year < 1 ? untracked(this.lowerBound) : untracked(this.upperBound);
      this.focused.set(calendar.toParts(bound));
      return;
    }
    const nextIso = `${String(year).padStart(4, '0')}-${String(
      date.getUTCMonth() + 1,
    ).padStart(2, '0')}-${String(date.getUTCDate()).padStart(2, '0')}`;
    this.focused.set(calendar.toParts(this.clampIso(nextIso)));
  }

  private moveToWeekEdge(edge: 'start' | 'end'): void {
    const calendar = untracked(this.calendar);
    const focused = untracked(this.focused);
    const iso = calendar.fromParts(focused);
    if (iso === null) {
      return;
    }
    const utcDay = this.utcOf(iso).getUTCDay();
    const isoWeekday = utcDay === 0 ? 7 : utcDay;
    const first = tmFirstDayOfWeek(untracked(this.locale));
    const offsetFromStart = (isoWeekday - first + 7) % 7;
    this.moveDays(edge === 'start' ? -offsetFromStart : 6 - offsetFromStart);
  }

  private moveMonths(months: number): void {
    const calendar = untracked(this.calendar);
    const focused = untracked(this.focused);
    let year = focused.year;
    let month = focused.month + months;
    while (month < 1) {
      year -= 1;
      if (year < 1) {
        year = 1;
        month = 1;
        break;
      }
      month += calendar.monthsInYear(year);
    }
    while (month > calendar.monthsInYear(year)) {
      month -= calendar.monthsInYear(year);
      year += 1;
    }
    this.focused.set(
      this.clampParts(this.constrainDay({ year, month, day: focused.day }), Math.sign(months)),
    );
  }

  private moveYears(years: number): void {
    const focused = untracked(this.focused);
    const year = Math.max(1, focused.year + years);
    this.focused.set(this.clampParts(this.constrainDay({ ...focused, year }), Math.sign(years)));
  }

  /** Clamps a day into the (possibly shorter) target month. */
  private constrainDay(parts: TmCalendarParts): TmCalendarParts {
    const calendar = untracked(this.calendar);
    const months = calendar.monthsInYear(parts.year);
    const month = Math.min(Math.max(parts.month, 1), months);
    const days = calendar.daysInMonth(parts.year, month);
    return { year: parts.year, month, day: Math.min(Math.max(parts.day, 1), days) };
  }

  /** Clamps an ISO date into the effective bounds. */
  private clampIso(iso: string): string {
    const lower = untracked(this.lowerBound);
    const upper = untracked(this.upperBound);
    return iso < lower ? lower : iso > upper ? upper : iso;
  }

  /**
   * Clamps parts into what the CALENDAR can represent — deliberately not
   * into `min`/`max`. Navigation ranges freely and out-of-range days
   * simply render disabled: an arrow button that silently refuses to
   * move is a worse answer than a month the user can see is unavailable.
   *
   * `direction` is the sign of the requested move: non-representable
   * parts are pinned by the DIRECTION OF TRAVEL, never by comparing
   * years (equal-year candidates go null too — an Ethiopic pre-epoch
   * value pages backward within year −3 — and a year comparison would
   * then teleport a backward gesture to the maximum bound).
   */
  private clampParts(parts: TmCalendarParts, direction: number): TmCalendarParts {
    const calendar = untracked(this.calendar);
    const iso = calendar.fromParts(parts);
    if (iso === null) {
      // Beyond what the calendar can represent (past the ISO ceiling, or
      // before its era's epoch): accepting the raw parts would strand the
      // roving focus on an unreachable, fully-disabled grid, taking the
      // dialog's keyboard (Escape included) down with it. Stand still if
      // the current focus is representable; otherwise fall back to a
      // bound, and last of all to today.
      const focused = untracked(this.focused);
      const candidates = [
        focused,
        calendar.toParts(direction < 0 ? MIN_ISO : MAX_ISO),
        calendar.toParts(this.clampIso(calendar.today())),
      ];
      return candidates.find((candidate) => calendar.fromParts(candidate) !== null) ?? focused;
    }
    const clamped = iso < MIN_ISO ? MIN_ISO : iso > MAX_ISO ? MAX_ISO : iso;
    return clamped === iso ? parts : calendar.toParts(clamped);
  }
}
