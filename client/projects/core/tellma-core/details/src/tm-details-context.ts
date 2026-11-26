// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type DestroyRef, inject, InjectionToken, type Signal } from '@angular/core';
import type { FieldTree } from '@angular/forms/signals';
import type {
  TmApiError,
  TmEntityDescriptor,
  TmId,
  TmJson,
  TmJsonObject,
  TmPageAction,
  TmVisibleData,
} from '@tellma/core';

import type { TmDetailsAbilities, TmDetailsSource } from './tm-details-source';

// The one contract a layout, a hatch, a panel and a layout test see (spec
// 0031 §4.1): the record's state, its form, its abilities and its commands,
// and the registrations the kit makes. The host provides one per record, so
// a re-keyed record brings a fresh form, fresh extras and no stale
// registrations.

/** The related rows a read returned, by entity and id. */
export interface TmRelated {
  /**
   * A related row.
   *
   * @param entity - The row's entity, such as `acme.Currency`.
   * @param id - The row's id.
   * @returns The row, or `undefined` when the read returned none.
   */
  get(entity: string, id: TmId | null | undefined): TmJsonObject | undefined;
}

/** A rendered field, as the kit registers it. */
export interface TmDetailsFieldRef {
  /** The label's key. */
  readonly labelKey: string;
  /** The label, in the active language. */
  readonly label: Signal<string>;
  /** Whether the field takes input in edit mode. */
  readonly editable: Signal<boolean>;
  /** Focuses the control: opens its tab, expands its card, then focuses it. */
  focus(): void;
}

/** A rendered child collection, as the kit registers it. */
export interface TmDetailsCollectionRef {
  /** Whether its deferred grid has rendered its rows. */
  readonly ready: Signal<boolean>;
  /** Its columns and rows as displayed. */
  readonly visible: Signal<TmVisibleData>;
  /**
   * Scrolls a row into view, and focuses its cell with `column`.
   *
   * @param rowId - The row's id.
   * @param options - Whether to flash the row, and the column to focus.
   * @returns Whether the row was found.
   */
  reveal(rowId: TmId, options?: { readonly flash?: boolean; readonly column?: string }): boolean;
}

/** The record's mode: view, edit of an existing record, or create of a new one. */
export type TmDetailsMode = 'view' | 'edit' | 'create';

/** The read's state: loading, ready, or failed. */
export type TmDetailsStatus = 'loading' | 'ready' | 'error';

/**
 * A details page's record as its layout sees it: its state, its form, its
 * abilities and its commands. `injectTmDetails()` returns it inside the
 * page's layout or hatch.
 *
 * @typeParam T - The record's type, such as `TmEntityType<'acme.Customer'>`.
 */
export interface TmDetailsContext<T = TmJsonObject> {
  /** The entity's descriptor. */
  readonly descriptor: TmEntityDescriptor;
  /** The page's source. */
  readonly source: TmDetailsSource<T>;
  /** The record's id; `null` in create mode. */
  readonly id: Signal<TmId | null>;
  /** The record's mode. */
  readonly mode: Signal<TmDetailsMode>;
  /** The read's state. */
  readonly status: Signal<TmDetailsStatus>;
  /** The read's failure, while `status` is `error`. */
  readonly error: Signal<TmApiError | null>;
  /** The server copy: the record as the last read or save returned it; `undefined` until it lands, and in create mode. */
  readonly entity: Signal<T | undefined>;
  /** The working copy, which the form edits: the server copy itself in view mode, `{}` until the read lands. */
  readonly model: Signal<T>;
  /** The Signal Forms tree over the working copy. */
  readonly form: FieldTree<T>;
  /** Whether the working copy differs from the base: an edit wrote to it. */
  readonly dirty: Signal<boolean>;
  /** Whether a write is in flight. */
  readonly busy: Signal<boolean>;
  /** The related rows the last read or save returned. */
  readonly related: TmRelated;
  /** The extras the last read or save returned, by name. */
  readonly extras: Signal<Readonly<Record<string, TmJson>>>;
  /** Whether the user may edit, create, delete and activate this record: permissions, the tenant's state and the record's refusals. */
  readonly can: TmDetailsAbilities;
  /** The abilities the `permissions` extra refuses on this record, with their reasons; empty without it. */
  readonly refused: Signal<Readonly<Record<string, string>>>;
  /** The page's actions, for a hatch's own toolbar. */
  readonly actions: Signal<readonly TmPageAction[]>;
  /** The child row a link asked to show, or `null`. */
  readonly childTarget: Signal<{ readonly collection: string; readonly id: TmId } | null>;
  /** Enters edit mode: no copy and no request; focus goes to the first editable field. */
  edit(): void;
  /**
   * Leaves edit mode, asking first when there are changes; in create mode, leaves the page.
   *
   * @returns `false` when the user kept editing.
   */
  cancel(): Promise<boolean>;
  /**
   * Saves the record.
   *
   * @returns `true` once it is saved and back in view mode.
   */
  save(): Promise<boolean>;
  /**
   * Reads the record again: in view mode the whole record; in edit mode its related rows and extras only.
   *
   * @param options - `quiet` shows no progress bar and no busy state, for a page that follows a job.
   */
  refresh(options?: { readonly quiet?: boolean }): void;
  /**
   * Writes the working copy in code, in edit and create mode; the record turns dirty.
   *
   * @param recipe - Takes the working copy and returns its next value.
   */
  update(recipe: (model: T) => T): void;
  /**
   * Registers a rendered field, until `destroyRef` ends.
   *
   * @param path - The field's path, PascalCase and relative to the record, such as `Name2`.
   * @param field - The field.
   * @param destroyRef - What ends the registration; the caller's injection context's by default.
   */
  registerField(path: string, field: TmDetailsFieldRef, destroyRef?: DestroyRef): void;
  /**
   * Registers a rendered child collection, until `destroyRef` ends.
   *
   * @param name - The collection's name, such as `Lines`.
   * @param collection - The collection.
   * @param destroyRef - What ends the registration; the caller's injection context's by default.
   */
  registerCollection(name: string, collection: TmDetailsCollectionRef, destroyRef?: DestroyRef): void;
}

/** The record's context in a details page's injector. Internal: `injectTmDetails` reads it, and a layout test provides a fake. */
export const TM_DETAILS_CONTEXT = new InjectionToken<TmDetailsContext<unknown>>('TM_DETAILS_CONTEXT');

/**
 * The details page's record, inside its layout, hatch or panel.
 *
 * @typeParam T - The record's type.
 * @returns The context.
 * @throws Error outside a details page.
 *
 * @example
 * ```ts
 * readonly details = injectTmDetails<TmEntityType<'acme.SalesInvoice'>>();
 * readonly total = computed(() => (this.details.model().lines ?? []).length);
 * recalculate(): void {
 *   this.details.update((invoice) => ({ ...invoice, notes: `${this.total()} lines` }));
 * }
 * ```
 */
export function injectTmDetails<T = TmJsonObject>(): TmDetailsContext<T> {
  const context = inject(TM_DETAILS_CONTEXT, { optional: true });
  if (context === null) {
    throw new Error('injectTmDetails() needs a details page: call it in a component its layout or hatch renders.');
  }
  return context as TmDetailsContext<T>;
}
