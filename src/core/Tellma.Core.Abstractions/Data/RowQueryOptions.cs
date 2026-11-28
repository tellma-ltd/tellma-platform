// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>What a <see cref="IDataBatch.Rows" /> statement returns besides its page of rows.</summary>
    public sealed class RowQueryOptions
    {
        /// <summary>
        ///     When set, the page also carries the grand total, counted up to this cap: a total of
        ///     cap + 1 means "more than the cap".
        /// </summary>
        public int? CountCap { get; set; }

        /// <summary>For a tree's roots only: also return the ancestor rows of the page's rows.</summary>
        public bool IncludeAncestors { get; set; }

        /// <summary>
        ///     The access filter alone, applied to the ancestor rows; null when the caller's access is
        ///     unrestricted.
        /// </summary>
        public FilterTree? AncestorsFilter { get; set; }

        /// <summary>
        ///     Also write the page's ids into the statement's batch-local keys table, for later
        ///     statements to restrict on.
        /// </summary>
        public bool CaptureKeys { get; set; }
    }
}
