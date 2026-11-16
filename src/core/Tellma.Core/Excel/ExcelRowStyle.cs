// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Excel
{
    /// <summary>How the table writer styles one data row of a report.</summary>
    public enum ExcelRowStyle
    {
        /// <summary>Regular.</summary>
        Normal,

        /// <summary>Italic: a pivot member's phantom child.</summary>
        Phantom,

        /// <summary>Bold on the lightest fill: a first-level subtotal.</summary>
        Subtotal1,

        /// <summary>Bold: a second-level subtotal.</summary>
        Subtotal2,

        /// <summary>Regular: subtotals at depth 3 and deeper.</summary>
        Subtotal3,

        /// <summary>Bold with a double top border: the grand total.</summary>
        Total,
    }
}
