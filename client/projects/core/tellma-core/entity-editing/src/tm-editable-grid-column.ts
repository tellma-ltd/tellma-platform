// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { assertInInjectionContext, inject, Injector, signal, untracked, type Signal } from '@angular/core';
import {
  TmDisplayNames,
  TmEntities,
  TmPermissions,
  type TmColumnPlan,
  type TmDisplayNameParts,
  type TmId,
  type TmNavigationDescriptor,
  type TmPropertyDescriptor,
} from '@tellma/core';
import { ɵtmGridColumnFor, ɵtmIsNewRow, type TmGridColumnConfig } from '@tellma/core/entity';

import { TmEntityPickers, ɵtmLabelParts } from './tm-entity-pickers';

// A form's grid column that edits its cells (spec 0029 §12.2's last
// column): `@tellma/core/entity`'s columns with their scalar edits, and a
// navigation at the row's own level, whose cell picks the related row
// through the entity picker binding (§14.5). It lives here, beside the
// binding, so a read-only grid's columns never reach the pickers.

/** How an editable grid column is made from a plan's column. */
export interface TmEditableGridColumnOptions {
  /** The injector the column reads the formats and the binding through; the current injection context when omitted. */
  readonly injector?: Injector;
}

/**
 * The `tm-grid-column` inputs a select plan's column binds in a form's
 * grid, whose rows are the form model's wire objects, read by JSON names.
 * Only a scalar at the row's own level edits, or a group there, whose cell
 * edits the active language's slot under its JSON name (made inside a
 * computed, the config follows a language switch), or a navigation there,
 * whose cell picks the related row through the entity's picker binding with
 * the column's filter, shows its display name with each part isolated, and
 * resolves pasted names in one query; it is read-only when the user may not
 * query the target. Navigations elsewhere, blobs, instants and expressions
 * stay read-only, and a write-once value edits only in a new row. A decimal
 * or a `long` displays and parses exactly, at its scale. A host binds
 * `header` to the column's label itself, which follows the language.
 *
 * @param column - The plan's column.
 * @param options - The injector.
 * @returns The inputs to bind.
 *
 * @example
 * ```html
 * @for (column of plan.columns; track column.key) {
 *   @let config = tmEditableGridColumnFor(column);
 *   <tm-grid-column [key]="config.key" [type]="config.type" [header]="column.label()" [readonly]="config.readonly" />
 * }
 * ```
 */
export function tmEditableGridColumnFor(column: TmColumnPlan, options: TmEditableGridColumnOptions = {}): Partial<TmGridColumnConfig> {
  if (options.injector === undefined) {
    assertInInjectionContext(tmEditableGridColumnFor);
  }
  const injector = options.injector ?? inject(Injector);
  // --- A navigation at the row's own level picks its related row; every other column edits as /entity's do ---
  const resolution = column.resolution;
  if (resolution?.end.kind === 'navigation' && resolution.steps.length === 0) {
    const navigation = resolution.end.navigation;
    return navigationColumn(column, navigation, injector.get(TmEntities).get(resolution.entity).property(navigation.foreignKey), injector);
  }
  return ɵtmGridColumnFor(column, { rows: 'model', injector }, true);
}

/**
 * The grid column of a navigation a form's grid edits: the related row's
 * id under its key property's JSON name, picked through the target's
 * binding with the column's filter, shown by its display name.
 *
 * @param column - The plan's column.
 * @param navigation - The navigation.
 * @param key - The navigation's key property, whose JSON name the cell's value is under.
 * @param injector - The injector the binding reads through.
 * @returns The inputs to bind.
 */
function navigationColumn(
  column: TmColumnPlan,
  navigation: TmNavigationDescriptor,
  key: TmPropertyDescriptor | undefined,
  injector: Injector,
): Partial<TmGridColumnConfig> {
  const target = navigation.target;
  const pickers = injector.get(TmEntityPickers);
  const names = injector.get(TmDisplayNames);
  const input = column.input;
  const filter = signal(typeof input === 'object' && 'path' in input ? (input.filter ?? null) : null).asReadonly();
  const binding = pickers.binding(target, { filter });

  // --- Who writes the cell: the key's ownership, and a target the user may search ---
  const ownership = navigation.ownership;
  const searchable = injector.get(TmPermissions).allows(target, 'query')();
  const readonly =
    !searchable || ownership === 'server-owned' || ownership === 'database-owned' || ownership === 'derived'
      ? true
      : ownership === 'write-once'
        ? (row: unknown) => !ɵtmIsNewRow(row)
        : false;

  // --- The display: the display name, its parts memoized per id so a cell follows the language ---
  const parts = new Map<string, Signal<TmDisplayNameParts | null>>();
  const partsOf = (id: TmId): TmDisplayNameParts | null => {
    let label = parts.get(String(id));
    if (label === undefined) {
      if (parts.size >= 1000) {
        parts.clear();
      }
      parts.set(String(id), (label = names.labelParts(target, id)));
    }
    return label();
  };
  const isId = (value: unknown): value is TmId => typeof value === 'number' || (typeof value === 'string' && value !== '');

  return {
    key: key?.json ?? navigation.json,
    type: 'entity',
    readonly,
    width: 200,
    search: binding.search,
    itemId: binding.itemId,
    itemLabel: binding.itemLabel,
    itemLabelParts: binding.itemLabelParts,
    ...(binding.itemDescription && { itemDescription: binding.itemDescription }),
    ...(binding.advancedSearch && { advancedSearch: binding.advancedSearch }),
    ...(binding.create && { create: binding.create }),
    ...(binding.edit && { edit: binding.edit }),
    format: (value) => (isId(value) ? (binding.displayWith(value) ?? '') : ''),
    formatParts: (value) => {
      if (!isId(value)) {
        return null;
      }
      const label = partsOf(value);
      if (label === null) {
        untracked(() => names.resolve(target, [value]));
      }
      return label && ɵtmLabelParts(label);
    },
    resolvePastedLabels: pickers.ɵpasteResolver(target, filter),
  };
}
