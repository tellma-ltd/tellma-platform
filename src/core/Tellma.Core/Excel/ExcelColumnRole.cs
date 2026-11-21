// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Excel
{
    /// <summary>What a column holds, as the workbook's manifest records it for the importer.</summary>
    public enum ExcelColumnRole
    {
        /// <summary>The row's id: its identity on a same-source file and the link its child rows name.</summary>
        Id,

        /// <summary>The row's concurrency stamp, a hidden text column.</summary>
        Stamp,

        /// <summary>A scalar property of the row.</summary>
        Property,

        /// <summary>A foreign key, expressed in a key of its target.</summary>
        Reference,

        /// <summary>A child row's link to its owner row, by the owner's id within the workbook.</summary>
        ParentReference,
    }
}
