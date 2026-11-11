// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Batches
{
    /// <summary>
    ///     Where the bump epilogue finds the users a write affected: a batch-local table of user ids
    ///     for the tables a statement wrote.
    /// </summary>
    /// <param name="Tables">The tables the statement wrote.</param>
    /// <param name="Column">
    ///     The user-id column the ids came from, or null when they apply to every user-level tag rule
    ///     of <paramref name="Tables" /> (a raw statement's declared ids).
    /// </param>
    /// <param name="Identifier">The batch-local table or table-valued parameter holding the ids.</param>
    internal sealed record UserIdSource(IReadOnlySet<TableName> Tables, string? Column, SqlIdentifier Identifier);
}
