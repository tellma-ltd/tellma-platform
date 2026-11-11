// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>The result sets a raw statement returned, in order, as many as it declared.</summary>
    /// <param name="ResultSets">The result sets; columns are bound by name, never by position.</param>
    public sealed record RawResult(IReadOnlyList<QueryRowSet> ResultSets)
    {
        /// <summary>The result of a statement that returns no result set.</summary>
        public static RawResult Empty { get; } = new([]);
    }
}
