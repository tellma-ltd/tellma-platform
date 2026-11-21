// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Batches
{
    // The captured key table is spec 0012 §5.2's; the platform's pipeline restricts later statements
    // on it (spec 0015 §5.3) and fills a key table of the stack's own key type (spec 0015 §5.4).
    /// <summary>
    ///     The key tables of a batch that the platform's own pipeline reaches beyond the public batch
    ///     contract: the table a rows or entity query captured its keys into, and a declared key
    ///     table of either key width.
    /// </summary>
    internal interface IBatchKeyTables
    {
        /// <summary>
        ///     The key-capture table a rows or entity query statement filled, for later statements to
        ///     restrict on; null when the statement captured none.
        /// </summary>
        /// <param name="handle">The handle a rows or entity query returned.</param>
        /// <returns>The table, or null.</returns>
        SqlIdentifier? KeysOf(object handle);

        /// <summary>
        ///     Declares an empty table of keys, <c>[Id]</c> its primary key, for later statements to fill
        ///     and to restrict on.
        /// </summary>
        /// <param name="keyType">The keys' type: <see cref="int" /> or <see cref="long" />.</param>
        /// <returns>The table's name.</returns>
        /// <exception cref="ArgumentException"><paramref name="keyType" /> is neither <see cref="int" /> nor <see cref="long" />.</exception>
        SqlIdentifier DeclareKeyTable(Type keyType);
    }
}
