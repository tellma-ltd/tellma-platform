// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmBlobDescriptor } from '../api/tm-api';
import type { TmId, TmJson, TmJsonObject } from '../json/tm-json';

// What the details page keeps between visits (spec 0031 §11.1, §13): the
// draft of a record being edited or created, which `TmDrafts` stores under
// the source's draft key, and the snapshot of a record shown, which
// `TmPageState` holds under the record's page-state key. They are types
// only, here rather than in the details entry point, so the tab's memory
// and its support dump can name what they hold without loading the page.

/**
 * The draft of a record being edited or created on a details page: the
 * working copy, the stamp its save sends, and the uploads and tabs it was
 * left with. A draft whose write contract differs from the running
 * build's is reconciled rather than resumed as it is.
 *
 * @example
 * ```ts
 * const draft: TmDetailsDraft = { v: 1, mode: 'edit', contract: 'f1', model: { id: 77, name: 'Acme' }, baseModifiedAt: '2026-11-01T10:00:00.0000000+00:00' };
 * ```
 */
export interface TmDetailsDraft {
  /** The draft format's version. */
  readonly v: 1;
  /** Whether the draft edits an existing record or creates one. */
  readonly mode: 'edit' | 'create';
  /** The fingerprint of the write route's contract when the draft began; empty when the server sent none. */
  readonly contract: string;
  /** The working copy, as plain JSON. */
  readonly model: TmJson;
  /** The stamp the save sends; `null` when creating. */
  readonly baseModifiedAt: string | null;
  /** The record a create was cloned from. */
  readonly clonedFrom?: TmId;
  /** When a write began, in epoch milliseconds; absent once its answer arrived. */
  readonly savingAt?: number;
  /** The staged uploads the working copy references, for their previews. */
  readonly uploads?: readonly TmBlobDescriptor[];
  /** The selected tab of each tab group, by the group's id. */
  readonly tabs?: Readonly<Record<string, string>>;
}

/**
 * A record as a details page showed it, kept so a return within the app
 * shows it at once and as the user left it: the server's copy with its
 * related rows and extras, the selected tabs, the expanded cards, and the
 * scroll positions.
 *
 * @example
 * ```ts
 * const snapshot: TmDetailsSnapshot = {
 *   entity: { id: 1284 }, related: {}, extras: {}, tabs: { main: 'lines' }, cards: { totals: false },
 *   scroll: { body: 320, aside: 0, panels: {} },
 * };
 * ```
 */
export interface TmDetailsSnapshot {
  /** The record, as the server sent it. */
  readonly entity: TmJsonObject;
  /** Its related rows, by entity name. */
  readonly related: Readonly<Record<string, readonly TmJsonObject[]>>;
  /** Its extras, by name. */
  readonly extras: Readonly<Record<string, TmJson>>;
  /** The selected tab of each tab group, by the group's id. */
  readonly tabs: Readonly<Record<string, string>>;
  /** Whether each card is expanded, by the card's id. */
  readonly cards: Readonly<Record<string, boolean>>;
  /** The scroll positions: the body band's, the aside's, and each scrollable card's by its id. */
  readonly scroll: {
    /** The body band's scroll offset, in pixels. */
    readonly body: number;
    /** The aside's scroll offset, in pixels. */
    readonly aside: number;
    /** Each scrollable card's scroll offset, in pixels, by the card's id. */
    readonly panels: Readonly<Record<string, number>>;
  };
}
