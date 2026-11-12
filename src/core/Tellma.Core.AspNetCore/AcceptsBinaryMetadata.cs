// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore
{
    /// <summary>
    ///     Marks an endpoint that accepts a body of any declared media type up to a size, where every
    ///     other cookie-authenticated endpoint accepts JSON alone: the blob upload.
    /// </summary>
    /// <param name="MaxBytes">The largest body the endpoint accepts, in bytes.</param>
    public sealed record AcceptsBinaryMetadata(long MaxBytes)
    {
        /// <summary>The largest body the endpoint accepts, in bytes.</summary>
        public long MaxBytes { get; init; } = MaxBytes > 0
            ? MaxBytes
            : throw new ArgumentOutOfRangeException(nameof(MaxBytes), MaxBytes, "A binary body limit is positive.");
    }
}
