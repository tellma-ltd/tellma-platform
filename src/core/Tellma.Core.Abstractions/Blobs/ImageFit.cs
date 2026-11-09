// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>How an image processor fits a normalised image into its maximum dimension.</summary>
    public enum ImageFit
    {
        /// <summary>
        ///     Fit within a square of the maximum dimension, keeping the aspect ratio and never
        ///     upscaling.
        /// </summary>
        Contain,

        /// <summary>
        ///     Centre-crop to a square on the shorter side, then scale it down to the maximum
        ///     dimension; a square smaller than the maximum is kept at its size, never upscaled.
        /// </summary>
        CoverSquare,
    }
}
