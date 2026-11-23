// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injector } from '@angular/core';

import { TmEntities } from '../entity/tm-entities';
import type { TmJson, TmJsonObject } from '../json/tm-json';
import type { TmPageContext, TmVisibleRow } from './tm-page-context';
import type { TmPageKind } from './tm-page-kind';

// The page context's snapshot (spec 0026 §9.6): every signal read once into
// plain JSON, the one serialisation the support dump and a future assistant
// share. Without page data, it leaves out the rows, the form's values and the
// errors' messages, and masks the user's text: the search's text and filters,
// a report's parameters, and a details page's title, which is a record's
// display name.

/** What masked text becomes. */
const MASK = '…';

/** The members of a search query that hold the user's text. */
const QUERY_TEXT = ['q', 'filter', 'columnFilters', 'custom'] as const;

/** A context as plain JSON, read at one moment. */
export interface TmPageContextSnapshot {
  /** The page's family. */
  readonly kind: TmPageKind;
  /** The route's template, or a dialog's name. */
  readonly routeTemplate: string;
  /** Whether the context is a dialog's. */
  readonly overlay: boolean;
  /** The entity the page shows, on an entity's page. */
  readonly entity?: string;
  /** The title as displayed; without page data, a details page's is the entity's singular label. */
  readonly title: string;
  /** The key of the page's help article. */
  readonly helpKey?: string;
  /** The page's data state. */
  readonly status: 'loading' | 'ready' | 'error';
  /** The kind's own state; without page data, its user text masked. */
  readonly state: TmJson;
  /** What the page's grids and lists display; the rows only with page data. */
  readonly visible?: TmVisibleSnapshot;
  /** The form being edited; the values only with page data. */
  readonly form?: TmFormSnapshot;
  /** What is wrong on the page; the messages only with page data. */
  readonly errors: readonly TmErrorSnapshot[];
  /** The context's actions, merged and in place order. */
  readonly actions: readonly TmActionSnapshot[];
}

/** {@link TmPageContextSnapshot.visible}: what the page's grids and lists display. */
export interface TmVisibleSnapshot {
  /** The columns' paths, in display order. */
  readonly columns: readonly string[];
  /** How many rows the context carries. */
  readonly rowCount: number;
  /** The total count of rows, when known. */
  readonly total: number | null;
  /** The rows, with page data only. */
  readonly rows?: readonly TmVisibleRow[];
}

/** {@link TmPageContextSnapshot.form}: the form being edited. */
export interface TmFormSnapshot {
  /** Whether the form holds unsaved changes. */
  readonly dirty: boolean;
  /** The form's fields, in display order. */
  readonly fields: readonly TmFieldSnapshot[];
}

/** One field of a {@link TmFormSnapshot}. */
export interface TmFieldSnapshot {
  /** The field's path. */
  readonly path: string;
  /** Whether the user may edit the field now. */
  readonly editable: boolean;
  /** How many errors the field shows. */
  readonly errorCount: number;
  /** The field's value, with page data only. */
  readonly value?: TmJson;
}

/** One problem of a {@link TmPageContextSnapshot}. */
export interface TmErrorSnapshot {
  /** Where the problem comes from. */
  readonly source: string;
  /** The problem's code. */
  readonly code: string;
  /** The path of the field it is about. */
  readonly path?: string;
  /** The trace id of the request that failed. */
  readonly traceId?: string;
  /** The problem's message, with page data only. */
  readonly message?: string;
}

/** One action of a {@link TmPageContextSnapshot}. */
export interface TmActionSnapshot {
  /** The action's id. */
  readonly id: string;
  /** Whether it may run now. */
  readonly enabled: boolean;
  /** Whether it destroys something. */
  readonly destructive: boolean;
  /** Where views show it: `primary` or `menu`. */
  readonly group: string;
  /** Whether it takes arguments. */
  readonly hasParams: boolean;
}

/** The options of {@link tmSerializePageContext}. */
export interface TmPageContextSnapshotOptions {
  /** Whether the snapshot carries page data: the rows, the form's values, the errors' messages and the user's text. */
  readonly includeData: boolean;
  /** The injector that resolves the entities' labels; by default, the injection context's. */
  readonly injector?: Injector;
}

/** Whether a JSON value is an object. */
function isObject(value: TmJson | undefined): value is TmJsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** A value with every string in it, at any depth, masked. */
function maskStrings(value: TmJson): TmJson {
  if (typeof value === 'string') {
    return MASK;
  }
  if (Array.isArray(value)) {
    return value.map(maskStrings);
  }
  return isObject(value) ? Object.fromEntries(Object.entries(value).map(([key, held]) => [key, maskStrings(held)])) : value;
}

/** A state with the user's text masked: the query's text and filters, and the parameters. */
function maskState(state: TmJson): TmJson {
  if (!isObject(state)) {
    return state;
  }
  const masked: Record<string, TmJson> = { ...state };
  const query = state['query'];
  if (isObject(query)) {
    const text: Record<string, TmJson> = { ...query };
    for (const key of QUERY_TEXT) {
      if (key in text) {
        text[key] = maskStrings(text[key]!);
      }
    }
    masked['query'] = text;
  }
  if ('parameters' in masked) {
    masked['parameters'] = maskStrings(masked['parameters']!);
  }
  return masked;
}

/**
 * Reads a page context once, every signal, into plain JSON: the one form in
 * which the support dump and a future assistant carry it.
 *
 * Without page data, the snapshot leaves out the rows, the form's values and
 * the errors' messages, and masks the user's text: every string in the
 * search query's `q`, `filter`, `columnFilters` and `custom`, and in a
 * report's `parameters`, becomes `"…"`, and a details page's title, which
 * is a record's display name, becomes the entity's singular label.
 *
 * Call it in an injection context, or pass `injector`.
 *
 * @param context - The context, such as `TmPageContexts.current()`.
 * @param options - Whether to carry page data, and the injector outside an injection context.
 * @returns The snapshot.
 *
 * @example
 * ```ts
 * const current = inject(TmPageContexts).current();
 * const snapshot = current && tmSerializePageContext(current, { includeData: false, injector });
 * ```
 */
export function tmSerializePageContext(context: TmPageContext, options: TmPageContextSnapshotOptions): TmPageContextSnapshot {
  const { includeData } = options;
  const injector = options.injector ?? inject(Injector);
  const { kind, entity, helpKey, form } = context;
  const visible = context.visible?.();

  // --- The title: a details page's names a record, so without page data it names the entity ---
  const title =
    includeData || kind !== 'details' ? context.title() : entity === undefined ? MASK : injector.get(TmEntities).label(entity)();

  return {
    kind,
    routeTemplate: context.routeTemplate,
    overlay: context.overlay ?? false,
    ...(entity !== undefined && { entity }),
    title,
    ...(helpKey !== undefined && { helpKey }),
    status: context.status(),
    state: includeData ? context.state() : maskState(context.state()),
    ...(visible !== undefined && {
      visible: {
        columns: visible.columns.map((column) => column.path),
        rowCount: visible.rows.length,
        total: visible.total,
        ...(includeData && { rows: visible.rows }),
      },
    }),
    ...(form !== undefined && {
      form: {
        dirty: form.dirty(),
        fields: form.fields().map((field) => ({
          path: field.path,
          editable: field.editable,
          errorCount: field.errors.length,
          ...(includeData && { value: field.value }),
        })),
      },
    }),
    errors: context.errors().map((error) => ({
      source: error.source,
      code: error.code,
      ...(error.path !== undefined && { path: error.path }),
      ...(error.traceId !== undefined && { traceId: error.traceId }),
      ...(includeData && { message: error.message }),
    })),
    actions: context.actions().map((action) => ({
      id: action.id,
      enabled: action.enabled(),
      destructive: action.destructive,
      group: action.group,
      hasParams: action.params !== undefined,
    })),
  };
}
