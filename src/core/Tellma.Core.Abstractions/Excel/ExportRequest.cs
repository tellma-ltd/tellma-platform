// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>
    ///     The body of a display-shape <c>export</c>: the rows, chosen as <see cref="ExportSourceRequest" />
    ///     chooses them, and the columns the grid shows, in order.
    /// </summary>
    public sealed class ExportRequest : ExportSourceRequest
    {
        /// <summary>The most columns an export may carry.</summary>
        public const int MaxColumns = 100;

        /// <summary>The columns, one sheet column each, in order; from 1 to <see cref="MaxColumns" />.</summary>
        public IReadOnlyList<ExportColumn> Columns { get; set; } = [];
    }
}
