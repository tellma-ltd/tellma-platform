// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Data.Batches;
using Tellma.Core.Queryex;

namespace Tellma.Core.Jobs
{
    /// <summary>A claim appended to a poll's batch: its handler, its token, and once the batch executed, its rows and their entities.</summary>
    /// <param name="Handler">The claim's handler.</param>
    /// <param name="Token">The claim's fencing token.</param>
    /// <param name="Rows">The rows leased under the token, in due order, once the batch executed.</param>
    /// <param name="Entities">The entity rows of an entity-backed handler's jobs, once the batch executed; null for a handler of the jobs themselves.</param>
    internal sealed record JobClaim(JobHandlerRegistration Handler, Guid Token, BatchResult<IReadOnlyList<ClaimedJob>> Rows, IJobEntityLoad? Entities);

    /// <summary>The entity rows of an entity-backed handler's claimed jobs.</summary>
    internal interface IJobEntityLoad
    {
        /// <summary>The loaded rows by the id of the job each names; a job whose entity is missing has none.</summary>
        /// <returns>The rows by job id.</returns>
        IReadOnlyDictionary<int, object> ByJobId();
    }

    // Spec 0020 §5.1: the entity join load rides the poll's round trip after the claim, restricted
    // to the claim's table variable of ids, with every mapped property selected.
    /// <summary>The entity load of one claim.</summary>
    /// <typeparam name="TEntity">The entity type the model maps.</typeparam>
    internal sealed class JobEntityLoad<TEntity> : IJobEntityLoad
        where TEntity : class, IJobEntity
    {
        /// <summary>The query's result, once the batch executed.</summary>
        private readonly BatchResult<EntityQueryResult<TEntity>> _rows;

        /// <summary>Appends the load after a claim.</summary>
        /// <param name="batch">The poll's batch.</param>
        /// <param name="claimedTable">The claim's table variable of ids.</param>
        public JobEntityLoad(DataBatch batch, string claimedTable)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentException.ThrowIfNullOrWhiteSpace(claimedTable);
            _rows = batch.Query(new EntityQuery<TEntity>
            {
                Select = nameof(IJobEntity.JobId),
                AllProperties = true,
                Restrictions = [new KeySetRestriction(nameof(IJobEntity.JobId), claimedTable)],
            });
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<int, object> ByJobId()
        {
            // A job names one owning row; should two name it, the first stands.
            Dictionary<int, object> rows = [];
            foreach (TEntity row in _rows.Value.Entities)
            {
                if (row.JobId is int jobId)
                {
                    _ = rows.TryAdd(jobId, row);
                }
            }

            return rows;
        }
    }
}
