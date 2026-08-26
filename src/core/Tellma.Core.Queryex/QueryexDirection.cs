// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>An ordering direction suffix.</summary>
    public enum QueryexDirection
    {
        /// <summary>No suffix was written. Ordered ascending.</summary>
        None,

        /// <summary>The <c>asc</c> suffix.</summary>
        Ascending,

        /// <summary>The <c>desc</c> suffix.</summary>
        Descending,
    }
}
