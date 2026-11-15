// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmBadgeTone } from '@tellma/core-ui/badge';
import type { TmRowId } from '@tellma/core-ui/contracts';

/**
 * What one tile corner shows: plain text, or text marked numeric (tabular
 * digits, kept at the physical right in every direction so columns of
 * amounts line up) or shown as a badge of a tone.
 */
export type TmTileText =
  | string
  | {
      /** The corner's text. */
      readonly text: string;
      /** Numeric text: tabular digits, at the physical right in RTL too. */
      readonly numeric?: boolean;
      /** Renders the text as a `tm-badge` of this tone. */
      readonly badge?: TmBadgeTone;
    };

/** A tile's image: a URL (or `null` for the placeholder glyph) and its text alternative. */
export interface TmTileImage {
  /** The image URL; `null` shows the placeholder glyph. */
  readonly src: string | null;
  /** The image's version tag, so a changed image reloads at the same URL. */
  readonly etag?: string | null;
  /** The text alternative; empty for a decorative image. */
  readonly alt: string;
}

/**
 * How a row maps onto a tile: an optional image at inline-start, the title
 * at top-start and three more corners. The readers run once per row, and
 * again for that row when the row, the layout or the data change, or when a
 * signal they read changes (a translated label across a language switch).
 */
export interface TmTileLayout<T> {
  /** The tile's image. Absent: no image column at all; a `null` src shows a placeholder glyph. */
  readonly image?: (row: T) => TmTileImage;
  /** The title at top-start, in the primary weight, ellipsized. Also names the tile's checkbox. */
  readonly title: (row: T) => string;
  /**
   * The title as parts, each isolated in its own `<bdi>` and joined by
   * " - ", so mixed-direction parts (an English code and an Arabic name)
   * keep their own order. `null` falls back to `title`.
   */
  readonly titleParts?: (row: T) => readonly string[] | null;
  /**
   * A URL the title links to: the title renders as `<a href>`, so a
   * modified or middle click opens it the browser's way. `null`: no link.
   */
  readonly href?: (row: T) => string | null;
  /** The top-end corner: an amount, a date, a status. */
  readonly topEnd?: (row: T) => TmTileText | null;
  /** The bottom-start corner, in the secondary ink. */
  readonly bottomStart?: (row: T) => TmTileText | null;
  /** The bottom-end corner. */
  readonly bottomEnd?: (row: T) => TmTileText | null;
}

/** The context a `tmTileContent` template renders a tile's body with. */
export interface TmTileContentContext<T> {
  /** The row the tile shows. */
  readonly $implicit: T;
  /** The row's id. */
  readonly rowId: TmRowId;
}
