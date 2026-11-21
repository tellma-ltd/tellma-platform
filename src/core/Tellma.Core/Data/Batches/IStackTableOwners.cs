// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Batches
{
    /// <summary>
    ///     Which stack owns a table, for the write-authority check a caller statement passes at
    ///     append. The stack registry's composition replaces the default, under which no stack owns
    ///     any table.
    /// </summary>
    internal interface IStackTableOwners
    {
        /// <summary>The name of the stack that owns a table, root or child.</summary>
        /// <param name="table">The table.</param>
        /// <returns>The owning stack's name, or null when no stack owns it.</returns>
        string? OwnerOf(TableName table);
    }

    /// <summary>The default: no stack owns any table, so every caller write passes.</summary>
    internal sealed class NoStackTableOwners : IStackTableOwners
    {
        /// <inheritdoc />
        public string? OwnerOf(TableName table)
        {
            ArgumentNullException.ThrowIfNull(table);
            return null;
        }
    }
}
