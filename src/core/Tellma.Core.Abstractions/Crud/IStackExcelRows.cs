// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     A stack's service as the platform's Excel operations reach it without knowing its key type:
    ///     the row source an export streams from and an import's lookups read, under the caller's read
    ///     filter, and the details read and save an import hydrates and writes through.
    /// </summary>
    internal interface IStackExcelRows
    {
        /// <summary>The stack's descriptor.</summary>
        StackDescriptor Descriptor { get; }

        /// <summary>The row source the Excel codec streams from, bound to one source; it runs nothing until read.</summary>
        /// <param name="source">The rows to read.</param>
        /// <returns>The row source.</returns>
        IExcelRowSource ExcelRowSource(ExportSource source);

        /// <summary>
        ///     Reads rows in details shape by id, each with its child collections at every depth, under
        ///     the caller's read filter: the stored rows an import overlays. An absent or hidden id is
        ///     omitted.
        /// </summary>
        /// <param name="ids">The ids, each a key the stack's key type holds.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The rows, each of the stack's entity type.</returns>
        /// <exception cref="OverflowException">An id is beyond the stack's key type.</exception>
        Task<IReadOnlyList<object>> HydrateAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken);

        /// <summary>Saves an import's rows, inserts and updates together, in one transaction, answering nothing.</summary>
        /// <param name="entities">The rows, each of the stack's entity type.</param>
        /// <param name="options">The save's options.</param>
        /// <param name="cancellationToken">Cancels the save.</param>
        /// <returns>A task that completes when the rows are committed.</returns>
        /// <exception cref="InvalidCastException">A row is not of the stack's entity type.</exception>
        Task SaveImportAsync(IReadOnlyList<object> entities, SaveOptions options, CancellationToken cancellationToken);
    }
}
