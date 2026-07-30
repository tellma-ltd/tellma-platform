// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmImageFit } from '../tm-image-types';

/**
 * The centered cover fit: the largest box-aspect rect that fits inside the
 * image — the state a fresh fitting session starts from.
 */
export function tmCoverFit(
  naturalWidth: number,
  naturalHeight: number,
  boxWidth: number,
  boxHeight: number,
): TmImageFit {
  const aspect = boxWidth / boxHeight;
  const width = Math.min(naturalWidth, naturalHeight * aspect);
  const height = width / aspect;
  const x = (naturalWidth - width) / 2;
  const y = (naturalHeight - height) / 2;
  return {
    rect: {
      x: x / naturalWidth,
      y: y / naturalHeight,
      width: width / naturalWidth,
      height: height / naturalHeight,
    },
    focal: { x: 0.5, y: 0.5 },
  };
}
