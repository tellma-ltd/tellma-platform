// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  linkedSignal,
  output,
  type InputSignal,
  type OutputEmitterRef,
  type Signal,
} from '@angular/core';
import { TmDecimals, TmEntities, TmLocalization, type TmDecimal, type TmId } from '@tellma/core';
import { TmButton } from '@tellma/core-ui/button';
import { TmCheckbox } from '@tellma/core-ui/checkbox';
import { TmDatePicker } from '@tellma/core-ui/date-picker';
import { TmDateRange, type TmDateRangeValue } from '@tellma/core-ui/date-range';
import { TmEntityMultiPicker, type TmEntitySearchFn } from '@tellma/core-ui/entity-picker';
import { TmInput } from '@tellma/core-ui/input';
import { TmNumber } from '@tellma/core-ui/number';
import { TmRadio, TmRadioGroup } from '@tellma/core-ui/radio';
import { TmMultiSelect, TmOption, TmSelect } from '@tellma/core-ui/select';

import { TmEntityMultiField } from './tm-entity-multi-field';
import { ɵTM_FILTER_STRINGS_EN } from './tm-entity-strings';
import { tmFilterLeaf, tmFormatFilter, tmParseFilter, TM_ENUM_PICKER_THRESHOLD, ɵTM_FILTER_OPS, type TmFilterOp, type TmFilterSpec } from './tm-filter';
import type { TmColumnPlan } from './tm-select-plan';

// The column filter control (spec 0029 §13.3): the content of a column
// header's popover. It edits one column's `f.` value as an operator and
// the values that operator takes, and emits the value on Apply (null on
// Clear). Apply is enabled only while the value makes a leaf, so the host
// never receives a value it would drop. Numbers are typed in any digits the
// locale reads and written in Latin digits.

/** The most enum values that show as a list of checkboxes. */
const CHECKBOX_LIMIT = 8;

/** The most values a large enum's search lists at once. */
const SEARCH_PAGE = 20;

/** The most values an enum or navigation filter takes. */
const MAX_ITEMS = 50;

/** An enum value as the control lists it. */
interface TmEnumChoice {
  /** The value as the wire spells it. */
  readonly value: string;
  /** Its label in the active language. */
  readonly label: string;
}

/**
 * Edits one column's filter: an operator select (when the column's kind
 * has more than one), the values the operator takes, Clear and Apply.
 * Enter applies a valid value; Escape is the host popover's.
 *
 * - Text: an input. Numbers: one number input, or a From and To pair for
 *   `between`. Dates and instants: a date picker, or a date range.
 * - Enums: a checkbox list up to 8 values, a multi-select below 50, and a
 *   searchable multi-picker of the values from 50 up. Navigations: an
 *   entity multi-field. Both take at most 50 values.
 * - Booleans: True and False radios.
 *
 * @example
 * ```html
 * <tm-column-filter [column]="column" [value]="query.columnFilters?.[column.key] ?? null" (applied)="filter(column, $event)" />
 * ```
 */
@Component({
  selector: 'tm-column-filter',
  imports: [
    TmButton,
    TmCheckbox,
    TmDatePicker,
    TmDateRange,
    TmEntityMultiField,
    TmEntityMultiPicker,
    TmInput,
    TmMultiSelect,
    TmNumber,
    TmOption,
    TmRadio,
    TmRadioGroup,
    TmSelect,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'tm-column-filter' },
  styles: `
    :host {
      display: block;
      inline-size: 320px;
      max-inline-size: 100%;
    }
    .tm-column-filter__form {
      display: flex;
      flex-direction: column;
      gap: var(--tm-space-3, 0.75rem);
    }
    .tm-column-filter__pair {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: var(--tm-space-2, 0.5rem);
    }
    .tm-column-filter__end {
      display: flex;
      flex-direction: column;
      gap: var(--tm-space-1, 0.25rem);
      min-inline-size: 0;
    }
    .tm-column-filter__list {
      display: flex;
      flex-direction: column;
      gap: var(--tm-space-2, 0.5rem);
    }
    .tm-column-filter__actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--tm-space-2, 0.5rem);
    }
  `,
  template: `
    @let label = column().label();
    <form class="tm-column-filter__form" (submit)="submit($event)" (keydown.enter)="enter($event)">
      <!-- The operator, when the kind has a choice of them -->
      @if (ops().length > 1) {
        <tm-select class="tm-column-filter__operator" [(value)]="op" [aria-label]="text.operator()">
          @for (option of ops(); track option) {
            <tm-option [value]="option" [label]="opLabels[option]()">{{ opLabels[option]() }}</tm-option>
          }
        </tm-select>
      }

      <!-- The values the operator takes, by the column's kind -->
      @switch (control()) {
        @case ('text') {
          <input tmInput class="tm-column-filter__text" [(value)]="textValue" [attr.aria-label]="label" />
        }
        @case ('number') {
          @if (op() === 'between') {
            <div class="tm-column-filter__pair" role="group" [attr.aria-label]="label">
              <label class="tm-column-filter__end">
                {{ text.from() }}
                <input tmNumber [(value)]="numberFrom" [maxDecimals]="decimals()" />
              </label>
              <label class="tm-column-filter__end">
                {{ text.to() }}
                <input tmNumber [(value)]="numberTo" [maxDecimals]="decimals()" />
              </label>
            </div>
          } @else {
            <input tmNumber [(value)]="numberValue" [maxDecimals]="decimals()" [attr.aria-label]="label" />
          }
        }
        @case ('date') {
          @if (op() === 'between') {
            <tm-date-range [(value)]="range" layout="inline" [label]="label" [fromLabel]="text.from()" [toLabel]="text.to()" />
          } @else {
            <tm-date-picker [(value)]="day" [aria-label]="label" />
          }
        }
        @case ('enum') {
          @let choices = enumChoices();
          @if (choices.length <= checkboxLimit) {
            <div class="tm-column-filter__list" role="group" [attr.aria-label]="label">
              @for (choice of choices; track choice.value) {
                <tm-checkbox [checked]="chosen().includes(choice.value)" (checkedChange)="toggle(choice.value, $event)">{{ choice.label }}</tm-checkbox>
              }
            </div>
          } @else if (choices.length < pickerThreshold) {
            <tm-multi-select [(value)]="chosen" [aria-label]="label">
              @for (choice of choices; track choice.value) {
                <tm-option [value]="choice.value" [label]="choice.label">{{ choice.label }}</tm-option>
              }
            </tm-multi-select>
          } @else {
            <tm-entity-multi-picker
              [(value)]="chosen"
              [search]="enumSearch()"
              [itemId]="choiceValue"
              [itemLabel]="choiceLabel"
              [displayWith]="enumLabel()"
              [maxItems]="maxItems"
              [aria-label]="label"
            />
          }
        }
        @case ('bool') {
          <tm-radio-group [(value)]="flag" [ariaLabel]="label">
            <tm-radio value="true" [label]="text.true()" />
            <tm-radio value="false" [label]="text.false()" />
          </tm-radio-group>
        }
        @case ('navigation') {
          <tm-entity-multi-field [entity]="target()" [create]="false" [(value)]="ids" [maxItems]="maxItems" [aria-label]="label" />
        }
      }

      <!-- Clear and Apply; Apply is the form's submit -->
      <div class="tm-column-filter__actions">
        <button tmButton type="button" variant="ghost" (click)="applied.emit(null)">{{ text.clear() }}</button>
        <button tmButton type="submit" variant="primary" [disabled]="filterValue() === null">{{ text.apply() }}</button>
      </div>
    </form>
  `,
})
export class TmColumnFilter {
  readonly #localization = inject(TmLocalization);
  readonly #entities = inject(TmEntities);

  /** The column filtered. */
  readonly column: InputSignal<TmColumnPlan> = input.required<TmColumnPlan>();

  /** The column's current `f.` value, such as `between:1,5`; null for none. */
  readonly value: InputSignal<string | null> = input<string | null>(null);

  /** The value applied: a valid `f.` value on Apply, null on Clear. */
  readonly applied: OutputEmitterRef<string | null> = output<string | null>();

  /** The most enum values that show as checkboxes. */
  protected readonly checkboxLimit = CHECKBOX_LIMIT;

  /** The enum size from which the values are searched. */
  protected readonly pickerThreshold = TM_ENUM_PICKER_THRESHOLD;

  /** The most values a list filter takes. */
  protected readonly maxItems = MAX_ITEMS;

  /** The control's strings. */
  protected readonly text = (() => {
    this.#localization.ɵregisterStrings('filter', ɵTM_FILTER_STRINGS_EN);
    const text = (key: string): Signal<string> => this.#localization.ɵtext(`tm.filter.${key}`);
    return {
      operator: text('operator'),
      apply: text('apply'),
      clear: text('clear'),
      from: text('from'),
      to: text('to'),
      true: text('true'),
      false: text('false'),
    };
  })();

  /** Each operator's name. */
  protected readonly opLabels = Object.fromEntries(
    Object.keys(ɵTM_FILTER_STRINGS_EN.op).map((op) => [op, this.#localization.ɵtext(`tm.filter.op.${op}`)]),
  ) as Readonly<Record<TmFilterOp, Signal<string>>>;

  /** The control the column's kind takes: an instant's days are chosen as a date's are. */
  protected readonly control: Signal<string | null> = computed(() => {
    const kind = this.column().filter;
    return kind === 'instant' ? 'date' : kind;
  });

  /** The operators the column's kind takes, the default first. */
  protected readonly ops: Signal<readonly TmFilterOp[]> = computed(() => {
    const kind = this.column().filter;
    return kind === null ? [] : ɵTM_FILTER_OPS[kind];
  });

  /** The current value, when it is one the column takes. */
  readonly #current: Signal<TmFilterSpec | null> = computed(() => {
    const value = this.value();
    const spec = value === null ? null : tmParseFilter(value);
    return spec !== null && this.ops().includes(spec.op) ? spec : null;
  });

  /** The chosen operator. */
  protected readonly op = linkedSignal<TmFilterOp | undefined>(() => this.#current()?.op ?? this.ops()[0]);

  /** A text filter's value. */
  protected readonly textValue = linkedSignal(() => (this.column().filter === 'text' ? (this.#current()?.values[0] ?? '') : ''));

  /** A number filter's single value. */
  protected readonly numberValue = linkedSignal(() => this.#number(this.#current()?.op === 'between' ? undefined : this.#current()?.values[0]));

  /** A number range's lower end. */
  protected readonly numberFrom = linkedSignal(() => this.#number(this.#current()?.op === 'between' ? this.#current()?.values[0] : undefined));

  /** A number range's upper end. */
  protected readonly numberTo = linkedSignal(() => this.#number(this.#current()?.op === 'between' ? this.#current()?.values[1] : undefined));

  /** A day filter's single day. */
  protected readonly day = linkedSignal<string | null>(() => (this.#current()?.op === 'between' ? null : (this.#current()?.values[0] ?? null)));

  /** A day filter's range. */
  protected readonly range = linkedSignal<TmDateRangeValue>(() => {
    const current = this.#current();
    return current?.op === 'between' ? { from: current.values[0] ?? null, to: current.values[1] ?? null } : { from: null, to: null };
  });

  /** The chosen enum values. */
  protected readonly chosen = linkedSignal<readonly string[]>(() => (this.column().filter === 'enum' ? (this.#current()?.values ?? []) : []));

  /** A boolean filter's choice. */
  protected readonly flag = linkedSignal<string | null>(() => (this.column().filter === 'bool' ? (this.#current()?.values[0] ?? null) : null));

  /** The chosen related rows. */
  protected readonly ids = linkedSignal<readonly TmId[]>(() => {
    const long = this.column().descriptor?.long === true;
    return this.column().filter === 'navigation' ? (this.#current()?.values ?? []).map((id) => (long ? (id as TmDecimal) : Number(id))) : [];
  });

  /** A navigation's target entity. */
  protected readonly target: Signal<string> = computed(() => this.column().descriptor?.target ?? '');

  /** The fraction digits a number takes: none for an integer, the scale for a decimal. */
  protected readonly decimals: Signal<number | undefined> = computed(() => {
    const of = this.column().descriptor;
    return of?.type === 'integer' ? 0 : of?.scale;
  });

  /** An enum's values with their labels, in the enum's order. */
  protected readonly enumChoices: Signal<readonly TmEnumChoice[]> = computed(() => {
    const name = this.column().descriptor?.enum;
    const descriptor = name === undefined ? undefined : this.#entities.enum(name);
    return (descriptor?.values ?? []).map(({ value }) => ({ value, label: descriptor!.label(value)() }));
  });

  /**
   * A large enum's search: synchronous, the values whose label or value
   * contains the text ignoring case, in the enum's order, at most 20 at a
   * time; the first 20 for an empty text.
   */
  protected readonly enumSearch: Signal<TmEntitySearchFn<TmEnumChoice>> = computed(() => {
    const choices = this.enumChoices();
    return (query: string) => {
      const needle = query.trim().toLocaleLowerCase();
      const matches = needle === '' ? choices : choices.filter((choice) => choice.label.toLocaleLowerCase().includes(needle) || choice.value.toLocaleLowerCase().includes(needle));
      return { items: matches.slice(0, SEARCH_PAGE), hasMore: matches.length > SEARCH_PAGE };
    };
  });

  /** A chosen enum value's label, for its chip. */
  protected readonly enumLabel: Signal<(value: string) => string | null> = computed(() => {
    const labels = new Map(this.enumChoices().map((choice) => [choice.value, choice.label]));
    return (value) => labels.get(value) ?? null;
  });

  /** An enum choice's id: its value. */
  protected readonly choiceValue = (choice: TmEnumChoice): string => choice.value;

  /** An enum choice's label. */
  protected readonly choiceLabel = (choice: TmEnumChoice): string => choice.label;

  /** The filter the controls hold, culture-free; null while they hold none. */
  readonly #spec: Signal<TmFilterSpec | null> = computed(() => {
    const op = this.op();
    if (op === undefined) {
      return null;
    }
    const values = ((): readonly (string | null | undefined)[] => {
      switch (this.column().filter) {
        case 'text':
          return [this.textValue()];
        case 'number':
          return op === 'between' ? [this.#decimal(this.numberFrom()), this.#decimal(this.numberTo())] : [this.#decimal(this.numberValue())];
        case 'date':
        case 'instant':
          return op === 'between' ? [this.range().from, this.range().to] : [this.day()];
        case 'enum':
          return this.chosen();
        case 'bool':
          return [this.flag()];
        case 'navigation':
          return this.ids().map(String);
        default:
          return [];
      }
    })();
    return values.length > 0 && values.every((value) => typeof value === 'string' && value !== '') ? { op, values: values as string[] } : null;
  });

  /** The `f.` value Apply emits; null while the controls hold no value the column takes. */
  protected readonly filterValue: Signal<string | null> = computed(() => {
    const spec = this.#spec();
    const valid = spec !== null && tmFilterLeaf(this.column(), spec, { timeZone: this.#localization.timeZone() }) !== null;
    return valid ? tmFormatFilter(spec) : null;
  });

  /**
   * Applies the value on the form's submit: Apply, or Enter in an input.
   *
   * @param event - The submit event.
   */
  protected submit(event: Event): void {
    event.preventDefault();
    const value = this.filterValue();
    if (value !== null) {
      this.applied.emit(value);
    }
  }

  /**
   * Applies the value on Enter in one of the control's own inputs. A
   * picker's or a select's Enter chooses a row there, so it is theirs; a
   * date picker has committed its typed text by the time the key reaches
   * the form.
   *
   * @param event - The key event.
   */
  protected enter(event: Event): void {
    const target = event.target;
    if (
      event.defaultPrevented ||
      !(target instanceof HTMLInputElement) ||
      target.closest('tm-select, tm-multi-select, tm-entity-multi-picker, tm-entity-multi-field') !== null
    ) {
      return;
    }
    this.submit(event);
  }

  /**
   * Adds or removes an enum value from the checkbox list, keeping the
   * enum's order.
   *
   * @param value - The value.
   * @param checked - Whether it is now chosen.
   */
  protected toggle(value: string, checked: boolean): void {
    const chosen = new Set(this.chosen());
    if (checked) {
      chosen.add(value);
    } else {
      chosen.delete(value);
    }
    this.chosen.set(this.enumChoices().map((choice) => choice.value).filter((item) => chosen.has(item)));
  }

  /**
   * A number control's value from decimal text.
   *
   * @param value - The text, when there is one.
   * @returns The number, or null.
   */
  #number(value: string | undefined): number | null {
    return value !== undefined && TmDecimals.isDecimal(value) ? TmDecimals.toNumber(value) : null;
  }

  /**
   * Decimal text, in Latin digits, from a number control's value.
   *
   * @param value - The number, or null.
   * @returns The text, or null.
   */
  #decimal(value: number | null): string | null {
    return value === null || !Number.isFinite(value) ? null : TmDecimals.fromNumber(value);
  }
}
