// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Crud
{
    // Spec 0015 §13.3, bypass resistance at run time: a statement appended through IDataBatch's
    // public members whose declared writes include a table a stack owns is refused unless the
    // appender holds that stack's authority. The owners are the registry's: every root and child
    // table of every stack, a read-only stack's included.
    /// <summary>Which stack owns a table, read from the stack registry. Singleton.</summary>
    /// <param name="registry">The stack registry.</param>
    internal sealed class StackTableOwners(StackRegistry registry) : IStackTableOwners
    {
        /// <inheritdoc />
        public string? OwnerOf(TableName table)
        {
            ArgumentNullException.ThrowIfNull(table);
            return registry.OwnerOf(table)?.Resource;
        }
    }
}
