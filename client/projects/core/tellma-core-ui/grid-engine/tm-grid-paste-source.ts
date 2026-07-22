// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmRowId } from '@tellma/core-ui/contracts';

import { tmParseTsv, type TmGridClipboardMeta } from './tm-grid-clipboard-serialize';

/**
 * What every clipboard payload reduces to before the engine shapes a paste:
 * a rectangular display-string matrix plus whatever richer information the
 * source carried. The reduction itself happens outside the engine (HTML
 * payloads need a DOM parser); the engine consumes only this.
 */
export interface TmGridPasteSource {
  /** The display strings, rectangular. */
  readonly matrix: ReadonlyArray<readonly string[]>;
  /** Parsed grid metadata, when the payload carried it. */
  readonly meta?: TmGridClipboardMeta;
  /**
   * Per-cell raw values aligned with `matrix`; an `undefined` slot means
   * "no raw value" (distinct from a legitimate `null` value).
   */
  readonly rawValues?: ReadonlyArray<ReadonlyArray<{ readonly value: unknown } | undefined>>;
  /** Per-row identities of a full-row copy (drives same-grid row moves). */
  readonly rowIds?: readonly TmRowId[];
  /**
   * Whether the first matrix row is a header row: `true`/`false` when the
   * metadata decided, `undefined` to let the engine run its content
   * heuristic against the target columns.
   */
  readonly hasHeaderRow?: boolean;
}

/**
 * A copy's full in-memory descriptor — the same-session fast path that
 * survives browsers stripping custom attributes from the HTML flavor. Keyed
 * by the text flavor's fingerprint; a paste uses it only when the actual
 * clipboard payload's fingerprint matches.
 */
export interface TmGridCopyDescriptor {
  /** Fingerprint of the `text/plain` flavor this descriptor belongs to. */
  readonly fingerprint: string;
  /** The copied metadata. */
  readonly meta: TmGridClipboardMeta;
  /** The copied display strings. */
  readonly matrix: ReadonlyArray<readonly string[]>;
  /** The copied raw values, aligned with `matrix`. */
  readonly rawValues: ReadonlyArray<ReadonlyArray<{ readonly value: unknown } | undefined>>;
  /** The copied rows' identities, for full-row copies. */
  readonly rowIds?: readonly TmRowId[];
}

/** Reduces plain TSV text to a paste source (the ladder's last rung). */
export function tmPasteSourceFromTsv(text: string): TmGridPasteSource {
  return { matrix: tmParseTsv(text) };
}

/** Reduces an in-memory copy descriptor to a paste source (the fast path). */
export function tmPasteSourceFromDescriptor(descriptor: TmGridCopyDescriptor): TmGridPasteSource {
  return {
    matrix: descriptor.matrix,
    meta: descriptor.meta,
    rawValues: descriptor.rawValues,
    rowIds: descriptor.rowIds,
    hasHeaderRow: descriptor.meta.headers === true,
  };
}
