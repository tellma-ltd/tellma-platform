// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * A fit, normalized to the image being fitted (all values `0..1`).
 *
 * `rect` is the exact crop window (its aspect equals the box aspect) for
 * lossless re-editing; `focal` is the rect center, which survives a future
 * change of the box aspect — the DAM-industry convention for automatic
 * recropping.
 */
export interface TmImageFit {
  /** The crop window, as fractions of the source image. */
  readonly rect: {
    readonly x: number;
    readonly y: number;
    readonly width: number;
    readonly height: number;
  };
  /** The rect center — the point to preserve under future aspect changes. */
  readonly focal: { readonly x: number; readonly y: number };
}

/**
 * One committed edit of a `tm-image`, for the consumer to send to the
 * server (which standardizes format, crops to `rect`, and generates
 * renditions).
 *
 * `blob` carries a NEWLY PICKED file only. A re-fit of an existing image
 * emits `blob: null` — what the component holds for display is a rendition
 * (already cropped/downscaled), so re-emitting it would silently downgrade
 * the stored original; the server re-crops from the original it holds and
 * the bytes never round-trip.
 */
export interface TmImageEdit {
  /** The newly picked file's bytes, or `null` for a re-fit. */
  readonly blob: Blob | null;
  /** The fit, normalized against the image being fitted. */
  readonly fit: TmImageFit;
}
