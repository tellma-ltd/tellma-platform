// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>What a save wrote, read after the batch executes.</summary>
    /// <param name="Stamp">The stamp every written row carries, with seven fractional digits.</param>
    /// <param name="Inserted">The rows inserted, children included.</param>
    /// <param name="Updated">The rows updated, children included; unchanged rows are not counted.</param>
    /// <param name="Deleted">The child rows deleted because the save no longer supplied them.</param>
    public sealed record SaveReceipt(DateTimeOffset Stamp, int Inserted, int Updated, int Deleted);

    /// <summary>
    ///     A save appended to a batch: its receipt, and the batch-local names it declared for the
    ///     statements composed after it.
    /// </summary>
    /// <param name="Receipt">The receipt, available after the batch executes.</param>
    /// <param name="SavedIds">The ids of every saved root row.</param>
    /// <param name="NewIds">The ids of the inserted root rows.</param>
    /// <param name="TouchedIds">The ids of the root rows the save wrote, itself or through a child.</param>
    /// <param name="Captures">One capture per captured column of every written table.</param>
    public sealed record SaveHandle(
        BatchResult<SaveReceipt> Receipt,
        SqlIdentifier SavedIds,
        SqlIdentifier NewIds,
        SqlIdentifier TouchedIds,
        IReadOnlyList<ColumnCapture> Captures);

    /// <summary>
    ///     A batch-local table holding the old and new values a write statement saw in one column, such
    ///     as a blob reference, for a statement composed after it.
    /// </summary>
    /// <param name="Table">The written table.</param>
    /// <param name="Column">The captured column.</param>
    /// <param name="Identifier">The batch-local table the values are captured into.</param>
    public sealed record ColumnCapture(TableName Table, string Column, SqlIdentifier Identifier);
}
