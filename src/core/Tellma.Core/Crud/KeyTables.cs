// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Crud
{
    /// <summary>The key tables the pipeline's reads restrict on: bound key lists, and the batch's own key tables.</summary>
    internal static class KeyTables
    {
        /// <summary>Binds a stack's keys as a table-valued parameter of the key's width.</summary>
        /// <typeparam name="TKey">The key type: <see cref="int" /> or <see cref="long" />.</typeparam>
        /// <param name="batch">The batch.</param>
        /// <param name="keys">The keys, distinct.</param>
        /// <returns>The parameter's name.</returns>
        /// <exception cref="NotSupportedException">The key type is neither <see cref="int" /> nor <see cref="long" />.</exception>
        public static SqlIdentifier Bind<TKey>(IDataBatch batch, IReadOnlyList<TKey> keys)
            where TKey : struct
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(keys);
            return keys switch
            {
                IReadOnlyList<int> ints => batch.Tvp([.. ints.Select(static id => new IdList { Id = id })]),
                IReadOnlyList<long> longs => batch.Tvp([.. longs.Select(static id => new BigIdList { Id = id })]),
                _ => throw new NotSupportedException($"A stack's key type is int or long; '{typeof(TKey).Name}' is neither."),
            };
        }

        /// <summary>The batch's own key tables, which the platform's batches carry.</summary>
        /// <param name="batch">The batch.</param>
        /// <returns>The key tables.</returns>
        /// <exception cref="InvalidOperationException">The batch is not one of the platform's.</exception>
        public static IBatchKeyTables Of(IDataBatch batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            return batch as IBatchKeyTables
                ?? throw new InvalidOperationException(
                    $"The batch, a {batch.GetType().Name}, exposes no key tables; the pipeline composes on the platform's own batches.");
        }
    }
}
