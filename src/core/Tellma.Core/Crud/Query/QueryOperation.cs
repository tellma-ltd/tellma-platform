// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;
using Tellma.Core.Data;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Query
{
    // The steps are spec 0015 §5.2; the tree read is §5.4; the read's batch is §5.1; the translations
    // are §14.2; a direct read's step-up and deny re-check are spec 0014 §4.3 and §5.1.
    /// <summary>
    ///     The query operation of a stack or a child query surface, and the tree read of a tree stack:
    ///     the type-level read check, the ceilings, then one guarded read whose composition plans the
    ///     query under the caller's connected access, applies the traversal and select-type rules,
    ///     and appends the one row read. A path into another entity is held to what a direct read of
    ///     that entity asks: the step-up of a sensitive read, which the traversal rule asks for, and
    ///     the re-check of a restriction from a set the fast-deny window no longer vouches for. Scoped.
    /// </summary>
    /// <param name="evaluator">The caller's access decisions.</param>
    /// <param name="securables">The registered securables.</param>
    /// <param name="runner">The guarded batch runner.</param>
    /// <param name="database">The tenant's database.</param>
    /// <param name="planner">The planner.</param>
    /// <param name="traversal">The traversal rule.</param>
    /// <param name="operations">The operation scope.</param>
    /// <param name="metadata">The tenant model's entity metadata.</param>
    /// <param name="data">The data layer's options, whose large-batch threshold picks a tree read's lane.</param>
    /// <param name="time">The clock.</param>
    internal sealed class QueryOperation(
        AccessEvaluator evaluator,
        ISecurableRegistry securables,
        IGuardedBatchRunner runner,
        ITenantDatabase database,
        QueryPlanner planner,
        QueryTraversalRule traversal,
        CrudOperations operations,
        IEntityMetadataProvider metadata,
        IOptions<DataOptions> data,
        TimeProvider time)
    {
        /// <summary>The problem code of a refused securable.</summary>
        private const string ForbiddenCode = "forbidden";

        /// <summary>The dependency a data-access failure names.</summary>
        private const string DatabaseDependency = "database";

        /// <summary>The path a tree read's out-of-range parent id is reported at.</summary>
        private const string ParentIdsPath = "parentIds";

        /// <summary>The wait a caller is told before retrying a read the database could not serve.</summary>
        private static readonly TimeSpan DatabaseRetryAfter = TimeSpan.FromSeconds(1);

        /// <summary>Runs one query.</summary>
        /// <param name="surface">The surface it runs on.</param>
        /// <param name="request">The request.</param>
        /// <param name="searchFilter">The service's branch replacing the searchable columns', given the stripped search; null for none.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The page.</returns>
        public Task<RowPage> RunAsync(QuerySurface surface, QueryRequest request, Func<string, FilterTree?>? searchFilter, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(surface);
            ArgumentNullException.ThrowIfNull(request);
            return operations.RunAsync(
                surface.Resource,
                CrudOperationNames.Query,
                source: null,
                CrudMetrics.SmallLane,
                () => TranslatedAsync(() => QueryAsync(surface, request, searchFilter, cancellationToken)),
                cancellationToken);
        }

        /// <summary>
        ///     Runs one tree read: the roots, the children of some parents, or both as one row set, in
        ///     one round trip, bounded by the page-size ceiling.
        /// </summary>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="surface">The tree stack's surface.</param>
        /// <param name="entityType">The stack's entity type, a tree.</param>
        /// <param name="request">The request.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The rows.</returns>
        public Task<QueryRowSet> RunParentIdsAsync<TKey>(QuerySurface surface, Type entityType, ParentIdsRequest request, CancellationToken cancellationToken)
            where TKey : struct
        {
            ArgumentNullException.ThrowIfNull(surface);
            ArgumentNullException.ThrowIfNull(entityType);
            ArgumentNullException.ThrowIfNull(request);
            return operations.RunAsync(
                surface.Resource,
                CrudOperationNames.GetByParentIds,
                source: null,
                CrudMetrics.Lane(request.ParentIds?.Count ?? 0, data.Value.LargeBatchThreshold),
                () => TranslatedAsync(() => ParentIdsAsync<TKey>(surface, entityType, request, cancellationToken)),
                cancellationToken);
        }

        /// <summary>
        ///     Runs a read's steps with the data layer's failures translated into what the caller can
        ///     act on, wherever they arise: the connect, a re-check, or the read.
        /// </summary>
        /// <typeparam name="TResult">What the read returns.</typeparam>
        /// <param name="steps">The read's steps.</param>
        /// <returns>What the read returned.</returns>
        private static async Task<TResult> TranslatedAsync<TResult>(Func<Task<TResult>> steps)
        {
            try
            {
                return await steps().ConfigureAwait(false);
            }
            catch (ArithmeticFailureException failure)
            {
                // A division by zero or an overflow in the caller's expressions is the caller's to fix.
                throw new ValidationException(
                [
                    new ValidationError(
                        ValidationPath.Root,
                        ValidationCodes.QueryArithmeticFailed,
                        new Dictionary<string, object?>(StringComparer.Ordinal) { ["reason"] = failure.Reason }),
                ]);
            }
            catch (DataAccessRetryExhaustedException failure)
            {
                // Deadlocks and transient failures outlasted the executor's retries: worth a retry soon.
                throw new DependencyUnavailableException(DatabaseDependency, DatabaseRetryAfter, failure);
            }
        }

        /// <summary>The steps of one query.</summary>
        /// <param name="surface">The surface.</param>
        /// <param name="request">The request.</param>
        /// <param name="searchFilter">The service's search branch, or null.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The page.</returns>
        private async Task<RowPage> QueryAsync(QuerySurface surface, QueryRequest request, Func<string, FilterTree?>? searchFilter, CancellationToken cancellationToken)
        {
            // --- The type-level grant, then the ceilings: both refuse before any round trip but the connect ---
            _ = await evaluator.RequireAsync(surface.Resource, AccessActions.Read, cancellationToken).ConfigureAwait(false);
            var shape = QueryShape.Admit(request, surface);

            // The service's branch reads only the text, so it is asked once, whatever the recompositions.
            FilterTree? customSearch = shape.Search is string search && searchFilter is not null ? searchFilter(search) : null;
            return await ReadAsync(surface, shape, customSearch, narrow: null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>The steps of one tree read.</summary>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="surface">The tree stack's surface.</param>
        /// <param name="entityType">The stack's entity type.</param>
        /// <param name="request">The request.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The rows.</returns>
        private async Task<QueryRowSet> ParentIdsAsync<TKey>(QuerySurface surface, Type entityType, ParentIdsRequest request, CancellationToken cancellationToken)
            where TKey : struct
        {
            EntityMetadata entity = metadata.Get(entityType);
            TreeMetadata tree = entity.Tree
                ?? throw new InvalidOperationException($"The entity {entity.Name} is no tree, so it has no rows by parent.");

            // --- The type-level grant, then the ceilings: both refuse before any round trip but the connect ---
            _ = await evaluator.RequireAsync(surface.Resource, AccessActions.Read, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<long> sent = request.ParentIds ?? [];
            if (sent.Count > surface.Limits.MaxIds)
            {
                throw new LimitExceededException(LimitNames.MaxIds, sent.Count, surface.Limits.MaxIds);
            }

            TKey[] parents = [.. WireIds.ToKeys<TKey>(sent, ParentIdsPath).Distinct()];

            // --- Which rows (spec 0015 §5.4): the roots when no parent is named, by the filter's leaf ---
            List<FilterTree> picks = parents.Length == 0 ? [FilterTree.Leaf($"{tree.ParentKey.Name} = null")] : [];
            var shape = QueryShape.AdmitParentIds(request, surface, picks);
            Func<IDataBatch, QuerySpec, QuerySpec>? narrow = parents.Length == 0
                ? null
                : request.IncludeRoots
                    ? (batch, spec) => RootsAndChildren(batch, spec, entity, tree, parents)
                    : (batch, spec) => spec with { Restrictions = [new KeySetRestriction(tree.ParentKey.Name, KeyTables.Bind(batch, parents).Name)] };

            // --- The read: one row beyond the ceiling tells a level that exceeds it ---
            RowPage page = await ReadAsync(surface, shape, customSearch: null, narrow, cancellationToken).ConfigureAwait(false);
            return page.Rows.RowCount > surface.Limits.MaxTake
                ? throw new LimitExceededException(LimitNames.MaxTake, shape.Take, surface.Limits.MaxTake)
                : page.Rows;
        }

        /// <summary>
        ///     Narrows a tree read to the roots and the children of its parents, as one row set: a keyed
        ///     statement fills a key table with both, since restrictions only conjoin.
        /// </summary>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="batch">The batch.</param>
        /// <param name="spec">The planned spec.</param>
        /// <param name="entity">The tree entity.</param>
        /// <param name="tree">The tree's columns.</param>
        /// <param name="parents">The parents' ids.</param>
        /// <returns>The spec, restricted to the table.</returns>
        private static QuerySpec RootsAndChildren<TKey>(IDataBatch batch, QuerySpec spec, EntityMetadata entity, TreeMetadata tree, TKey[] parents)
            where TKey : struct
        {
            SqlIdentifier parentIds = KeyTables.Bind(batch, parents);
            SqlIdentifier picked = KeyTables.Of(batch).DeclareKeyTable(typeof(TKey));
            SqlIdentifier table = new($"{entity.Table.Schema}.{entity.Table.Name}");
            SqlIdentifier key = new(entity.Key.Column);
            SqlIdentifier parentKey = new(tree.ParentKey.Column);

            // The parent-id index serves both seeks; the access filter and the active flag apply to the read itself.
#pragma warning disable TELLMA0003
            _ = batch.Sql(
                $"""
                INSERT INTO {picked} ([Id])
                SELECT T.{key} FROM {table} AS T WHERE T.{parentKey} IS NULL
                UNION
                SELECT T.{key} FROM {table} AS T WHERE T.{parentKey} IN (SELECT P.[Id] FROM {parentIds} AS P);
                """,
                SqlOptions.None);
#pragma warning restore TELLMA0003
            return spec with { Restrictions = [new KeySetRestriction(entity.Key.Name, picked.Name)] };
        }

        /// <summary>The read: a path refused by a restriction is re-checked once, as a direct read of its entity would be.</summary>
        /// <param name="surface">The surface.</param>
        /// <param name="shape">The admitted request.</param>
        /// <param name="customSearch">The service's search branch, or null.</param>
        /// <param name="narrow">Narrows the planned spec on the batch it is composed on; null keeps it.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The page.</returns>
        private async Task<RowPage> ReadAsync(
            QuerySurface surface, QueryShape shape, FilterTree? customSearch, Func<IDataBatch, QuerySpec, QuerySpec>? narrow, CancellationToken cancellationToken)
        {
            bool rechecked = false;
            while (true)
            {
                string? refusedBy = null;
                try
                {
                    return await GuardedReadAsync(surface, shape, customSearch, narrow, entity => refusedBy = entity, cancellationToken).ConfigureAwait(false);
                }
                catch (ForbiddenException) when (!rechecked && refusedBy is string entity)
                {
                    // A refusal raised in the composition never reaches a batch, so no prologue
                    // compares the set's tags for it: the evaluator re-checks a denial or a filter
                    // from a set older than the fast-deny window in one prologue-only round trip
                    // per scope, and a grant it finds recomposes the read once.
                    rechecked = true;
                    AccessDecision decision = await evaluator.RecheckAsync(entity, AccessActions.Read, cancellationToken).ConfigureAwait(false);
                    if (decision.Outcome != AccessOutcome.Unrestricted)
                    {
                        throw;
                    }
                }
            }
        }

        /// <summary>The read's one guarded round trip.</summary>
        /// <param name="surface">The surface.</param>
        /// <param name="shape">The admitted request.</param>
        /// <param name="customSearch">The service's search branch, or null.</param>
        /// <param name="narrow">Narrows the planned spec on the batch it is composed on; null keeps it.</param>
        /// <param name="refusedBy">Told the entity whose decision refused a path, before the refusal is thrown.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The page.</returns>
        private async Task<RowPage> GuardedReadAsync(
            QuerySurface surface,
            QueryShape shape,
            FilterTree? customSearch,
            Func<IDataBatch, QuerySpec, QuerySpec>? narrow,
            Action<string> refusedBy,
            CancellationToken cancellationToken)
        {
            BatchResult<RowPage>? page = null;
            return await runner.RunAsync(
                BatchPurpose.Read,
                (user, batch) =>
                {
                    page = Compose(surface, shape, customSearch, narrow, refusedBy, user, batch);
                    return ValueTask.CompletedTask;
                },
                (_, _) => page!.Value,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        ///     Composes the read under one connected user: the root decision from the user's set,
        ///     the plan, the rules after compilation, then the row read.
        /// </summary>
        /// <param name="surface">The surface.</param>
        /// <param name="shape">The admitted request.</param>
        /// <param name="customSearch">The service's search branch, or null.</param>
        /// <param name="narrow">Narrows the planned spec on the batch it is composed on; null keeps it.</param>
        /// <param name="refusedBy">Told the entity whose decision refused a path, before the refusal is thrown.</param>
        /// <param name="user">The connected user the batch is composed under.</param>
        /// <param name="batch">The batch.</param>
        /// <returns>The row read's handle.</returns>
        /// <exception cref="Abstractions.Tenancy.StepUpRequiredException">A path reads a sensitive entity beyond its projection below the step-up bar.</exception>
        private BatchResult<RowPage> Compose(
            QuerySurface surface,
            QueryShape shape,
            FilterTree? customSearch,
            Func<IDataBatch, QuerySpec, QuerySpec>? narrow,
            Action<string> refusedBy,
            ConnectedUser user,
            IDataBatch batch)
        {
            // Every pipeline batch compiles under the pipeline's ceilings, so the batch admits what the plan admitted.
            batch.Limits = QueryexProfiles.PipelineLimits;
            QueryexSchema schema = database.Schema;
            EntityDescriptor root = schema.FindEntity(surface.Resource)
                ?? throw new InvalidOperationException($"The tenant's schema has no entity for the query surface '{surface.Resource}'.");

            // --- The decisions of this composition, each made once from the connected user's set ---
            DateTimeOffset now = time.GetUtcNow();
            Dictionary<string, AccessDecision> decided = new(StringComparer.OrdinalIgnoreCase);
            AccessDecision Decide(string entity)
            {
                if (!decided.TryGetValue(entity, out AccessDecision? decision))
                {
                    decision = securables.Find(entity, AccessActions.Read) is SecurableDescriptor securable
                        ? evaluator.DecideFrom(user.Access, securable, now)
                        : evaluator.Unknown(entity, AccessActions.Read);
                    decided[entity] = decision;
                }

                return decision;
            }

            AccessDecision rootDecision = Decide(root.Name);
            if (rootDecision.Outcome == AccessOutcome.Denied)
            {
                throw new ForbiddenException(ForbiddenCode, rootDecision.Resource, rootDecision.Action, ForbiddenReason.NoGrant);
            }

            // --- The plan, compiled: any diagnostic refuses the query here ---
            PlannedQuery plan = planner.Plan(surface, shape, schema, root, rootDecision.Filter, customSearch, hasUser: database.Context.UserId is not null);

            // --- The traversal rule over the caller's paths, which also asks for the step-up of a
            // sensitive entity read beyond its projection: the first refusal, its entity told ---
            IReadOnlyList<TraversalRefusal> refusals = traversal.Check(schema, root.Name, plan.Discovery, Decide);
            if (refusals.Count > 0)
            {
                TraversalRefusal first = refusals[0];
                refusedBy(first.Entity);
                throw new ForbiddenException(ForbiddenCode, first.Entity, AccessActions.Read, ForbiddenReason.RestrictedPath, string.Join('.', first.Path.Segments));
            }

            // --- The select-item types ---
            QueryPlanner.RequireSelectable(plan, surface.Resource);

            // --- The one row read: the page, its capped count and its ancestors, narrowed by the caller's statements ---
            QuerySpec spec = narrow is null ? plan.Spec : narrow(batch, plan.Spec);
            return batch.Rows(spec, plan.Arguments, plan.Options);
        }
    }
}
