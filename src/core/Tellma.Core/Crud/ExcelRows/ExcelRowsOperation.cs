// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Access;
using Tellma.Core.Crud.Query;
using Tellma.Core.Data;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.ExcelRows
{
    // The row source is spec 0015 §15; its operation name and instruments are §16.1 and its budget
    // §16.2; a refusal raised in a composition is re-checked as a query's is (§5.2, spec 0014 §5.1).
    /// <summary>
    ///     The Excel row source's operation: it binds a row source to a stack's or a child query
    ///     surface's root and one source, and runs each of its read batches through the guarded
    ///     runner under the <c>excel_rows</c> operation, with the data layer's failures translated.
    ///     Scoped.
    /// </summary>
    /// <param name="evaluator">The caller's access decisions.</param>
    /// <param name="runner">The guarded batch runner, through which every page reconnects the caller.</param>
    /// <param name="database">The tenant's database.</param>
    /// <param name="planner">The query planner.</param>
    /// <param name="traversal">The traversal rule.</param>
    /// <param name="surfaces">The query surfaces.</param>
    /// <param name="reads">The reads of the lookup targets.</param>
    /// <param name="operations">The operation scope.</param>
    /// <param name="data">The data layer's options, whose large-batch threshold picks a read's lane.</param>
    internal sealed class ExcelRowsOperation(
        AccessEvaluator evaluator,
        IGuardedBatchRunner runner,
        ITenantDatabase database,
        QueryPlanner planner,
        QueryTraversalRule traversal,
        QuerySurfaces surfaces,
        TargetReads reads,
        CrudOperations operations,
        IOptions<DataOptions> data)
    {
        /// <summary>The tenant's database.</summary>
        internal ITenantDatabase Database { get; } = database;

        /// <summary>The query planner.</summary>
        internal QueryPlanner Planner { get; } = planner;

        /// <summary>The traversal rule.</summary>
        internal QueryTraversalRule Traversal { get; } = traversal;

        /// <summary>The query surfaces.</summary>
        internal QuerySurfaces Surfaces { get; } = surfaces;

        /// <summary>The reads of the lookup targets.</summary>
        internal TargetReads Reads { get; } = reads;

        /// <summary>
        ///     Binds a row source to a root and a source; it runs nothing. A child query surface takes
        ///     a source by ids or by query, without named filters.
        /// </summary>
        /// <param name="surface">The root's surface: a stack's or an exportable child query surface's.</param>
        /// <param name="source">The rows to read.</param>
        /// <param name="searchFilter">The service's branch replacing the searchable columns', given the stripped search; null for the platform's.</param>
        /// <returns>The row source.</returns>
        /// <exception cref="ArgumentException">A child query surface is given every row, or named filters.</exception>
        public IExcelRowSource Create(QuerySurface surface, ExportSource source, Func<string, FilterTree?>? searchFilter)
        {
            ArgumentNullException.ThrowIfNull(surface);
            ArgumentNullException.ThrowIfNull(source);
            if (surface.IsChild)
            {
                switch (source)
                {
                    case ExportSource.All:
                        throw new ArgumentException($"The child query surface {surface.Resource} exports rows by ids or by query, not every row.", nameof(source));
                    case ExportSource.ByQuery { NamedFilters.Count: > 0 }:
                        throw new ArgumentException($"The child query surface {surface.Resource} declares no named filters.", nameof(source));
                    default:
                        break;
                }
            }

            return new ExcelRowSource(this, surface, source, searchFilter);
        }

        /// <summary>The lane of a read over some payload rows.</summary>
        /// <param name="count">The ids or values the read binds.</param>
        /// <returns>The <c>crud.lane</c> value.</returns>
        internal string Lane(int count)
        {
            return CrudMetrics.Lane(count, data.Value.LargeBatchThreshold);
        }

        /// <summary>The type-level read check of a row source's root, with its step-up when the read is sensitive.</summary>
        /// <param name="resource">The root's resource.</param>
        /// <param name="cancellationToken">Cancels the check.</param>
        /// <returns>A task that completes when the read is granted.</returns>
        /// <exception cref="ForbiddenException">The caller holds no read grant on the root.</exception>
        internal Task RequireAsync(string resource, CancellationToken cancellationToken)
        {
            return evaluator.RequireAsync(resource, AccessActions.Read, cancellationToken);
        }

        /// <summary>Runs one call of a row source as an <c>excel_rows</c> operation, its data failures translated.</summary>
        /// <typeparam name="TResult">What the call returns.</typeparam>
        /// <param name="resource">The root's resource.</param>
        /// <param name="lane">The <c>crud.lane</c> value.</param>
        /// <param name="body">The call.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>What the call returned.</returns>
        internal Task<TResult> RunAsync<TResult>(string resource, string lane, Func<Task<TResult>> body, CancellationToken cancellationToken)
        {
            return operations.RunAsync(resource, CrudOperationNames.ExcelRows, source: null, lane, () => QueryOperation.TranslatedAsync(body), cancellationToken);
        }

        /// <summary>
        ///     One guarded read round trip: the batch composed for the connected user, and a path
        ///     refused by a restriction re-checked once, as a direct read of its entity would be.
        /// </summary>
        /// <typeparam name="TResult">What the round trip reads.</typeparam>
        /// <param name="binding">The row source's binding.</param>
        /// <param name="compose">Composes the batch's statements and returns what reads their results.</param>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <returns>What the round trip read.</returns>
        internal async Task<TResult> RoundTripAsync<TResult>(RowsBinding binding, Func<RowsComposition, Func<TResult>> compose, CancellationToken cancellationToken)
        {
            bool rechecked = false;
            while (true)
            {
                string? refusedBy = null;
                try
                {
                    Func<TResult>? read = null;
                    return await runner.RunAsync(
                        BatchPurpose.Read,
                        (user, batch) =>
                        {
                            read = compose(new RowsComposition(this, binding, user, batch, entity => refusedBy = entity));
                            return ValueTask.CompletedTask;
                        },
                        (_, _) => read!(),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (ForbiddenException) when (!rechecked && refusedBy is string entity)
                {
                    // A refusal raised in the composition never reaches a batch, so no prologue
                    // compares the set's tags for it: the evaluator re-checks a denial or a filter
                    // from a set older than the fast-deny window, and a grant it finds recomposes
                    // the round trip once.
                    rechecked = true;
                    AccessDecision decision = await evaluator.RecheckAsync(entity, AccessActions.Read, cancellationToken).ConfigureAwait(false);
                    if (decision.Outcome != AccessOutcome.Unrestricted)
                    {
                        throw;
                    }
                }
            }
        }
    }

    /// <summary>What a row source is bound to, and what its first call's admission found.</summary>
    /// <param name="Surface">The root's surface.</param>
    /// <param name="Source">The rows to read.</param>
    /// <param name="Ids">A by-ids source's ids, narrowed to the root's key type and distinct; null for any other source.</param>
    /// <param name="CustomSearch">The service's branch for a by-query source's search; null for the platform's or for none.</param>
    internal sealed record RowsBinding(QuerySurface Surface, ExportSource Source, IReadOnlyList<long>? Ids, FilterTree? CustomSearch)
    {
        /// <summary>The rows one statement reads at most, beyond which a follow-up reads on: the root's take ceiling.</summary>
        public int MaxTake => Surface.Limits.MaxTake;
    }
}
