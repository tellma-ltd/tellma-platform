// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmPreferenceKey, type TmColumnInput, type TmColumnPreference, type TmPreferenceKey } from '@tellma/core';

import {
  TM_SEARCH_DEFAULT_PAGE_SIZE,
  TM_SEARCH_EXPRESSION_LABEL,
  TM_SEARCH_PAGE_SIZES,
  tmSearchColumnInput,
  tmSearchColumnKey,
  tmSearchIsExpression,
  type TmSearchDeclaration,
  type TmSearchView,
  type TmSearchViewPreference,
} from './tm-search-model';

// The search page's three preferences per entity (spec 0025 Appendix A,
// spec 0030 §3.4, §11.4, §14), all written behind: the shown columns, the
// view halves with the list choice, and the page size. Each reads as its
// fallback when unset or invalid, and a value equal to its fallback deletes
// the key, so a later change of the code default reaches the user.

/**
 * Whether a stored value is a column list: column keys and expression
 * columns, at least one.
 *
 * @param value - The stored value.
 * @returns Whether it is one, or `null`.
 */
function isColumns(value: unknown): value is readonly TmColumnPreference[] | null {
  return (
    value === null ||
    (Array.isArray(value) &&
      value.length > 0 &&
      value.every(
        (entry: unknown) =>
          (typeof entry === 'string' && entry !== '') ||
          (typeof entry === 'object' &&
            entry !== null &&
            typeof (entry as { readonly expression?: unknown }).expression === 'string' &&
            (entry as { readonly expression: string }).expression !== ''),
      ))
  );
}

/**
 * Whether a stored value is a view preference.
 *
 * @param value - The stored value.
 * @returns Whether it is one.
 */
function isView(value: unknown): value is TmSearchViewPreference {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return false;
  }
  const { narrow, wide, list } = value as Readonly<Record<string, unknown>>;
  const isHalf = (half: unknown): boolean => half === undefined || half === 'grid' || half === 'tiles';
  return isHalf(narrow) && isHalf(wide) && (list === undefined || typeof list === 'boolean');
}

/**
 * Whether a stored value is a page size the search page offers.
 *
 * @param value - The stored value.
 * @returns Whether it is one.
 */
function isPageSize(value: unknown): value is number {
  return typeof value === 'number' && TM_SEARCH_PAGE_SIZES.includes(value);
}

/**
 * The preference of an entity's shown columns; `null`, its fallback, is the
 * code default.
 *
 * @param entity - The entity's name, such as `gl.Center`.
 * @returns The key `search.<entity>.columns`.
 */
export function tmSearchColumnsKey(entity: string): TmPreferenceKey<readonly TmColumnPreference[] | null> {
  return tmPreferenceKey<readonly TmColumnPreference[] | null>(`search.${entity}.columns`, 'write-behind', null, isColumns);
}

/**
 * The preference of an entity's view halves and list choice.
 *
 * @param entity - The entity's name.
 * @returns The key `search.<entity>.view`.
 */
export function tmSearchViewKey(entity: string): TmPreferenceKey<TmSearchViewPreference> {
  return tmPreferenceKey<TmSearchViewPreference>(`search.${entity}.view`, 'write-behind', {}, isView);
}

/**
 * The preference of an entity's page size.
 *
 * @param entity - The entity's name.
 * @returns The key `search.<entity>.pageSize`.
 */
export function tmSearchPageSizeKey(entity: string): TmPreferenceKey<number> {
  return tmPreferenceKey<number>(`search.${entity}.pageSize`, 'write-behind', TM_SEARCH_DEFAULT_PAGE_SIZE, isPageSize);
}

/**
 * The column inputs a column preference reads as. A key takes the declared
 * column with that key, so its custom cell and width carry; an expression
 * whose text equals a declared expression column's takes that column's label
 * and type; any other expression is the user's own, labelled by its text.
 *
 * @param declaration - The entity's declaration.
 * @param preference - The stored columns.
 * @returns The inputs.
 */
export function tmSearchColumnsFromPreference(
  declaration: TmSearchDeclaration,
  preference: readonly TmColumnPreference[],
): readonly TmColumnInput<string>[] {
  return preference.map((entry) => {
    if (typeof entry === 'string') {
      return tmSearchColumnInput(declaration, entry);
    }
    const declared = declaration.columns?.find((column) => tmSearchIsExpression(column) && column.expression === entry.expression);
    return declared ?? { expression: entry.expression, labelKey: TM_SEARCH_EXPRESSION_LABEL };
  });
}

/**
 * The column preference of a shown column list: each path column's key, and
 * each expression column, declared or the user's, as its text.
 *
 * @param columns - The shown columns.
 * @returns The preference.
 */
export function tmSearchColumnsToPreference(columns: readonly TmColumnInput<string>[]): readonly TmColumnPreference[] {
  return columns.map((column) => (tmSearchIsExpression(column) ? { expression: column.expression } : tmSearchColumnKey(column)));
}

/**
 * A view preference with one width class's half set: deleted when it is
 * the class's default, and the other half and the list choice kept.
 *
 * @param preference - The current preference.
 * @param width - The width class.
 * @param view - The class's view.
 * @param classDefault - The class's default view.
 * @returns The new preference.
 */
export function tmSearchViewHalf(
  preference: TmSearchViewPreference,
  width: 'narrow' | 'wide',
  view: TmSearchView,
  classDefault: TmSearchView,
): TmSearchViewPreference {
  const next: { -readonly [K in keyof TmSearchViewPreference]: TmSearchViewPreference[K] } = { ...preference };
  delete next[width];
  return view === classDefault ? next : { ...next, [width]: view };
}

/**
 * A view preference with the list choice set: deleted when the tree shows
 * as a tree, and the halves kept.
 *
 * @param preference - The current preference.
 * @param list - Whether the tree shows as a list.
 * @returns The new preference.
 */
export function tmSearchViewList(preference: TmSearchViewPreference, list: boolean): TmSearchViewPreference {
  const next: { -readonly [K in keyof TmSearchViewPreference]: TmSearchViewPreference[K] } = { ...preference };
  delete next.list;
  return list ? { ...next, list: true } : next;
}
