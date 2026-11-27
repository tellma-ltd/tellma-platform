// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Location, NgComponentOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
  untracked,
  viewChild,
  type InputSignal,
  type ModelSignal,
  type OutputEmitterRef,
  type Signal,
  type Type,
} from '@angular/core';
import {
  TmDisplayNames,
  TmLocalization,
  TmTelemetry,
  TmUrls,
  tmRowsToObjects,
  type TmColumnPlan,
  type TmId,
  type TmJsonObject,
  type TmLazyComponent,
  type TmPlanRow,
  type TmQueryRowSet,
} from '@tellma/core';
import { TmFormats, tmSelectPlan } from '@tellma/core/entity';
import type { TmGridRowActivation } from '@tellma/core-ui/grid';
import { TmTileContent, TmTiles, TmTilesEmpty, type TmTileImage, type TmTileLayout, type TmTileText } from '@tellma/core-ui/tiles';

import { tmSearchQuiet } from './tm-search-canonical';
import type { TmSearchKeys } from './tm-search-keys';
import { tmSearchIsActivatable, tmSearchTileParts, type TmSearchSetting } from './tm-search-model';
import { TmSearchState as TmSearchStateView, type TmSearchRegionState } from './tm-search-state';
import { ɵTM_SEARCH_TILES_STRINGS_EN } from './tm-search-tiles-strings';

// The tiles view (spec 0030 §2.3, §8): the same rows, selection and
// highlight as the grid, each row a tile whose parts the declaration's
// `tile` maps, or whose body its component draws. It loads in a chunk of its
// own the first time the page shows tiles, so a page that only ever shows
// the grid never downloads it. The region's states render in the tiles'
// empty template; the first load shows the tiles' own skeleton.

/** The value types a tile's top-end text shows as a number: tabular digits, at the physical right. */
const NUMERIC = new Set(['integer', 'decimal', 'float']);

/**
 * The component a lazy component's module holds.
 *
 * @param load - The lazy component.
 * @returns The component.
 */
async function componentOf(load: TmLazyComponent): Promise<Type<unknown>> {
  const loaded = await load();
  return typeof loaded === 'function' ? loaded : loaded.default;
}

/**
 * The search page's rows as tiles. Internal to the search page, which
 * renders it in the grid's place while the view is tiles.
 */
@Component({
  selector: 'tm-search-tiles',
  imports: [NgComponentOutlet, TmSearchStateView, TmTileContent, TmTiles, TmTilesEmpty],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <tm-tiles
      class="tm-search-tiles__tiles"
      [data]="rows()"
      [rowId]="rowId"
      [layout]="layout()"
      [ariaLabel]="label()"
      [loading]="loading()"
      selectable
      [selectedIds]="selectedIds()"
      (selectedIdsChange)="selectedIdsChange.emit($event)"
      [(activeRowId)]="activeRowId"
      (rowActivate)="rowActivate.emit($event)"
    >
      <!-- A declared tile component draws the body, once loaded; the mapped parts until then. -->
      @if (body(); as component) {
        <ng-template tmTileContent let-row let-id="rowId">
          <ng-container *ngComponentOutlet="component; inputs: { value: id, row }" />
        </ng-template>
      }

      <!-- No rows: the region's state in their place. -->
      <div *tmTilesEmpty class="tm-search-tiles__state">
        @if (state(); as shown) {
          <tm-search-state [state]="shown" [speaker]="keys()" [editable]="editable()" (clear)="clear.emit($event)" (retry)="retry.emit()" (edit)="edit.emit()" />
        }
      </div>
    </tm-tiles>
  `,
  styles: `
    :host {
      display: flex;
      flex: 1 1 auto;
      flex-direction: column;
      min-block-size: 0;
    }

    .tm-search-tiles__tiles {
      flex: 1 1 auto;
      min-block-size: 0;
    }
  `,
})
export class ɵTmSearchTiles {
  readonly #formats = inject(TmFormats);
  readonly #names = inject(TmDisplayNames);
  readonly #l10n = inject(TmLocalization);
  readonly #location = inject(Location);
  readonly #telemetry = inject(TmTelemetry);
  readonly #defaultUrls = inject(TmUrls);

  /** The page's setting: the entity, its declaration and the URLs. */
  readonly setting: InputSignal<TmSearchSetting> = input.required<TmSearchSetting>();

  /** The rows on screen. */
  readonly rows: InputSignal<readonly TmPlanRow[]> = input.required<readonly TmPlanRow[]>();

  /** The row set the rows were read from, whose wire rows name each row; `null` while none is held. */
  readonly set: InputSignal<TmQueryRowSet | null> = input.required<TmQueryRowSet | null>();

  /** A row's target, which its title links to; `null` for none. */
  readonly targetOf: InputSignal<(row: TmPlanRow) => string | null> = input.required<(row: TmPlanRow) => string | null>();

  /** The tiles' accessible name: the page's title. */
  readonly label: InputSignal<string> = input.required<string>();

  /** Whether the first load runs, so the tiles show their skeleton. */
  readonly loading: InputSignal<boolean> = input(false);

  /** The selected rows' ids. */
  readonly selectedIds: InputSignal<ReadonlySet<TmId>> = input.required<ReadonlySet<TmId>>();

  /** The highlighted row's id. */
  readonly activeRowId: ModelSignal<TmId | null> = model<TmId | null>(null);

  /** The region's state in place of rows; `null` for none. */
  readonly state: InputSignal<TmSearchRegionState | null> = input<TmSearchRegionState | null>(null);

  /** The page's keys, whose live region says an error view once it loads. */
  readonly keys: InputSignal<TmSearchKeys | null> = input<TmSearchKeys | null>(null);

  /** Whether the states offer Edit filter. */
  readonly editable: InputSignal<boolean> = input(false);

  /** Emits the ids of the checked rows. */
  readonly selectedIdsChange: OutputEmitterRef<ReadonlySet<string | number>> = output<ReadonlySet<string | number>>();

  /** Emits when a tile opens: a click, Enter, or a press on its title. */
  readonly rowActivate: OutputEmitterRef<TmGridRowActivation<TmPlanRow>> = output<TmGridRowActivation<TmPlanRow>>();

  /** Emits when a state asks to clear the filters or the search text. */
  readonly clear: OutputEmitterRef<'filters' | 'search'> = output<'filters' | 'search'>();

  /** Emits when the error view asks to try again. */
  readonly retry: OutputEmitterRef<void> = output<void>();

  /** Emits when a state asks to edit the filter expression. */
  readonly edit: OutputEmitterRef<void> = output<void>();

  /** The tiles, which the search field drives. */
  readonly tiles: Signal<TmTiles<TmPlanRow> | undefined> = viewChild<TmTiles<TmPlanRow>>(TmTiles);

  /** A row's id. */
  protected readonly rowId = (row: TmPlanRow): TmId => row.id;

  /** The declared tile component, once loaded; `null` for none, while it loads, or when it failed. */
  protected readonly body = signal<Type<unknown> | null>(null);

  /** The declared tile component's loader, read once. */
  readonly #component = computed(() => {
    const tile = this.setting().declaration.tile;
    return tile !== undefined && 'component' in tile ? tile.component : null;
  });

  /** Each row's wire object, by row, which the display names read. */
  readonly #objects = computed(() => {
    const set = this.set();
    const rows = this.rows();
    const objects = new WeakMap<TmPlanRow, TmJsonObject>();
    if (set !== null) {
      // The rows are the set's rows in order, so a row and its wire object share an index.
      tmRowsToObjects(this.setting().descriptor, set).forEach((object, index) => {
        const row = rows[index];
        if (row !== undefined) {
          objects.set(row, object);
        }
      });
    }
    return objects;
  });

  /** The tiles' layout, mapped from the declaration's tile and rebuilt when the setting or the targets move. */
  protected readonly layout = computed(() => this.#layout(this.setting(), this.targetOf()));

  /**
   * Registers the tiles' own strings, which ship with this chunk, and loads
   * the declared tile component, once; a failed load leaves the mapped parts
   * and is reported.
   */
  constructor() {
    this.#l10n.ɵregisterStrings('searchTiles', ɵTM_SEARCH_TILES_STRINGS_EN);
    let loaded: TmLazyComponent | null = null;
    effect(() => {
      const load = this.#component();
      if (load !== null && load !== loaded) {
        loaded = load;
        untracked(() =>
          componentOf(load).then(
            (component) => this.body.set(component),
            (error: unknown) => this.#telemetry.error(error, { area: 'tm.search' }),
          ),
        );
      }
    });
  }

  /**
   * The tiles' scroll offset, which an anchor remembers.
   *
   * @returns The offset in pixels.
   */
  scrollOffset(): number {
    return this.tiles()?.scrollOffset() ?? 0;
  }

  /**
   * Scrolls the tiles to an offset, as an anchor's restore does.
   *
   * @param offset - The offset in pixels.
   */
  scrollTo(offset: number): void {
    this.tiles()?.scrollTo(offset);
  }

  /**
   * The layout of a setting's tiles: the thumbnail, the
   * title (the display name's parts unless declared) linking to the row's
   * target, the subtitle, the top-end text (numeric for numbers) and the
   * badge.
   *
   * @param setting - The page's setting.
   * @param targetOf - A row's target.
   * @returns The layout.
   */
  #layout(setting: TmSearchSetting, targetOf: (row: TmPlanRow) => string | null): TmTileLayout<TmPlanRow> {
    const parts = tmSearchTileParts(setting);
    const entity = setting.descriptor.name;
    // The parts' columns, planned quietly: the page's own plan reported any part that dropped.
    const paths = [parts.image, parts.title, parts.subtitle, parts.topEnd, parts.badge].filter((path) => path !== null);
    const plan = tmSelectPlan(setting.descriptor, paths, [], { injector: tmSearchQuiet(setting).injector, source: 'declaration' });
    const column = (path: string | null): TmColumnPlan | null =>
      path === null ? null : (plan.columns.find((candidate) => candidate.key.toLowerCase() === path.toLowerCase()) ?? null);
    const [image, title, subtitle, topEnd, badge] = [parts.image, parts.title, parts.subtitle, parts.topEnd, parts.badge].map(column);
    const text = (of: TmColumnPlan, row: TmPlanRow): string | null => this.#formats.cell(of, row) || null;
    const objectOf = (row: TmPlanRow): TmJsonObject => this.#objects().get(row) ?? {};
    const urls = setting.urls ?? this.#defaultUrls;

    return {
      ...(image?.kind === 'image' && { image: (row: TmPlanRow): TmTileImage => this.#image(image, row, urls.blob.bind(urls)) }),
      // The title: a declared path as its column shows it, else the display name in its parts.
      title: (row) => (title === null ? this.#names.of(entity, objectOf(row))() : this.#formats.cell(title, row)),
      ...(title === null && {
        titleParts: (row: TmPlanRow) => {
          const name = this.#names.parts(entity, objectOf(row))();
          return name.prefix === null ? [name.name] : [name.prefix, name.name];
        },
      }),
      href: (row) => {
        const target = targetOf(row);
        return target === null ? null : this.#location.prepareExternalUrl(target);
      },
      ...(subtitle !== null && { bottomStart: (row: TmPlanRow) => text(subtitle, row) }),
      ...(topEnd !== null && {
        topEnd: (row: TmPlanRow): TmTileText | null => {
          const shown = text(topEnd, row);
          return shown === null || !NUMERIC.has(topEnd.type ?? '') ? shown : { text: shown, numeric: true };
        },
      }),
      ...(badge !== null && { bottomEnd: (row: TmPlanRow) => this.#badge(setting, badge, row) }),
    };
  }

  /**
   * A tile's thumbnail: the image's `thumb` variant when the blob lists one,
   * else the image itself; the placeholder glyph for a row with none.
   *
   * @param column - The image's column.
   * @param row - The row.
   * @param blob - Builds a blob's URL.
   * @returns The image.
   */
  #image(column: TmColumnPlan, row: TmPlanRow, blob: (kind: string, id: number, options?: { readonly variant?: string }) => string): TmTileImage {
    const id = column.value(row);
    const end = column.resolution?.end;
    if ((typeof id !== 'number' && !(typeof id === 'string' && /^\d+$/.test(id))) || end?.kind !== 'image') {
      return { src: null, alt: '' };
    }
    // The thumbnail is asked for only when the blob lists it, as a grid's thumbnail is.
    const variants = String(row.get(`${column.key}.Variants`) ?? '');
    const thumb = end.policy.thumbnail && variants.split(',').some((variant) => variant.trim() === 'thumb');
    // The title names the tile, so the thumbnail is decorative.
    return { src: blob(end.policy.kind, Number(id), thumb ? { variant: 'thumb' } : undefined), alt: '' };
  }

  /**
   * A tile's badge, by its value's type: `IsActive` on an activatable entity
   * as Active (success) or Inactive (neutral), another bool as its label
   * when true, and any other value as it shows (neutral).
   *
   * @param setting - The page's setting.
   * @param column - The badge's column.
   * @param row - The row.
   * @returns The badge, or `null` for none.
   */
  #badge(setting: TmSearchSetting, column: TmColumnPlan, row: TmPlanRow): TmTileText | null {
    const value = column.value(row);
    if (column.key === 'IsActive' && tmSearchIsActivatable(setting.descriptor)) {
      return value === true
        ? { text: this.#l10n.ɵtext('tm.searchTiles.active')(), badge: 'success' }
        : value === false
          ? { text: this.#l10n.ɵtext('tm.searchTiles.inactive')(), badge: 'neutral' }
          : null;
    }
    if (column.type === 'bool') {
      return value === true ? { text: column.label(), badge: 'neutral' } : null;
    }
    const shown = this.#formats.cell(column, row);
    return shown === '' ? null : { text: shown, badge: 'neutral' };
  }
}
