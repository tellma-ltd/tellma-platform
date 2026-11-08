// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     The encodings an image processor writes. <see cref="BlobContentTypes.ForImageFormat" />
    ///     gives each one's media type.
    /// </summary>
    public enum ImageFormat
    {
        /// <summary>WebP (<c>image/webp</c>); alpha preserved.</summary>
        WebP,

        /// <summary>JPEG (<c>image/jpeg</c>); no alpha channel.</summary>
        Jpeg,

        /// <summary>PNG (<c>image/png</c>); alpha preserved.</summary>
        Png,
    }
}
