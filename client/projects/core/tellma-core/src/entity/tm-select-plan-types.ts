// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Signal } from '@angular/core';

import type { TmColumnInput, TmPlanRow } from './tm-entity-declaration';
import type { TmPathResolution } from './tm-entity-descriptors';
import type { TmDataType, TmValueDescriptor } from './tm-entity-metadata';
import type { TmQueryRowSet } from './tm-rows-to-objects';

// The shapes of a select plan (spec 0029 §6), as types only. `tmSelectPlan`,
// which builds one, is `@tellma/core/entity`'s; the shapes live in the
// primary so that the search page's context, which a declaration's page
// actions receive, can name its plan before either entry point loads.

/** What a column shows: a scalar, a multilingual group, a related row, a blob, or an expression. */
export type TmColumnKind = 'value' | 'text' | 'navigation' | 'image' | 'file' | 'expression';

/** The filter control a column's type takes; `null` on a column with no filter. */
export type TmFilterKind = 'text' | 'number' | 'date' | 'instant' | 'enum' | 'bool' | 'navigation';

/** Why a key was left out of a plan: it does not resolve, or the user may not read it. */
export type TmDroppedReason = 'unknown' | 'unreachable';

/** Where a plan's keys came from, as the `tm.entity.column-dropped` telemetry reports it. */
export type TmColumnSource = 'url' | 'preference' | 'declaration';

/** A key a plan left out, and why. */
export interface TmDroppedKey {
  /** The key, as given. */
  readonly key: string;
  /** Why it was left out. */
  readonly reason: TmDroppedReason;
}

/**
 * One shown column of a select plan: what it reads, how it is labelled,
 * filtered and ordered, and how a row's cell is read.
 */
export interface TmColumnPlan {
  /** The column's key in canonical case, such as `Parent.Code`; an expression's text. */
  readonly key: string;
  /** What the column shows. */
  readonly kind: TmColumnKind;
  /** The column input the plan was given, with its display, width and picker filter. */
  readonly input: TmColumnInput<string>;
  /** The key read against the entity; `null` for an expression. */
  readonly resolution: TmPathResolution | null;
  /**
   * The type facts the cell formats by: the property of a value, the first
   * slot of a group, an id for a navigation or a blob, and an expression's
   * once a row set types it. `null` for an expression no row set has typed.
   */
  readonly descriptor: TmValueDescriptor | null;
  /** The cell's type; `null` until a row set types an expression. */
  readonly type: TmDataType | null;
  /** The column's slash label, such as "Parent / Code". */
  readonly label: Signal<string>;
  /** The filter control the column takes; `null` for none. */
  readonly filter: TmFilterKind | null;
  /**
   * The order terms that sort by the column, in the active slot.
   *
   * @param direction - The direction.
   * @returns The terms, such as `coalesce(Name2, Name) desc`; `null` when the column is not orderable.
   */
  orderBy(direction: 'asc' | 'desc'): string | null;
  /**
   * A row's cell value: a navigation's id, a group's shown slot, a blob's
   * id, and any other column's value as the wire sent it.
   *
   * @param row - The row.
   * @returns The value; `null` when the row has none.
   */
  value(row: TmPlanRow): unknown;
}

/** The query terms a list of column keys expands to, and how its row set is read back. */
export interface TmSelectPlan {
  /** The terms, comma separated, for a query's select: the root's key first, each term once. */
  readonly select: string;
  /** The shown columns, the keys in order; never an extra path or a dropped key. */
  readonly columns: readonly TmColumnPlan[];
  /** The keys and extra paths left out, and why. */
  readonly dropped: readonly TmDroppedKey[];
  /**
   * Reads a row set the plan's select returned: one row per wire row,
   * which reads any selected term by path and each column's value by its
   * key. An expression column takes its type from the row set.
   *
   * @param set - The row set.
   * @returns The rows, in order.
   */
  rows(set: TmQueryRowSet): readonly TmPlanRow[];
}
