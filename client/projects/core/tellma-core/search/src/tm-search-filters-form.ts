// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  Injector,
  input,
  linkedSignal,
  output,
  signal,
  untracked,
  viewChild,
  type InputSignal,
  type OutputEmitterRef,
  type Signal,
} from '@angular/core';
import ChevronLeft from 'lucide/dist/esm/icons/chevron-left.mjs';
import Plus from 'lucide/dist/esm/icons/plus.mjs';
import { TmLocalization, type TmColumnPlan, type TmSearchQuery } from '@tellma/core';
import { TmColumnFilter } from '@tellma/core/column-filter';
import { tmDescribeFilter, TmFilterDescriptionText, tmParseFilter, type TmFilterDescription } from '@tellma/core/entity';
import { injectTmQueryexDraft, TmExpressionEditor } from '@tellma/core/expression-editor';
import { TmButton } from '@tellma/core-ui/button';
import { TmChip, TmChipSet } from '@tellma/core-ui/chip';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmMenu, TmMenuTrigger, type TmMenuItem } from '@tellma/core-ui/menu';
import { TmRadio, TmRadioGroup } from '@tellma/core-ui/radio';
import { TmSwitch } from '@tellma/core-ui/switch';

import type { ɵTmSearchFiltersHost } from './tm-search-filters-host';

// The Filters popup's body (spec 0030 §10): the tabs below `sm`, the filter
// expression, Active only, the custom filters, the applied column filters
// as chips (and an order on a hidden column), Add column filter, then Clear
// all and Apply. Every edit is staged here: the body is created when the
// popup opens and destroyed when it closes, so a dismissal discards the
// staged edits, and Apply turns them into one query change. A chip, or a
// column picked from Add column filter, opens that column's filter in
// place, with Back; its Apply and Clear stage the column's filter.

/** Matches the elements focus may move to inside the body. */
const FOCUSABLE = 'button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/** A staged column filter's chip: the column's key and its filter's description. */
interface TmSearchFilterChip {
  /** The column's key. */
  readonly key: string;
  /** The description, which follows the language. */
  readonly description: Signal<TmFilterDescription>;
}

/**
 * The body of the Filters popup: its sections, its staged edits, Clear all
 * and Apply. Internal to the search page.
 */
@Component({
  selector: 'tm-search-filters-form',
  imports: [
    TmButton,
    TmChip,
    TmChipSet,
    TmColumnFilter,
    TmExpressionEditor,
    TmFilterDescriptionText,
    TmIcon,
    TmMenu,
    TmMenuTrigger,
    TmRadio,
    TmRadioGroup,
    TmSwitch,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let shown = host();
    @if (editingColumn(); as column) {
      <!-- One column's filter in place, with Back to the sections; its Apply and Clear stage it. -->
      <div class="tm-search-filters__column">
        <button tmButton type="button" variant="ghost" size="sm" data-tm-filters-back (click)="back()">
          <tm-icon [svg]="glyphs.back" />
          {{ text.back() }}
        </button>
        <tm-column-filter [column]="column" [value]="staged(column.key)" (applied)="stageColumn(column.key, $event)" />
      </div>
    } @else {
      <h2 class="tm-search-filters__title">{{ text.filters() }}</h2>
      <div class="tm-search-filters__sections">
        <!-- 1. The tabs, below sm: a radio group of the declared tabs. -->
        @if (shown.tabs().length > 0) {
          <tm-radio-group data-tm-filters-tabs [ariaLabel]="text.tabs()" [value]="tab()" (valueChange)="tab.set($event ?? '')">
            @for (choice of shown.tabs(); track choice.key) {
              <tm-radio [value]="choice.key" [label]="choice.label" />
            }
          </tm-radio-group>
        }

        <!-- 2. The filter expression; Mod+Enter applies. -->
        @if (shown.expression()) {
          <div class="tm-search-filters__section">
            <span class="tm-search-filters__label" aria-hidden="true">{{ text.filterExpression() }}</span>
            <tm-expression-editor
              clause="filter"
              [draft]="draft"
              [root]="shown.root()"
              [rows]="3"
              [ariaLabel]="text.filterExpression()"
              [(value)]="filter"
              (submit)="apply()"
            />
          </div>
        }

        <!-- 3. Active only, on activatable entities. -->
        @if (shown.activeOnly()) {
          <tm-switch data-tm-filters-active [(checked)]="activeOnly">{{ text.activeOnly() }}</tm-switch>
        }

        <!-- 4. The custom filters, in declaration order: a switch, or a radio group with Any first. -->
        @for (custom of shown.custom(); track custom.key) {
          @if (custom.options; as options) {
            <div class="tm-search-filters__section">
              <span class="tm-search-filters__label" aria-hidden="true">{{ custom.label }}</span>
              <tm-radio-group
                [attr.data-tm-filters-custom]="custom.key"
                orientation="horizontal"
                [ariaLabel]="custom.label"
                [value]="customValue(custom.key)"
                (valueChange)="stageCustom(custom.key, $event || null)"
              >
                <tm-radio value="" [label]="text.any()" />
                @for (option of options; track option.key) {
                  <tm-radio [value]="option.key" [label]="option.label" />
                }
              </tm-radio-group>
            </div>
          } @else {
            <tm-switch
              [attr.data-tm-filters-custom]="custom.key"
              [checked]="customFilters()[custom.key] === '1'"
              (checkedChange)="stageCustom(custom.key, $event ? '1' : null)"
              >{{ custom.label }}</tm-switch
            >
          }
        }

        <!-- 5. The applied column filters and a hidden order, as chips; Add column filter opens a new one in place. -->
        @if (chips().length > 0 || order() !== null) {
          <tm-chip-set data-tm-filters-applied [ariaLabel]="text.appliedFilters()">
            @for (chip of chips(); track chip.key) {
              <tm-chip [label]="chip.description().text" removable (activate)="edit(chip.key)" (remove)="stageColumn(chip.key, null)">
                <tm-filter-description [description]="chip.description()" />
              </tm-chip>
            }
            @if (order(); as ordered) {
              <tm-chip [label]="ordered.text" removable (remove)="removeOrder()">
                <tm-filter-description [description]="ordered" />
              </tm-chip>
            }
          </tm-chip-set>
        }
        @if (addItems().length > 0) {
          <button tmButton type="button" variant="ghost" size="sm" class="tm-search-filters__add" data-tm-filters-add [tmMenuTriggerFor]="add">
            <tm-icon [svg]="glyphs.add" />
            {{ text.addColumnFilter() }}
          </button>
          <tm-menu #add [items]="addItems()" [aria-label]="text.addColumnFilter()" />
        }
      </div>

      <!-- The footer: Clear all stages the defaults; Apply applies, unless the last completed check found problems. -->
      <!-- In a drawer, the footer takes the modal footer's class, which pins it under the scrolling body. -->
      <div class="tm-search-filters__footer" [class.tm-modal__footer]="drawer()">
        <button tmButton type="button" variant="ghost" data-tm-filters-clear (click)="clearAll()">{{ text.clearAll() }}</button>
        <button tmButton type="button" variant="primary" data-tm-filters-apply [disabled]="applyDisabled()" (click)="apply()">{{ text.apply() }}</button>
      </div>
    }
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }

    .tm-search-filters__title {
      margin: 0;
      font: var(--font-heading-sm, inherit);
      font-weight: 600;
    }

    .tm-search-filters__sections,
    .tm-search-filters__section,
    .tm-search-filters__column {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
    }

    .tm-search-filters__sections {
      gap: var(--space-4);
    }

    .tm-search-filters__column > button {
      align-self: flex-start;
    }

    .tm-search-filters__label {
      font-weight: 600;
    }

    .tm-search-filters__add {
      align-self: flex-start;
    }

    .tm-search-filters__footer {
      display: flex;
      gap: var(--space-2);
      justify-content: flex-end;
    }
  `,
})
export class ɵTmSearchFiltersForm {
  readonly #injector = inject(Injector);
  readonly #l10n = inject(TmLocalization);
  readonly #element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  /** What the popup reads of the page and how it applies. */
  readonly host: InputSignal<ɵTmSearchFiltersHost> = input.required<ɵTmSearchFiltersHost>();

  /** Whether the filter expression's editor takes focus once the body renders, as Edit filter asks. */
  readonly focusEditor: InputSignal<boolean> = input(false);

  /** Whether the body is a drawer's, whose footer the drawer pins. */
  readonly drawer: InputSignal<boolean> = input(false);

  /** Emits once Apply has applied the staged edits, so the popup closes. */
  readonly done: OutputEmitterRef<void> = output<void>();

  /** The filter expression's editor, while the sections show. */
  private readonly editor = viewChild(TmExpressionEditor);

  // ---------------------------------------------------------------------------
  // The staged edits, each from the query the popup opened over
  // ---------------------------------------------------------------------------

  /** The query the popup opened over, read once. */
  readonly #opened = computed<TmSearchQuery>(() => untracked(this.host().query));

  /** The staged tab's key. */
  protected readonly tab = linkedSignal(() => this.#opened().tab ?? untracked(() => this.host().tabs()[0]?.key) ?? '');

  /** The staged filter expression. */
  protected readonly filter = linkedSignal(() => this.#opened().filter ?? '');

  /** Whether Active only is staged on. */
  protected readonly activeOnly = linkedSignal(() => this.#opened().inactive !== true);

  /** The staged custom filters' values, by key. */
  protected readonly customFilters = linkedSignal<Readonly<Record<string, string>>>(() => this.#opened().custom ?? {});

  /** The staged column filters, by column key. */
  protected readonly columnFilters = linkedSignal<Readonly<Record<string, string>>>(() => this.#opened().columnFilters ?? {});

  /** Whether the order on a hidden column is staged off. */
  protected readonly orderRemoved = signal(false);

  /** The key of the column whose filter is open in place; `null` while the sections show. */
  protected readonly editing = signal<string | null>(null);

  /** The query's draft, which checks the filter expression as it is typed. */
  protected readonly draft = injectTmQueryexDraft({ root: computed(() => (this.host().expression() ? this.host().root() : '')) });

  // ---------------------------------------------------------------------------
  // What the sections show
  // ---------------------------------------------------------------------------

  /** The column whose filter is open in place. */
  protected readonly editingColumn = computed(() => {
    const key = this.editing();
    return key === null ? null : this.host().column(key);
  });

  /** The descriptions made so far, by key and filter, so a chip's description is made once. */
  readonly #descriptions = new Map<string, Signal<TmFilterDescription>>();

  /** The staged column filters as chips, in key order; a key that no longer resolves shows none. */
  protected readonly chips = computed<readonly TmSearchFilterChip[]>(() => {
    const host = this.host();
    return Object.entries(this.columnFilters()).flatMap(([key, value]) => {
      const id = `${key}\n${value}`;
      let description = this.#descriptions.get(id);
      if (description === undefined) {
        const column = host.column(key);
        const spec = tmParseFilter(value);
        if (column === null || spec === null) {
          return [];
        }
        // Made outside the reactive context: the description registers its strings.
        description = untracked(() => tmDescribeFilter(column, spec, { injector: this.#injector }));
        this.#descriptions.set(id, description);
      }
      return [{ key, description }];
    });
  });

  /** The chip of an order on a hidden column, built as a filter's description is, its label the argument; `null` for none. */
  protected readonly order = computed<TmFilterDescription | null>(() => {
    const hidden = this.host().hiddenOrder();
    if (hidden === null || this.orderRemoved()) {
      return null;
    }
    // The label is a placeholder the message's text splits on, so translators order the words.
    const holder = '';
    const [before = '', after = ''] = this.#l10n.ɵtext('tm.searchFilters.orderedBy', { label: holder, direction: hidden.direction })().split(holder);
    const parts = [
      { text: before, argument: false },
      { text: hidden.label, argument: true },
      { text: after, argument: false },
    ].filter((part) => part.text !== '');
    return { text: parts.map((part) => part.text).join(''), parts };
  });

  /** Add column filter's items: the shown columns that take a filter, each opening its filter in place. */
  protected readonly addItems = computed<readonly TmMenuItem[]>(() =>
    this.host()
      .filterable()
      .map((column: TmColumnPlan) => ({ id: column.key, label: column.label(), action: () => this.edit(column.key) })),
  );

  /** Whether Apply is disabled: only while the last completed check, run on the current text, found problems. */
  protected readonly applyDisabled = computed(
    () => this.host().expression() && this.draft.status() === 'checked' && this.draft.diagnostics('filter')().length > 0,
  );

  /** The glyphs of Back and Add column filter; Back points against the reading direction. */
  protected readonly glyphs = { back: { node: ChevronLeft, mirrorInRtl: true }, add: Plus };

  /** The popup's labels. */
  protected readonly text = {
    filters: this.#l10n.ɵtext('tm.search.filters'),
    tabs: this.#l10n.ɵtext('tm.search.tabs'),
    filterExpression: this.#l10n.ɵtext('tm.searchFilters.filterExpression'),
    activeOnly: this.#l10n.ɵtext('tm.searchFilters.activeOnly'),
    any: this.#l10n.ɵtext('tm.searchFilters.any'),
    appliedFilters: this.#l10n.ɵtext('tm.searchFilters.appliedFilters'),
    addColumnFilter: this.#l10n.ɵtext('tm.searchFilters.addColumnFilter'),
    back: this.#l10n.ɵtext('tm.searchFilters.back'),
    clearAll: this.#l10n.ɵtext('tm.searchFilters.clearAll'),
    apply: this.#l10n.ɵtext('tm.searchFilters.apply'),
  };

  /** Focuses the filter expression's editor once the body renders, when asked to. */
  constructor() {
    afterNextRender(() => {
      if (this.focusEditor()) {
        this.editor()?.focus();
      }
    });
  }

  // ---------------------------------------------------------------------------
  // The staging
  // ---------------------------------------------------------------------------

  /**
   * Opens a column's filter in place.
   *
   * @param key - The column's key.
   */
  protected edit(key: string): void {
    this.#switchView(() => this.editing.set(key));
  }

  /** Goes back from a column's filter to the sections. */
  protected back(): void {
    this.#switchView(() => this.editing.set(null));
  }

  /**
   * Stages a column's filter, or its removal, and goes back to the sections.
   *
   * @param key - The column's key.
   * @param value - The filter, or `null` to remove it.
   */
  protected stageColumn(key: string, value: string | null): void {
    const next = { ...this.columnFilters() };
    if (value === null) {
      delete next[key];
    } else {
      next[key] = value;
    }
    this.columnFilters.set(next);
    if (this.editing() !== null) {
      this.back();
    }
  }

  /**
   * Stages a custom filter's value, or its removal.
   *
   * @param key - The filter's key.
   * @param value - The value: `1` for a switch that is on, an option's value; `null` for none.
   */
  protected stageCustom(key: string, value: string | null): void {
    const next = { ...this.customFilters() };
    if (value === null) {
      delete next[key];
    } else {
      next[key] = value;
    }
    this.customFilters.set(next);
  }

  /** Stages the removal of the order on a hidden column. */
  protected removeOrder(): void {
    this.orderRemoved.set(true);
  }

  /** Clear all: an empty filter expression, no column or custom filter, Active only on and, below `sm`, the first tab. */
  protected clearAll(): void {
    this.filter.set('');
    this.columnFilters.set({});
    this.customFilters.set({});
    this.activeOnly.set(true);
    this.tab.set(this.host().tabs()[0]?.key ?? '');
  }

  /**
   * A column's staged filter.
   *
   * @param key - The column's key.
   * @returns The filter, or `null` for none.
   */
  protected staged(key: string): string | null {
    return this.columnFilters()[key] ?? null;
  }

  /**
   * A custom filter's staged option, for its radio group.
   *
   * @param key - The filter's key.
   * @returns The option's value; empty for Any.
   */
  protected customValue(key: string): string {
    return this.customFilters()[key] ?? '';
  }

  /** Applies the staged edits as one query change, unless Apply is disabled. */
  protected apply(): void {
    if (this.applyDisabled()) {
      return;
    }
    const host = this.host();
    const record = (value: Readonly<Record<string, string>>): Readonly<Record<string, string>> | undefined =>
      Object.keys(value).length > 0 ? value : undefined;
    const filter = this.filter();
    const tabs = host.tabs();
    const patch: { -readonly [K in keyof TmSearchQuery]?: TmSearchQuery[K] } = {
      columnFilters: record(this.columnFilters()),
      custom: record(this.customFilters()),
    };
    if (host.expression()) {
      patch.filter = filter.trim() === '' ? undefined : filter;
    }
    if (host.activeOnly()) {
      patch.inactive = this.activeOnly() ? undefined : true;
    }
    if (tabs.length > 0) {
      patch.tab = this.tab() === tabs[0]!.key ? undefined : this.tab();
    }
    if (this.orderRemoved()) {
      patch.order = undefined;
    }
    host.apply(patch);
    this.done.emit();
  }

  /**
   * Switches between the sections and a column's filter, keeping focus in
   * the popup: focus rests on the body's dialog while the control that had
   * it leaves, then moves to the first control of the new view.
   *
   * @param change - The change of view.
   */
  #switchView(change: () => void): void {
    this.#element.closest<HTMLElement>('[role="dialog"]')?.focus();
    change();
    afterNextRender(() => this.#element.querySelector<HTMLElement>(FOCUSABLE)?.focus(), { injector: this.#injector });
  }
}
