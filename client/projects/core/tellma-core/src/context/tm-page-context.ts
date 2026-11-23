// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Signal } from '@angular/core';

import type { TmId, TmJson } from '../json/tm-json';
import type { TmPageAction } from './tm-page-action';
import type { TmPageKind } from './tm-page-kind';

// The page context's contracts (spec 0026 §9.1). Every routed page and every
// context-bearing dialog publishes one context: what it is, what it shows,
// what is wrong and what can be done there. Each member is a signal the
// publisher already holds, read only when a reader asks, so publishing costs
// nothing.

/**
 * What a routed page or a context-bearing dialog publishes about itself, for
 * the toolbars, menus, shortcuts, the support dump and a future assistant.
 * Frameworks publish it from state they already hold; a page registers its
 * own through `TmPageContexts.register` only for richer context than its
 * host's.
 *
 * @example
 * ```ts
 * inject(TmPageContexts).register({
 *   kind: 'custom', routeTemplate: '/:t/credit-review', title: this.title, status: this.status,
 *   state: computed(() => ({ threshold: this.threshold(), overLimit: this.overLimit() })),
 *   errors: signal([]), actions: signal([]),
 * });
 * ```
 */
export interface TmPageContext {
  /** The page's family. */
  readonly kind: TmPageKind;
  /** The entity the page shows, such as `gl.Center`, on an entity's page. */
  readonly entity?: string;
  /** The route's template, such as `/:t/centers/:id`, or a dialog's name, such as `panel:bell`; never a URL. */
  readonly routeTemplate: string;
  /** Whether the context is a dialog's, above the routed page; default `false`. */
  readonly overlay?: boolean;
  /** The page's title, localized, as displayed. */
  readonly title: Signal<string>;
  /** The key of the page's help article, such as `gl.center`; reserved for the help drawer. */
  readonly helpKey?: string;
  /**
   * The page's data state. It is `loading` while the page's read runs, also
   * while held content shows meanwhile (a refresh, a step to another record,
   * a return to a stale page), so a reader never takes held content for current.
   */
  readonly status: Signal<'loading' | 'ready' | 'error'>;
  /** The kind's own state: ids, codes, modes, flags and counts, as plain JSON. */
  readonly state: Signal<TmJson>;
  /** What the page's grids and lists display, when it shows any. */
  readonly visible?: Signal<TmVisibleData | undefined>;
  /** The form being edited, in a page's edit and create modes. */
  readonly form?: TmFormContext;
  /** What is wrong on the page now. */
  readonly errors: Signal<readonly TmContextError[]>;
  /**
   * The publisher's own actions. The registry publishes the context with
   * these merged with the actions of its page scope.
   */
  readonly actions: Signal<readonly TmPageAction[]>;
}

/** What a page's grids and lists display: the columns as displayed and the rows they show. */
export interface TmVisibleData {
  /** The columns, at most 50, in display order: each one's path and its label as displayed. */
  readonly columns: readonly TmVisibleColumn[];
  /**
   * The rows, at most 200, each holding what its cells show as plain JSON: a
   * decimal as its string, a navigation as its display name with its id under
   * `<path>.Id`, a date as `yyyy-MM-dd`, an instant as ISO 8601 in the display
   * zone's offset, and an enum as its code.
   */
  readonly rows: readonly TmVisibleRow[];
  /** The total count of rows, when known. */
  readonly total: number | null;
  /** Whether more rows are shown than `rows` carries. */
  readonly truncated: boolean;
}

/** One displayed column of {@link TmVisibleData}. */
export interface TmVisibleColumn {
  /** The column's path, such as `Parent` or `Lines[2].Quantity`. */
  readonly path: string;
  /** The column's label as displayed. */
  readonly label: string;
}

/**
 * One displayed row of {@link TmVisibleData}, such as
 * `{ id: 5, cells: { Name: 'Jeddah', Parent: 'West', 'Parent.Id': 2 } }`.
 */
export interface TmVisibleRow {
  /** The row's record id, or `null` for a row that has none, such as an unsaved line. */
  readonly id: TmId | null;
  /** What each cell shows, by the column's path. */
  readonly cells: Readonly<Record<string, TmJson>>;
}

/** One problem on a page: a validation error, a failed request or a client failure. */
export interface TmContextError {
  /** Where the problem comes from. */
  readonly source: 'validation' | 'request' | 'render' | 'client';
  /** The problem's code, such as `Required` or `query-invalid`. */
  readonly code: string;
  /** The problem's message, localized. */
  readonly message: string;
  /** The path of the field the problem is about, such as `Lines[2].Quantity`. */
  readonly path?: string;
  /** The trace id of the request that failed, for a request's problem. */
  readonly traceId?: string;
}

/** The form a page edits, in its edit and create modes. */
export interface TmFormContext {
  /** The form's fields, in display order. */
  readonly fields: Signal<readonly TmFormField[]>;
  /** Whether the form holds unsaved changes. */
  readonly dirty: Signal<boolean>;
  /** The paths written by `propose` and not edited since. */
  readonly assisted: Signal<ReadonlySet<string>>;
  /**
   * Writes a value through a field, as the user would, and marks its path in
   * `assisted`. Never saves.
   *
   * @param path - The field's path, such as `Code` or `Lines[2].Quantity`.
   * @param value - The value, as plain JSON.
   * @returns Whether the value was written: `false` when the path is unknown or read-only.
   */
  propose(path: string, value: TmJson): boolean;
}

/** One field of a {@link TmFormContext}. */
export interface TmFormField {
  /** The field's path, such as `Code` or `Lines[2].Quantity`. */
  readonly path: string;
  /** The string key of the field's label. */
  readonly labelKey: string;
  /** The field's label as displayed. */
  readonly label: string;
  /** The field's value, as plain JSON. */
  readonly value: TmJson;
  /** The field's error messages, localized. */
  readonly errors: readonly string[];
  /** Whether the user may edit the field now. */
  readonly editable: boolean;
}
