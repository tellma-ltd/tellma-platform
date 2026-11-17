// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>What an import plan made of one sheet of the workbook.</summary>
    public enum ImportSheetStatus
    {
        /// <summary>The sheet holds the parent rows.</summary>
        Parent,

        /// <summary>The sheet holds the rows of a child collection.</summary>
        Child,

        /// <summary>The sheet is the workbook's hidden manifest.</summary>
        Manifest,

        /// <summary>The sheet matched nothing, or the caller ignored it; it is not read.</summary>
        Ignored,

        /// <summary>The sheet is one of two bound to the same target, which is an error at import.</summary>
        Duplicate,
    }
}
