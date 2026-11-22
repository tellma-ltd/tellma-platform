// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmEntitiesChunk, TmEntitySummary } from '@tellma/core';

import chunk from '../fixtures/entity-model/entities-chunk.json';
import summaries from '../fixtures/entity-model/core-entities.json';

// The server's own client entity model for its Acme-like test model: the
// Entities chunk and the Core chunk's summaries the emit writes, recorded by
// the server's suite. A test of the entity registry, the display names or
// the permissions reads the same shapes the server sends, twins, blobs,
// child entities and actions included.

/** The recorded entity model: the Core chunk's summaries and the Entities chunk. */
export interface TmTestEntities {
  /** The summaries, as the Core chunk's `entities` carries them. */
  readonly summaries: readonly TmEntitySummary[];
  /** The Entities chunk. */
  readonly chunk: TmEntitiesChunk;
}

/**
 * The server's recorded entity model, a fresh copy on every call so a test
 * may patch it.
 *
 * @returns The summaries and the Entities chunk.
 *
 * @example
 * ```ts
 * const { summaries, chunk } = tmTestEntities();
 * const metadata = tmTestMetadata({ entities: summaries });
 * ```
 */
export function tmTestEntities(): TmTestEntities {
  return structuredClone({
    summaries: summaries as unknown as readonly TmEntitySummary[],
    chunk: chunk as unknown as TmEntitiesChunk,
  });
}
