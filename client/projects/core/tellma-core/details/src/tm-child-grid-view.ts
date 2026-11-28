// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  Injector,
  input,
  type InputSignal,
  signal,
  type Signal,
  untracked,
  viewChild,
} from '@angular/core';
import type { FieldTree } from '@angular/forms/signals';
import type { TmControlSize } from '@tellma/core-ui';
import { TmButton } from '@tellma/core-ui/button';
import { TmGrid, TmGridColumn, TmGridDisplayDef } from '@tellma/core-ui/grid';
import {
  type TmChildDescriptor,
  type TmColumnInput,
  type TmColumnPlan,
  TmDisplayNames,
  TmEntities,
  type TmId,
  type TmJson,
  type TmJsonObject,
  TmLocalization,
  type TmVisibleData,
} from '@tellma/core';
import { type TmGridColumnConfig, ɵtmGridColumnFor, tmSelectPlan, TmValue } from '@tellma/core/entity';

import { injectTmDetailsRecord } from './tm-details-record';
import { TM_DETAILS_SCOPE } from './tm-details-scope';

// The grid of a child collection (spec 0031 §6.4), in its own chunk: the
// details route loads it once a grid nears the viewport. One tm-grid binds
// to the collection's form field in every mode and is read-only in view
// mode, so a mode switch changes its rules' colours only. Its columns come
// from a select plan over the child entity; the navigations a form's grid
// picks come with the editing entry point, which loads once the record may
// be edited, so Edit finds them ready. Scroll and column widths are kept
// per record under `details:<entity>:<collection>`. Once its rows render,
// the grid registers its collection with the record: what it displays,
// and the reveal a deep link or an error summary calls.

/** The columns the page context carries, at most. */
const VISIBLE_COLUMNS = 50;

/** The rows the page context carries, at most. */
const VISIBLE_ROWS = 200;

/** The editing entry point's editable columns, once loaded. */
type TmEditableColumnFor = typeof import('@tellma/core/entity-editing').tmEditableGridColumnFor;

/** A column as the grid binds it. */
interface TmChildGridColumn {
  /** The plan's column. */
  readonly plan: TmColumnPlan;
  /** The inputs it binds. */
  readonly config: Partial<TmGridColumnConfig>;
  /** The grid column's key: the row's member the cell holds; `undefined` for a path through a navigation. */
  readonly key: string | undefined;
  /** Whether a typed value renders the cell: a navigation's link, a thumbnail or a file, on the row itself. */
  readonly typed: boolean;
  /** Whether the column is a blob's, which renders typed in every mode. */
  readonly blob: boolean;
  /** The column's width in pixels. */
  readonly width: number | undefined;
}

/** The editing entry point's editable columns, loaded once for every grid. */
let editableColumns: Promise<TmEditableColumnFor> | null = null;

/**
 * Loads the editing entry point's editable columns once.
 *
 * @returns The column function.
 */
function loadEditableColumns(): Promise<TmEditableColumnFor> {
  editableColumns ??= import('@tellma/core/entity-editing').then(
    ({ tmEditableGridColumnFor }) => tmEditableGridColumnFor,
    (error: unknown) => {
      // A failed load is forgotten, so the next mode change loads it again.
      editableColumns = null;
      throw error;
    },
  );
  return editableColumns;
}

/** The edit columns' loader, which a test may replace to fail or delay the load. Internal. */
export const ɵtmChildGridEditLoader: {
  /** Loads the editing entry point and resolves its editable columns. */
  load: () => Promise<TmEditableColumnFor>;
} = { load: loadEditableColumns };

/**
 * The grid of a child collection, rendered by `tm-child-grid` in a deferred
 * chunk. Internal: a layout writes `tm-child-grid`.
 */
@Component({
  selector: 'tm-child-grid-view',
  imports: [TmButton, TmGrid, TmGridColumn, TmGridDisplayDef, TmValue],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'tm-child-grid-view' },
  styles: `
    :host,
    .tm-child-grid-view__grid {
      display: block;
      block-size: 100%;
    }

    :host {
      position: relative;
    }

    /* The edit side's failed load, over the grid's status band: the message in the error colour, then Try again. */
    .tm-child-grid-view__failed {
      position: absolute;
      inset-inline: var(--border-width);
      inset-block-end: var(--border-width);
      box-sizing: border-box;
      display: flex;
      align-items: center;
      gap: var(--space-2);
      block-size: var(--grid-status-height);
      margin: 0;
      padding-inline: var(--space-3);
      background: var(--surface-card);
      color: var(--error);
      font-size: var(--text-sm);
    }
  `,
  template: `
    <tm-grid
      class="tm-child-grid-view__grid"
      [gridId]="gridId()"
      [contentKey]="contentKey()"
      [field]="field()"
      [data]="field() === undefined ? none : undefined"
      [rowId]="rowId"
      [readonly]="!editable()"
      [newRow]="adds() ? newRow : undefined"
      [size]="size()"
    >
      @for (column of gridColumns(); track column.plan.key) {
        <tm-grid-column
          [key]="column.key"
          [type]="column.config.type ?? 'text'"
          [header]="column.plan.label()"
          [value]="column.config.value"
          [format]="column.config.format"
          [formatParts]="column.config.formatParts"
          [parse]="column.config.parse"
          [options]="column.config.options"
          [optionLabel]="column.config.optionLabel"
          [optionValue]="column.config.optionValue"
          [readonly]="column.config.readonly ?? true"
          [width]="column.width"
          [align]="column.config.align"
          [minDecimals]="column.config.minDecimals"
          [maxDecimals]="column.config.maxDecimals"
          [search]="column.config.search"
          [itemId]="column.config.itemId"
          [itemLabel]="column.config.itemLabel"
          [itemDescription]="column.config.itemDescription"
          [itemLabelParts]="column.config.itemLabelParts"
          [advancedSearch]="column.config.advancedSearch"
          [create]="column.config.create"
          [edit]="column.config.edit"
          [resolvePastedLabels]="column.config.resolvePastedLabels"
        >
          <!-- A navigation's link in view mode, a thumbnail or a file in every mode: the typed value -->
          @if (column.typed && (column.blob || !editable())) {
            <tm-value *tmGridDisplay="let value" surface="grid" [property]="column.plan.resolution!" [value]="related(column, value)" />
          }
        </tm-grid-column>
      }
    </tm-grid>

    @if (failed()) {
      <!-- The edit side failed to load in edit mode: say so, and offer the load again -->
      <p class="tm-child-grid-view__failed">
        <span role="alert">{{ failedText() }}</span>
        <button tmButton type="button" variant="ghost" size="sm" (click)="retry()">{{ retryText() }}</button>
      </p>
    }
  `,
})
export class TmChildGridView {
  readonly #record = injectTmDetailsRecord();
  readonly #injector = inject(Injector);
  readonly #entities = inject(TmEntities);
  readonly #names = inject(TmDisplayNames);
  readonly #l10n = inject(TmLocalization);
  readonly #scope = inject(TM_DETAILS_SCOPE, { optional: true });

  /** The child collection. */
  readonly child: InputSignal<TmChildDescriptor> = input.required<TmChildDescriptor>();

  /** The columns, by keys relative to the child entity. */
  readonly columns: InputSignal<readonly TmColumnInput<string>[]> = input.required<readonly TmColumnInput<string>[]>();

  /** A new row's values, beside its id. */
  readonly defaults: InputSignal<(() => TmJsonObject) | undefined> = input<(() => TmJsonObject) | undefined>(undefined);

  /** Whether the grid edits now. */
  readonly editable: InputSignal<boolean> = input.required<boolean>();

  /** Whether the grid is never editable, so its edit columns never load. */
  readonly readonly: InputSignal<boolean> = input(false);

  /** The size the grid draws at. */
  readonly size: InputSignal<TmControlSize> = input.required<TmControlSize>();

  /** The grid. */
  protected readonly grid: Signal<TmGrid<TmJsonObject> | undefined> = viewChild<TmGrid<TmJsonObject>>(TmGrid);

  /** The rows of a record whose read left the collection out. */
  protected readonly none: readonly TmJsonObject[] = [];

  /** The editing entry point's editable columns, once loaded. */
  readonly #editableColumnFor = signal<TmEditableColumnFor | null>(null);

  /** Whether the placeholder row adds rows: once the grid edits and its edit columns have loaded. */
  protected readonly adds = computed(() => this.editable() && this.#editableColumnFor() !== null);

  /** Whether the last load of the edit columns failed. */
  readonly #loadFailed = signal(false);

  /** Whether the grid should edit but its edit columns failed to load: the message and Try again show. */
  protected readonly failed = computed(() => this.editable() && this.#loadFailed() && this.#editableColumnFor() === null);

  /** The failed load's message. */
  protected readonly failedText = computed(() => this.#l10n.ɵtext('tm.details.listEditorFailed')());

  /** The failed load's retry label. */
  protected readonly retryText = computed(() => this.#l10n.ɵtext('tm.details.retry')());

  /** The child entity's descriptor. */
  readonly #descriptor = computed(() => this.#entities.get(this.child().entity));

  /** The child row's key member. */
  readonly #keyJson = computed(() => {
    const descriptor = this.#descriptor();
    return descriptor.property(descriptor.key.property)?.json ?? 'id';
  });

  /** The grid's id: the record's entity and the collection, so scroll and widths are kept per collection. */
  protected readonly gridId = computed(() => `details:${this.#record.descriptor.name}:${this.child().name}`);

  /** The grid's content key: the record's id, so each record keeps its own scroll. */
  protected readonly contentKey = computed(() => this.#record.id() ?? 'new');

  /** The collection's form field; `undefined` while the working copy lacks the collection. */
  protected readonly field = computed<FieldTree<TmJsonObject[]> | undefined>(() => {
    const json = this.child().json;
    const rows = (this.#record.model() as Readonly<Record<string, TmJson | undefined>>)[json];
    const form = this.#record.form as unknown as Readonly<Record<string, FieldTree<TmJsonObject[]> | undefined>>;
    return Array.isArray(rows) ? form[json] : undefined;
  });

  /** A row's id. */
  protected readonly rowId = (row: TmJsonObject): TmId => row[untracked(this.#keyJson)] as TmId;

  /** A new row: the defaults, the next new row's id and every other member as `null`. */
  protected readonly newRow = (): TmJsonObject => this.#record.ɵnewChildRow(untracked(this.child), untracked(this.defaults)?.() ?? {});

  /** The select plan over the child entity, which reads each column's key. */
  readonly #plan = computed(() => tmSelectPlan(this.#descriptor(), this.columns(), [], { injector: this.#injector }));

  /** The columns as the grid binds them, made again on a language switch and once the edit columns load. */
  protected readonly gridColumns: Signal<readonly TmChildGridColumn[]> = computed(() => {
    const editableColumnFor = this.#editableColumnFor();
    return this.#plan().columns.map((plan) => {
      const config = editableColumnFor !== null ? editableColumnFor(plan, { injector: this.#injector }) : ɵtmGridColumnFor(plan, { rows: 'model', injector: this.#injector }, true);
      const resolution = plan.resolution;
      const kind = resolution?.end.kind;
      const own = resolution !== null && resolution.steps.length === 0;
      const input = typeof plan.input === 'object' && 'path' in plan.input ? plan.input : null;
      // A navigation or a blob on the row is its key member, which a read-only column reads through a function.
      const end = resolution?.end;
      const foreignKey = own && end !== undefined && end.kind !== 'value' && end.kind !== 'text' ? end.navigation.foreignKey : undefined;
      return {
        plan,
        config,
        key: config.key ?? (foreignKey === undefined ? undefined : this.#descriptor().property(foreignKey)?.json),
        typed: own && (kind === 'navigation' || kind === 'image' || kind === 'file'),
        blob: kind === 'image' || kind === 'file',
        width: input?.width ?? config.width,
      };
    });
  });

  /** What the grid displays, for the page context: its columns and at most 200 rows. */
  readonly #visible: Signal<TmVisibleData> = computed(() => {
    const columns = this.gridColumns().slice(0, VISIBLE_COLUMNS);
    const rows = this.field()?.().value() ?? this.none;
    const key = this.#keyJson();
    return {
      columns: columns.map((column) => ({ path: column.plan.key, label: column.plan.label() })),
      rows: rows.slice(0, VISIBLE_ROWS).map((row) => {
        const id = row[key] as TmId | null | undefined;
        // A new row's negative id is no record's.
        const stored = id !== null && id !== undefined && !(typeof id === 'number' && id < 0);
        return { id: stored ? id : null, cells: Object.fromEntries(columns.flatMap((column) => this.#visibleCells(column, row))) };
      }),
      total: rows.length,
      truncated: rows.length > VISIBLE_ROWS,
    };
  });

  /** Loads the edit columns once the record may be edited, and registers the collection once its rows render. */
  constructor() {
    // --- The edit columns load once the record may be edited; a failed load tries again at the next mode change ---
    effect(() => {
      const mayEdit = !this.readonly() && (this.editable() || this.#record.can.edit());
      this.#record.mode();
      if (mayEdit && this.#editableColumnFor() === null) {
        untracked(() => this.#loadEdit());
      }
    });

    // --- The collection registers once the grid's rows have rendered ---
    const ready = signal(false);
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      ready.set(true);
      this.#record.registerCollection(
        untracked(this.child).name,
        { ready: ready.asReadonly(), visible: this.#visible, reveal: (rowId, options) => this.#reveal(rowId, options) },
        destroyRef,
      );
    });
  }

  /** Loads the edit columns again after a failed load: the failed load's Try again. */
  protected retry(): void {
    this.#loadEdit();
  }

  /** Loads the edit columns; a failure shows in edit mode, and a failed prefetch in view mode shows nothing. */
  #loadEdit(): void {
    ɵtmChildGridEditLoader.load().then(
      (columnFor) => {
        this.#loadFailed.set(false);
        this.#editableColumnFor.set(columnFor);
      },
      () => this.#loadFailed.set(true),
    );
  }

  /**
   * The value a typed cell renders: the related row the read returned, else the id.
   *
   * @param column - The column.
   * @param value - The cell's value: the related row's or the blob's id.
   * @returns The value.
   */
  protected related(column: TmChildGridColumn, value: unknown): unknown {
    const end = column.plan.resolution?.end;
    const target = end === undefined ? undefined : end.kind === 'navigation' ? end.navigation.target : end.kind === 'image' || end.kind === 'file' ? 'core.Blob' : undefined;
    const id = typeof value === 'number' || typeof value === 'string' ? (value as TmId) : null;
    return (target === undefined ? undefined : this.#record.related.get(target, id)) ?? value;
  }

  /**
   * A cell as the page context carries it: a navigation as its display name with its id beside it, any other value as the model holds it.
   *
   * @param column - The column.
   * @param row - The row.
   * @returns The cell's entries.
   */
  #visibleCells(column: TmChildGridColumn, row: TmJsonObject): (readonly [string, TmJson])[] {
    const read = column.config.value;
    const value = ((read !== undefined ? read(row) : column.key === undefined ? null : row[column.key]) ?? null) as TmJson;
    const end = column.plan.resolution?.end;
    if (end?.kind === 'navigation') {
      const id = typeof value === 'number' || typeof value === 'string' ? (value as TmId) : null;
      const related = this.#record.related.get(end.navigation.target, id);
      const name = id === null ? null : related !== undefined ? this.#names.of(end.navigation.target, related)() : (this.#names.label(end.navigation.target, id)() ?? String(id));
      return [
        [column.plan.key, name],
        [`${column.plan.key}.Id`, id],
      ];
    }
    return [[column.plan.key, value]];
  }

  /**
   * Scrolls a row into view, revealing the tab and the card around the grid, and focuses its cell with `column`.
   *
   * @param rowId - The row's id.
   * @param options - Whether to flash the row, and the column to focus, by its path relative to the child entity.
   * @returns Whether the row was found.
   */
  #reveal(rowId: TmId, options: { readonly flash?: boolean; readonly column?: string } = {}): boolean {
    const grid = untracked(this.grid);
    if (grid === undefined) {
      return false;
    }
    this.#scope?.reveal();
    const key = options.column === undefined ? undefined : this.#keyOf(options.column);
    const found = grid.scrollToRow(rowId, { ...(options.flash !== undefined && { flash: options.flash }), ...(key !== undefined && { column: key }) });
    if (found && options.column !== undefined) {
      // The tab or the card shows the grid with the next render; focus waits for it.
      afterNextRender(() => grid.focus(), { injector: this.#injector });
    }
    return found;
  }

  /**
   * The grid column a path names: its column's key, or a navigation's by its key property.
   *
   * @param path - The path, such as `Amount`, `Center` or `CenterId`.
   * @returns The grid column's key, or `undefined` when no column shows it, or its column has no key to focus by.
   */
  #keyOf(path: string): string | undefined {
    const folded = path.toLowerCase();
    const column = untracked(this.gridColumns).find((candidate) => {
      const end = candidate.plan.resolution?.end;
      const foreignKey = end?.kind === 'navigation' || end?.kind === 'image' || end?.kind === 'file' ? end.navigation.foreignKey : undefined;
      return candidate.plan.key.toLowerCase() === folded || foreignKey?.toLowerCase() === folded;
    });
    return column?.key;
  }
}
