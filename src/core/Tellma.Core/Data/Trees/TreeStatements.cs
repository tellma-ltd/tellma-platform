// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Data.Common;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Data.Trees
{
    // The verify statement of spec 0012 §10.4: the whole-table recount (T5b over every row), which
    // repairs every row whose counts drifted. The maintenance job of spec 0020 runs it in one round
    // trip per tree table; a repair is logged and metered, since the statements of §10.2 keep the
    // counts exact and drift means a write went around them.
    /// <summary>
    ///     Appends the tree statements a batch runs on its own: the verify statement that recounts a
    ///     tree table and repairs the rows whose counts drifted.
    /// </summary>
    /// <param name="metrics">The data instruments, which count the recount and its repairs.</param>
    /// <param name="logger">The logger, which warns of a repair.</param>
    internal sealed class TreeStatements(DataMetrics metrics, ILogger<TreeStatements> logger)
    {
        /// <summary>
        ///     Appends the verify statement of a tree table on the batch's internal channel: it
        ///     recounts every row and repairs those whose counts drifted. Running it twice has the
        ///     effect of running it once.
        /// </summary>
        /// <param name="batch">The batch the statement rides, a persist or a maintenance batch.</param>
        /// <param name="entity">The tree entity.</param>
        /// <returns>How many rows the statement repaired, once the batch executed.</returns>
        /// <exception cref="ArgumentException">The entity is not a tree.</exception>
        /// <exception cref="InvalidOperationException">The batch is a read or a validate batch.</exception>
        public BatchResult<int> Verify(DataBatch batch, EntityMetadata entity)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(entity);
            if (batch.Purpose is BatchPurpose.Read or BatchPurpose.Validate)
            {
                throw new InvalidOperationException($"A {batch.Purpose} batch writes nothing; the tree verify belongs to a {nameof(BatchPurpose.Maintenance)} batch.");
            }

            TreeShape tree = entity.Tree is null
                ? throw new ArgumentException($"The entity {entity.Name} is not a tree, so it has no counts to verify.", nameof(entity))
                : TreeShape.Of(entity);

            // --- The statement: the whole-table recount, then how many rows it repaired ---
            BatchResult<int> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                draft.EntityType = entity.ClrType;
                int ordinal = draft.Ordinal;
                batch.Append(draft, TreeText.Verify(tree), new HashSet<TableName> { entity.Table }, idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    int repaired = await set.ReadAsync(cancellationToken).ConfigureAwait(false) ? set.GetInt32(0) : 0;
                    cursor.CountRows(repaired);
                    cursor.Complete(result, repaired);
                });
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }

            // --- Once it ran: the recount is metered, and a repair is a warning ---
            string name = entity.Name;
            string table = entity.Table.Delimited;
            int tenantId = batch.Connection.TenantId;
            batch.OnExecuted(_ =>
            {
                if (result.IsCompleted)
                {
                    metrics.RecordTreeRecompute(name);
                    metrics.RecordTreeRepairs(name, result.Value);
                    if (result.Value > 0)
                    {
                        TreeStatementsLog.Repaired(logger, table, tenantId, result.Value);
                    }
                }

                return Task.CompletedTask;
            });

            return result;
        }
    }
}
