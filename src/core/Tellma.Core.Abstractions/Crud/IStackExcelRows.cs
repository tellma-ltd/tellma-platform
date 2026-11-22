// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     A stack's service as the platform's Excel operations reach it without knowing its key type:
    ///     the row source an export streams from, under the caller's read filter.
    /// </summary>
    internal interface IStackExcelRows
    {
        /// <summary>The stack's descriptor.</summary>
        StackDescriptor Descriptor { get; }

        /// <summary>The row source the Excel codec streams from, bound to one source; it runs nothing until read.</summary>
        /// <param name="source">The rows to read.</param>
        /// <returns>The row source.</returns>
        IExcelRowSource ExcelRowSource(ExportSource source);
    }
}
