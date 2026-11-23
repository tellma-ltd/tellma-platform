// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Reports
{
    /// <summary>What a report shows, which names the one block its document holds.</summary>
    public enum ReportKind
    {
        /// <summary>Row-level columns of the root.</summary>
        List,

        /// <summary>Grouping keys and measures, one row per group.</summary>
        Summary,

        /// <summary>Row and column dimensions with measures in the cells.</summary>
        Pivot,

        /// <summary>A chart of one measure over its dimensions.</summary>
        Chart,
    }
}
