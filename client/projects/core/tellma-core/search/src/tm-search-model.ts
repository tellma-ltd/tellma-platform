// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Injector } from '@angular/core';
import {
  TmEntities,
  type TmColumnInput,
  type TmColumnPreference,
  type TmEntityDeclarationInput,
  type TmEntityDescriptor,
  type TmSearchQuery,
  type TmTabDeclaration,
} from '@tellma/core';
import type { TmColumnSource } from '@tellma/core/entity';

// The search page's model (spec 0030 §1-§4): the setting a host opens with,
// the state it shows, and the facts both derive from the entity and its
// declaration (the code default columns, the tile's and the tree's extra
// paths, the page sizes). Everything here is plain data and pure functions,
// so the precedence, the canonical URL, the history rules and the request
// body are tested without a page.

// -----------------------------------------------------------------------------
// The setting
// -----------------------------------------------------------------------------

/** Where the page runs: an entity's search page, a picker's advanced search, or a drill-through list. */
export type TmSearchMode = 'page' | 'picker' | 'list';

/** The width class: `narrow` below `md`, `wide` from `md` up. */
export type TmSearchWidth = 'narrow' | 'wide';

/** How the rows show. */
export type TmSearchView = 'grid' | 'tiles';

/** The members of an entity declaration the search page reads. */
export type TmSearchDeclaration = Pick<TmEntityDeclarationInput<string>, 'columns' | 'orderBy' | 'tile' | 'filters' | 'tabs'>;

/** The value of an entity's view preference: each width class's view when it differs from the class default, and whether a tree shows as a list. */
export interface TmSearchViewPreference {
  /** The view below `md`, when not tiles. */
  readonly narrow?: TmSearchView;
  /** The view from `md` up, when not grid. */
  readonly wide?: TmSearchView;
  /** Whether a tree entity shows as a flat list. */
  readonly list?: boolean;
}

/** An entity's search preferences, as the user's preferences read them. */
export interface TmSearchPreferenceValues {
  /** The shown columns; `null` for the code default. */
  readonly columns: readonly TmColumnPreference[] | null;
  /** The view halves and the list choice. */
  readonly view: TmSearchViewPreference;
  /** The page size. */
  readonly pageSize: number;
}

/** What a search host opens with: the entity, where the page runs, how wide it is, and the user's preferences. */
export interface TmSearchSetting {
  /** Where the page runs. */
  readonly mode: TmSearchMode;
  /** The entity's descriptor. */
  readonly descriptor: TmEntityDescriptor;
  /** The entity's declaration members the page reads. */
  readonly declaration: TmSearchDeclaration;
  /** The width class. */
  readonly width: TmSearchWidth;
  /** The user's preferences for the entity. */
  readonly preferences: TmSearchPreferenceValues;
  /** In picker mode, the leaves every read applies: the picker's filter and the field's. */
  readonly fixedLeaves?: readonly string[];
  /** In list mode, the list's segment, such as `sales-invoice-lines`. */
  readonly segment?: string;
  /** The injector the entity registry, the URLs, the localization and the telemetry are read through. */
  readonly injector: Injector;
}

/** The query state a search page shows. */
export interface TmSearchState {
  /**
   * The query in its canonical form: every field at its code default left
   * out, the view left out at the width default, and the columns only when
   * they travel. It prints as the page's canonical URL.
   */
  readonly query: TmSearchQuery;
  /** The shown columns, in order, each key in its canonical case. */
  readonly columns: readonly TmColumnInput<string>[];
  /** Where the shown columns came from. */
  readonly columnsFrom: TmColumnSource;
  /** In list mode, the list's fixed filter from its `#d=` payload; `null` for none. */
  readonly listFilter?: string | null;
}

/** A query state's every field at its value, the code defaults filled in. */
export interface TmSearchEffective {
  /** The shown tab; `null` when the declaration has none. */
  readonly tab: TmTabDeclaration | null;
  /** The search text. */
  readonly q: string;
  /** Whether inactive rows show. */
  readonly inactive: boolean;
  /** The 1-based page. */
  readonly page: number;
  /** The page size. */
  readonly size: number;
  /** How the rows show. */
  readonly view: TmSearchView;
  /** Whether the rows show as a tree: a tree entity not shown as a list. */
  readonly tree: boolean;
}

// -----------------------------------------------------------------------------
// Constants
// -----------------------------------------------------------------------------

/** The page sizes a search page offers. */
export const TM_SEARCH_PAGE_SIZES: readonly number[] = [20, 50, 100, 200];

/** The page size a search page shows by default. */
export const TM_SEARCH_DEFAULT_PAGE_SIZE = 50;

/** The string key that labels a user's expression column. */
export const TM_SEARCH_EXPRESSION_LABEL = 'tm.search.expressionColumn';

// -----------------------------------------------------------------------------
// Derived facts
// -----------------------------------------------------------------------------

/**
 * The view a width class shows by default.
 *
 * @param width - The width class.
 * @returns Tiles below `md`, the grid from `md` up.
 */
export function tmSearchWidthView(width: TmSearchWidth): TmSearchView {
  return width === 'narrow' ? 'tiles' : 'grid';
}

/**
 * Whether an entity shows as a tree by default.
 *
 * @param descriptor - The entity's descriptor.
 * @returns Whether its stack declares the tree capability.
 */
export function tmSearchIsTree(descriptor: TmEntityDescriptor): boolean {
  return descriptor.capabilities.has('tree');
}

/**
 * Whether an entity's rows can be inactive.
 *
 * @param descriptor - The entity's descriptor.
 * @returns Whether its stack declares the activatable capability.
 */
export function tmSearchIsActivatable(descriptor: TmEntityDescriptor): boolean {
  return descriptor.capabilities.has('activatable');
}

/**
 * The page sizes an entity offers: the search page's sizes within its
 * largest page.
 *
 * @param descriptor - The entity's descriptor.
 * @returns The sizes, smallest first.
 */
export function tmSearchPageSizes(descriptor: TmEntityDescriptor): readonly number[] {
  return TM_SEARCH_PAGE_SIZES.filter((size) => size <= descriptor.limits.maxTake);
}

/**
 * The last page the server serves at a size: its skip window, divided by
 * the size and rounded up.
 *
 * @param descriptor - The entity's descriptor.
 * @param size - The page size.
 * @returns The last page.
 */
export function tmSearchMaxPage(descriptor: TmEntityDescriptor, size: number): number {
  return Math.max(1, Math.ceil(descriptor.limits.maxSkipWindow / size));
}

/**
 * The page that keeps the first visible row when the page size changes.
 *
 * @param page - The 1-based page at the old size.
 * @param oldSize - The old size.
 * @param newSize - The new size.
 * @returns The 1-based page at the new size.
 *
 * @example
 * ```ts
 * tmSearchPageForSize(3, 50, 20); // 6: rows 101-150 become rows 101-120
 * ```
 */
export function tmSearchPageForSize(page: number, oldSize: number, newSize: number): number {
  return Math.floor(((page - 1) * oldSize) / newSize) + 1;
}

/**
 * The declared tabs' first tab, which the URL leaves out.
 *
 * @param declaration - The declaration.
 * @returns The tab, or `null` when none is declared.
 */
export function tmSearchFirstTab(declaration: TmSearchDeclaration): TmTabDeclaration | null {
  return declaration.tabs?.[0] ?? null;
}

/**
 * A query state's every field at its value.
 *
 * @param setting - The page's setting.
 * @param query - The canonical query.
 * @returns The values.
 */
export function tmSearchEffective(setting: TmSearchSetting, query: TmSearchQuery): TmSearchEffective {
  const tabs = setting.declaration.tabs ?? [];
  return {
    tab: tabs.find((tab) => tab.key === query.tab) ?? tabs[0] ?? null,
    q: query.q ?? '',
    inactive: query.inactive === true,
    page: query.page ?? 1,
    size: query.size ?? TM_SEARCH_DEFAULT_PAGE_SIZE,
    view: query.view ?? tmSearchWidthView(setting.width),
    tree: tmSearchIsTree(setting.descriptor) && query.list !== true,
  };
}

// -----------------------------------------------------------------------------
// Columns
// -----------------------------------------------------------------------------

/**
 * A column input's key: its path, or an expression's text.
 *
 * @param column - The column input.
 * @returns The key.
 */
export function tmSearchColumnKey(column: TmColumnInput<string>): string {
  return typeof column === 'string' ? column : 'expression' in column ? column.expression : column.path;
}

/**
 * Whether a column input is an expression column.
 *
 * @param column - The column input.
 * @returns Whether it is one.
 */
export function tmSearchIsExpression(column: TmColumnInput<string>): column is Extract<TmColumnInput<string>, { readonly expression: string }> {
  return typeof column === 'object' && 'expression' in column;
}

/**
 * Whether two column lists show the same columns in the same order: path
 * keys compared ignoring case, expressions by their exact text.
 *
 * @param a - One list.
 * @param b - The other.
 * @returns Whether they are the same.
 */
export function tmSearchSameColumns(a: readonly TmColumnInput<string>[], b: readonly TmColumnInput<string>[]): boolean {
  return (
    a.length === b.length &&
    a.every((column, index) => {
      const other = b[index]!;
      if (tmSearchIsExpression(column) || tmSearchIsExpression(other)) {
        return tmSearchIsExpression(column) && tmSearchIsExpression(other) && column.expression === other.expression;
      }
      return tmSearchColumnKey(column).toLowerCase() === tmSearchColumnKey(other).toLowerCase();
    })
  );
}

/**
 * The code default columns: the declaration's, else the display name's
 * prefix and source, the other natural keys, then `IsActive` on an
 * activatable entity.
 *
 * @param descriptor - The entity's descriptor.
 * @param declaration - The entity's declaration.
 * @returns The columns.
 */
export function tmSearchDefaultColumns(descriptor: TmEntityDescriptor, declaration: TmSearchDeclaration): readonly TmColumnInput<string>[] {
  if (declaration.columns !== undefined && declaration.columns.length > 0) {
    return declaration.columns;
  }
  const { prefix, source } = descriptor.displayName;
  const keys: string[] = [];
  const add = (key: string): void => {
    if (!keys.some((other) => other.toLowerCase() === key.toLowerCase())) {
      keys.push(key);
    }
  };
  if (prefix !== undefined) {
    add(prefix);
  }
  add(source);
  descriptor.naturalKeys.forEach(add);
  if (tmSearchIsActivatable(descriptor)) {
    add('IsActive');
  }
  return keys;
}

/**
 * The path keys of a shown column list, as a URL's `cols` carries them.
 *
 * @param columns - The shown columns.
 * @returns The keys, or `null` when an expression column shows, since none travels.
 */
export function tmSearchPathKeys(columns: readonly TmColumnInput<string>[]): readonly string[] | null {
  return columns.some(tmSearchIsExpression) ? null : columns.map(tmSearchColumnKey);
}

/**
 * The column input a key reads as: the declared column with that key,
 * matched ignoring case, so its custom cell and width carry; else the key
 * itself.
 *
 * @param declaration - The entity's declaration.
 * @param key - The key.
 * @returns The input.
 */
export function tmSearchColumnInput(declaration: TmSearchDeclaration, key: string): TmColumnInput<string> {
  const folded = key.toLowerCase();
  const declared = declaration.columns?.find((column) => !tmSearchIsExpression(column) && tmSearchColumnKey(column).toLowerCase() === folded);
  if (declared === undefined) {
    return key;
  }
  // The declared input, under the key's canonical case.
  return typeof declared === 'string' ? key : { ...declared, path: key };
}

// -----------------------------------------------------------------------------
// Extra paths: the tile's and the tree's
// -----------------------------------------------------------------------------

/**
 * The paths a tree reads beside the columns: the parent, the subtree's size
 * and, on an activatable tree, the active subtree's size.
 *
 * @param descriptor - The entity's descriptor.
 * @returns The paths; none for an entity that is not a tree.
 */
export function tmSearchTreeKeys(descriptor: TmEntityDescriptor): readonly string[] {
  if (!tmSearchIsTree(descriptor)) {
    return [];
  }
  return ['ParentId', 'SubtreeCount', ...(tmSearchIsActivatable(descriptor) ? ['ActiveSubtreeCount'] : [])];
}

/**
 * The paths a tile reads: the declared parts, or by default the first image
 * navigation, the display name, the first natural key the display name does
 * not show, and `IsActive` on an activatable entity. A tile's title is the
 * display name unless declared. An image given as its key, such as
 * `ImageId`, reads as its navigation, so the thumbnail's variants come with
 * it. A tile drawn by a component reads none.
 *
 * @param setting - The page's setting.
 * @returns The paths.
 */
export function tmSearchTilePaths(setting: TmSearchSetting): readonly string[] {
  const { descriptor, declaration } = setting;
  const entities = setting.injector.get(TmEntities);
  const tile = declaration.tile;
  if (tile !== undefined && 'component' in tile) {
    return [];
  }
  const { prefix, source } = descriptor.displayName;
  const displayName = [...(prefix === undefined ? [] : [prefix]), source];
  const resolvesTo = (path: string, kind: string): boolean => entities.resolve(descriptor.name, path)?.end.kind === kind;

  // --- No tile declared: the defaults of each part ---
  if (tile === undefined) {
    const image = descriptor.navigations.find((navigation) => resolvesTo(navigation.name, 'image'))?.name;
    const shown = new Set(displayName.map((path) => path.toLowerCase()));
    const subtitle = descriptor.naturalKeys.find((key) => !shown.has(key.toLowerCase()));
    return [
      ...(image === undefined ? [] : [image]),
      ...displayName,
      ...(subtitle === undefined ? [] : [subtitle]),
      ...(tmSearchIsActivatable(descriptor) ? ['IsActive'] : []),
    ];
  }

  // --- A declared tile: its parts, the title defaulting to the display name ---
  const image = tile.image;
  const imagePath =
    image !== undefined && /Id$/.test(image) && resolvesTo(image, 'value') && resolvesTo(image.slice(0, -2), 'image') ? image.slice(0, -2) : image;
  return [
    ...(imagePath === undefined ? [] : [imagePath]),
    ...(tile.title === undefined ? displayName : [tile.title]),
    ...[tile.subtitle, tile.topEnd, tile.badge].filter((path) => path !== undefined),
  ];
}
