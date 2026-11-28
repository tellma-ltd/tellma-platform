// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     What a caller may do with one securable: nothing, the rows a filter admits, or every row.
    /// </summary>
    /// <remarks>
    ///     The default value is <see cref="Denied" />, so an outcome nobody set grants nothing.
    /// </remarks>
    public enum AccessOutcome
    {
        /// <summary>No grant reaches the securable.</summary>
        Denied = 0,

        /// <summary>Grants reach the securable for the rows a filter admits.</summary>
        Filtered = 1,

        /// <summary>A grant reaches the securable for every row.</summary>
        Unrestricted = 2,
    }
}
