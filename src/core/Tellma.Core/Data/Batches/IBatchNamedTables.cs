// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Batches
{
    // Spec 0015 §11.2: an action's method composes before its persist batch exists, so the deferred
    // recording view answers a batch-local table's name itself, from the once-per-batch namespace no
    // other statement draws on, and the replay declares the table under that name. The name is then
    // the same on every recomposed batch, wherever in the batch the recording lands.
    /// <summary>
    ///     The batch-local tables a batch declares under a name its caller chose, for the platform's
    ///     deferred recording view: a table-valued parameter, and an empty key table.
    /// </summary>
    internal interface IBatchNamedTables
    {
        /// <summary>Binds rows of a table type as a table-valued parameter under a given name, for later statements to name.</summary>
        /// <typeparam name="TRow">The row type: a standalone table type or an entity with a table type.</typeparam>
        /// <param name="rows">The rows.</param>
        /// <param name="name">The name: a variable of the once-per-batch namespace the batch has not declared.</param>
        /// <returns>The parameter's name.</returns>
        /// <exception cref="ArgumentException">The name is no variable of the once-per-batch namespace.</exception>
        /// <exception cref="InvalidOperationException">The batch already declared the name, or the row type has no table type.</exception>
        SqlIdentifier Tvp<TRow>(IReadOnlyList<TRow> rows, string name)
            where TRow : class;

        /// <summary>Declares an empty table of keys, <c>[Id]</c> its primary key, under a given name.</summary>
        /// <param name="keyType">The keys' type: <see cref="int" /> or <see cref="long" />.</param>
        /// <param name="name">The name: a variable of the once-per-batch namespace the batch has not declared.</param>
        /// <returns>The table's name.</returns>
        /// <exception cref="ArgumentException">The key type is neither int nor long, or the name is no variable of the once-per-batch namespace.</exception>
        /// <exception cref="InvalidOperationException">The batch already declared the name.</exception>
        SqlIdentifier DeclareKeyTable(Type keyType, string name);
    }
}
