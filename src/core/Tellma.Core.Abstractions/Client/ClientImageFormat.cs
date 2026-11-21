// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     The format an image kind re-encodes images to.
    ///     Each member travels as the name its <see cref="JsonStringEnumMemberNameAttribute" /> declares.
    /// </summary>
    public enum ClientImageFormat
    {
        /// <summary>WebP.</summary>
        [JsonStringEnumMemberName("webp")]
        WebP,

        /// <summary>JPEG.</summary>
        [JsonStringEnumMemberName("jpeg")]
        Jpeg,

        /// <summary>PNG.</summary>
        [JsonStringEnumMemberName("png")]
        Png,
    }
}
