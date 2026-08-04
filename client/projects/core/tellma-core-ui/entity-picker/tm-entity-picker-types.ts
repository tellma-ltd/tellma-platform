// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Type } from '@angular/core';

/** The shape of a foreign-key id the picker can hold. */
export type TmEntityId = string | number;

/**
 * A search result set. The bare-array form is the common case; the object
 * form additionally flags a server-truncated set (`hasMore`), which renders
 * the picker's truncation hint and switches the results announcement to its
 * open-ended variant ("10+ results").
 */
export type TmEntitySearchResult<T> =
  | readonly T[]
  | { readonly items: readonly T[]; readonly hasMore?: boolean };

/**
 * The consumer's search facility. Called with the query text and an
 * `AbortSignal` that fires when the request is superseded, the dropdown
 * closes, or the picker is destroyed — unless a host has taken the request
 * over (a grid cell does, so the answer can outlive the editor), in which
 * case the host aborts it instead and the guarantee is unchanged: the
 * signal fires when nobody is left to read the answer. May return the
 * results synchronously (in-memory/cache source — renders instantly, no
 * spinner) or as a Promise.
 * The implementation is expected to impose a result limit; the picker
 * renders what it gets, in order, without filtering or re-ranking.
 */
export type TmEntitySearchFn<T> = (
  query: string,
  signal: AbortSignal,
) => TmEntitySearchResult<T> | Promise<TmEntitySearchResult<T>>;

/**
 * A selection returned by a modal page (advanced search, create, or edit)
 * through its `TmModalRef` result.
 */
export interface TmEntityPick<Id extends TmEntityId = TmEntityId, T = unknown> {
  /** The picked entity's id — becomes the picker's committed value. */
  readonly id: Id;
  /**
   * The display text at pick time. Seeds the picker's label memo; a
   * configured `displayWith` wins over it wherever it resolves.
   */
  readonly label: string;
  /**
   * The full entity, when the page has it — relayed through the picker's
   * `picked` output so consumers can warm their entity caches.
   */
  readonly item?: T;
}

/**
 * A consumer page the picker launches in a modal: the component class alone,
 * or a config object adding a size bucket and a title (defaults: size `lg`
 * for advanced search, `md` for create and edit; localized default titles).
 */
export type TmEntityPickerPage =
  | Type<unknown>
  | {
      /** The page component the modal instantiates. */
      component: Type<unknown>;
      /** The modal size bucket; each page kind has its own default. */
      size?: 'sm' | 'md' | 'lg';
      /** The modal title; defaults to a localized per-page-kind title. */
      title?: string;
    };

/**
 * The payload a launched page receives via `TM_MODAL_DATA`.
 */
export interface TmEntityPickerPageData<Id extends TmEntityId = TmEntityId> {
  /**
   * The picker's query text at launch — prefills the page's own search
   * field (advanced search) or the new entity's name (create). Empty when
   * the picker's text is pristine (it holds the committed value's display
   * text, which is a browse intent, not a query).
   */
  readonly query: string;
  /** Edit launches only: the committed value being edited. */
  readonly id?: Id;
}

/**
 * The payload of the picker's `picked` output — every committed selection,
 * whatever produced it.
 */
export interface TmEntityPicked<T, Id extends TmEntityId> {
  /** The picked entity's id — the committed value. */
  readonly id: Id;
  /** The display text the pick committed. */
  readonly label: string;
  /**
   * The search result for `'list'`/`'auto'` sources; the page-returned
   * entity for modal sources; absent when neither had the full item.
   */
  readonly item?: T;
  /**
   * What produced the pick: a dropdown row (`'list'`), the unique-match
   * auto-resolution of typed text (`'auto'`), or one of the modal pages.
   */
  readonly source: 'list' | 'auto' | 'advanced' | 'create' | 'edit';
}

/**
 * A search handed from a picker to its host at commit time — the grid's
 * cell-editor seam.
 *
 * The request it describes has been DETACHED from the picker: nothing the
 * picker does afterwards (a new search, the popup closing, its own
 * destruction) will abort it, because the host needs the answer to outlive
 * the editor. `abort` is therefore the only remaining handle, and a host
 * that drops it leaks the request.
 * @internal
 */
export interface ɵTmEntityAdoptedSearch<T> {
  /**
   * The result set, or `'failed'` when the search threw or rejected —
   * which an ABORT also surfaces as, since the two are indistinguishable
   * from here. `hasMore` is carried because a truncated page can be
   * trusted about what it contains and never about what it does not.
   */
  readonly settled: Promise<{ readonly items: readonly T[]; readonly hasMore: boolean } | 'failed'>;
  /** Cancels the request; the only handle now that the picker has let go. */
  readonly abort: () => void;
}
