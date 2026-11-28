// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  type InputSignal,
  type InputSignalWithTransform,
  numberAttribute,
  signal,
  type Signal,
  untracked,
} from '@angular/core';
import { ɵtmControlSize } from '@tellma/core-ui';
import {
  type TmChildDescriptor,
  type TmColumnInput,
  TmEntities,
  type TmEntityDescriptor,
  type TmJson,
  type TmJsonObject,
  TmLocalization,
  type TmPropertyDescriptor,
} from '@tellma/core';
import { TmFormats } from '@tellma/core/entity';

import { ɵtmChildGridTotal } from './tm-child-grid-totals';
import { TmChildGridView } from './tm-child-grid-view';
import { ɵtmDeferTriggers } from './tm-details-defer';
import { injectTmDetailsRecord, tmDevMode } from './tm-details-record';
import { ɵtmHoldCollectionErrors } from './tm-details-scope';

// A child collection as one grid (spec 0031 §6.4). This part loads with
// the details route: the frame of a fixed height, the same-geometry
// placeholder, the totals and the collection's error report to its card
// and tab. The grid itself renders in a deferred chunk once the frame
// enters the viewport, prefetched once the browser is idle (the semantics
// of `on viewport; prefetch on idle`, through signals). The height is
// set when the record lands, from the rows and the placeholder row, between
// 4 rows and `maxRows`, plus the header and the status band. View mode
// reserves the placeholder row and the band too, so the frame's box is the
// same in view mode, in Edit and after Cancel (spec 0031 §18's stable
// layout); typing never moves it, and beyond it the grid scrolls inside. A
// grid that never edits has neither, and no mode moves it either.

/** The fewest body rows the frame shows. */
const MIN_ROWS = 4;

/** One total under the grid: its label and its sum, as shown. */
interface TmChildGridTotalView {
  /** The column's key. */
  readonly key: string;
  /** "Total {label}", in the active language. */
  readonly label: string;
  /** The exact sum, as the column displays it. */
  readonly text: string;
}

/**
 * A child collection of the record as one grid, editable in edit and
 * create mode and read-only in view mode, with its rows' new-row
 * placeholder, a fixed height and the exact totals of its numeric columns.
 * View-mode navigation cells are links.
 *
 * @example
 * ```html
 * <tm-details-card titleKey="Acme_SalesInvoice_Lines">
 *   <tm-child-grid
 *     collection="Lines"
 *     [columns]="['Center', 'Description', 'Amount']"
 *     [totals]="['Amount']"
 *     [defaults]="newLine"
 *   />
 * </tm-details-card>
 * ```
 */
@Component({
  selector: 'tm-child-grid',
  imports: [TmChildGridView],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'tm-child-grid',
    '[class.tm-child-grid--sm]': "size() === 'sm'",
    '[class.tm-child-grid--lg]': "size() === 'lg'",
    '[class.tm-child-grid--editable]': 'editable()',
    '[class.tm-child-grid--readonly]': 'readonly()',
    '[style.--tm-child-grid-rows]': 'rows()',
    '[attr.data-tm-collection]': 'child()?.name ?? null',
  },
  styles: `
    :host {
      display: block;
      min-inline-size: 0;
      --tm-child-grid-status: var(--grid-status-height);
    }

    /* The grid's own row heights, by the size the grid draws at. */
    :host(.tm-child-grid--sm) {
      --grid-row-height: var(--grid-row-height-sm);
    }

    :host(.tm-child-grid--lg) {
      --grid-row-height: var(--grid-row-height-lg);
    }

    /* A grid that never edits has no status band to reserve. */
    :host(.tm-child-grid--readonly) {
      --tm-child-grid-status: 0px;
    }

    /* The frame: the header, the rows shown, the scroller's border and the status band. */
    .tm-child-grid__frame {
      box-sizing: border-box;
      block-size: calc(
        var(--grid-row-height) * (var(--tm-child-grid-rows) + 1) + 2 * var(--border-width) + var(--tm-child-grid-status)
      );
    }

    /* The placeholder: the grid's outline and header band, before its chunk renders. */
    .tm-child-grid__placeholder {
      box-sizing: border-box;
      block-size: 100%;
      border: var(--border-width) solid var(--border-default);
      border-radius: var(--radius-sm);
      background: var(--surface-card);
    }

    .tm-child-grid__band {
      box-sizing: border-box;
      block-size: var(--grid-row-height);
      border-block-end: var(--border-width) solid var(--grid-header-line);
      background: var(--grid-header-bg);
    }

    .tm-child-grid__failed {
      margin: 0;
      padding: var(--space-3);
      color: var(--error);
      font-size: var(--text-sm);
    }

    /* The totals, under the grid, at its end edge. */
    .tm-child-grid__totals {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: var(--space-2) var(--space-6);
      margin: 0;
      padding-block: var(--space-2) 0;
      font-size: var(--text-sm);
    }

    .tm-child-grid__total {
      display: flex;
      gap: var(--space-2);
      align-items: baseline;
    }

    .tm-child-grid__total dt {
      color: var(--text-secondary);
    }

    .tm-child-grid__total dd {
      margin: 0;
      font-weight: var(--weight-semibold);
      font-variant-numeric: tabular-nums;
      color: var(--text-strong);
    }
  `,
  template: `
    <!-- The frame keeps its height whether the grid, its placeholder or a failure shows -->
    <div class="tm-child-grid__frame">
      @if (child(); as collection) {
        @if (loading()) {
          <!-- The record loads with nothing held: the placeholder's geometry -->
          <div class="tm-child-grid__placeholder" aria-hidden="true"><div class="tm-child-grid__band"></div></div>
        } @else {
          @defer (when defer.visible()) {
            <tm-child-grid-view
              [child]="collection"
              [columns]="columns()"
              [defaults]="defaults()"
              [editable]="editable()"
              [readonly]="readonly()"
              [size]="size()"
            />
          } @placeholder {
            <div class="tm-child-grid__placeholder" aria-hidden="true"><div class="tm-child-grid__band"></div></div>
          } @error {
            <div class="tm-child-grid__placeholder">
              <p class="tm-child-grid__failed" role="alert">{{ failedText() }}</p>
            </div>
          }
        }
      }
    </div>

    <!-- The totals: each one's exact sum, live in edit mode -->
    @if (totalViews().length > 0) {
      <dl class="tm-child-grid__totals" [attr.aria-label]="totalsText()">
        @for (total of totalViews(); track total.key) {
          <div class="tm-child-grid__total">
            <dt>{{ total.label }}</dt>
            <dd>{{ total.text }}</dd>
          </div>
        }
      </dl>
    }
  `,
})
export class TmChildGrid {
  readonly #record = injectTmDetailsRecord();
  readonly #entities = inject(TmEntities);
  readonly #formats = inject(TmFormats);
  readonly #l10n = inject(TmLocalization);

  /** The child collection's name, such as `Lines`. */
  readonly collection: InputSignal<string> = input.required<string>();

  /** The columns, by keys relative to the child entity, such as `Center` or `Amount`. */
  readonly columns: InputSignal<readonly TmColumnInput<string>[]> = input.required<readonly TmColumnInput<string>[]>();

  /** The numeric columns summed under the grid, by key. */
  readonly totals: InputSignal<readonly string[]> = input<readonly string[]>([]);

  /** A new row's values, beside its id. */
  readonly defaults: InputSignal<(() => TmJsonObject) | undefined> = input<(() => TmJsonObject) | undefined>(undefined);

  /** Whether the grid is never editable, in every mode. */
  readonly readonly: InputSignalWithTransform<boolean, unknown> = input(false, { transform: booleanAttribute });

  /** The most body rows the frame shows before the grid scrolls inside. */
  readonly maxRows: InputSignalWithTransform<number, unknown> = input(12, { transform: numberAttribute });

  /** When the grid's chunk loads: once the frame enters the viewport, prefetched once the browser is idle. */
  protected readonly defer = ɵtmDeferTriggers(() => import('./tm-child-grid-view'));

  /** The size the grid draws at: the app's control size, `lg` on a narrow screen. */
  protected readonly size = ɵtmControlSize(signal(undefined));

  /** The collection's descriptor; a development error when the record's entity has none of the name. */
  protected readonly child: Signal<TmChildDescriptor | undefined> = computed(() => {
    const name = this.collection();
    const child = this.#record.descriptor.child(name);
    if (child === undefined && tmDevMode()) {
      throw new Error(`tm-child-grid's collection '${name}' names no child collection of ${this.#record.descriptor.name}.`);
    }
    return child;
  });

  /** The child entity's descriptor. */
  readonly #childEntity = computed<TmEntityDescriptor | undefined>(() => {
    const child = this.child();
    return child === undefined ? undefined : this.#entities.get(child.entity);
  });

  /** Whether the record loads with nothing held: the placeholder, and no grid. */
  protected readonly loading = computed(() => this.#record.status() === 'loading' && !this.#record.landed());

  /** Whether the grid edits: in edit and create mode, unless it is read-only. */
  protected readonly editable = computed(() => this.#record.mode() !== 'view' && !this.readonly());

  /** The body rows the frame shows, set when the record lands. */
  protected readonly rows = signal(MIN_ROWS);

  /** The failed load's message. */
  protected readonly failedText = computed(() => this.#l10n.ɵtext('tm.details.tableFailed')());

  /** The totals' name, for assistive technology. */
  protected readonly totalsText = computed(() => this.#l10n.ɵtext('tm.details.totals')());

  /** The collection's rows in the working copy. */
  readonly #modelRows = computed<readonly TmJson[]>(() => {
    const child = this.child();
    const rows = child === undefined ? undefined : (this.#record.model() as Readonly<Record<string, TmJson | undefined>>)[child.json];
    return Array.isArray(rows) ? rows : [];
  });

  /** The totals, as shown: each named column's exact sum over the working copy's rows. */
  protected readonly totalViews: Signal<readonly TmChildGridTotalView[]> = computed(() => {
    const entity = this.#childEntity();
    if (entity === undefined) {
      return [];
    }
    const rows = this.#modelRows();
    return this.totals().flatMap((key) => {
      const property = numeric(entity, key);
      if (property === undefined) {
        return [];
      }
      const total = ɵtmChildGridTotal(rows, property.json, property.scale ?? 0);
      return [{ key, label: this.#l10n.ɵtext('tm.details.total', { label: property.label() })(), text: this.#formats.ɵgridText(total, property) }];
    });
  });

  /** Sets the frame's height at each landing, and reports the collection's errors to its card and tab. */
  constructor() {
    // --- The height: the rows and the placeholder row, at least 4, at most maxRows; fixed across modes and while typing ---
    effect(() => {
      this.#record.landed();
      this.#record.entity();
      const max = Math.max(MIN_ROWS, this.maxRows());
      // View mode reserves the placeholder row Edit adds; a grid that never edits has none.
      const count = untracked(this.#modelRows).length + (this.readonly() ? 0 : 1);
      this.rows.set(Math.min(max, Math.max(MIN_ROWS, count)));
    });

    // --- The collection's rows show their errors once touched or dirty; the card and tab hold them from creation ---
    ɵtmHoldCollectionErrors(computed(() => this.child()?.json));
  }
}

/**
 * A totals column's property: a decimal or an integer on the child row itself.
 *
 * @param entity - The child entity.
 * @param key - The column's key.
 * @returns The property; `undefined` outside development for a key that names none.
 * @throws Error in development, when the key names no number of the child entity.
 */
function numeric(entity: TmEntityDescriptor, key: string): TmPropertyDescriptor | undefined {
  const property = entity.property(key);
  if (property !== undefined && (property.type === 'decimal' || property.type === 'integer')) {
    return property;
  }
  if (tmDevMode()) {
    throw new Error(`tm-child-grid's total '${key}' names no decimal or integer property of ${entity.name}.`);
  }
  return undefined;
}
