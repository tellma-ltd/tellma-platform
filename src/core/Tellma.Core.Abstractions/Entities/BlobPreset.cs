// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     The <see cref="BlobKindPolicy" /> a blob kind starts from; see
    ///     <see cref="BlobKindPolicy.For" /> for each preset's policy.
    /// </summary>
    public enum BlobPreset
    {
        /// <summary>Documents, images, text and archives (<see cref="BlobKindPolicy.Attachment" />).</summary>
        Attachment,

        /// <summary>A profile picture, cropped square (<see cref="BlobKindPolicy.Avatar" />).</summary>
        Avatar,

        /// <summary>A photograph, fitted (<see cref="BlobKindPolicy.Photo" />).</summary>
        Photo,

        /// <summary>A transparent signature image (<see cref="BlobKindPolicy.Signature" />).</summary>
        Signature,
    }
}
