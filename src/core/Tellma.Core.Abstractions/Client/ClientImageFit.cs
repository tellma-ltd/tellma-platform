// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     How an image kind fits an image into its largest dimension.
    ///     Each member travels as the name its <see cref="JsonStringEnumMemberNameAttribute" /> declares.
    /// </summary>
    public enum ClientImageFit
    {
        /// <summary>Scaled down to fit, keeping its aspect ratio.</summary>
        [JsonStringEnumMemberName("contain")]
        Contain,

        /// <summary>Cropped to a centred square, then scaled; the client crops before uploading.</summary>
        [JsonStringEnumMemberName("cover-square")]
        CoverSquare,
    }
}
