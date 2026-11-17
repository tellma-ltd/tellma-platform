// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;
using Tellma.Core.Data;
using Tellma.Core.Queryex;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Crud.Query
{
    // The steps are spec 0015 §5.2; the read's batch is §5.1; the translations are §14.2; a direct
    // read's step-up and deny re-check are spec 0014 §4.3 and §5.1.
    /// <summary>
    ///     The query operation of a stack or a child query surface: the type-level read check, the
    ///     ceilings, then one guarded read whose composition plans the query under the caller's
    ///     connected access, applies the traversal and select-type rules, and appends the one row
    ///     read with its count and ancestors. A path into another entity is held to what a direct
    ///     read of that entity asks: the step-up of a sensitive read, and the re-check of a denial
    ///     from a set the fast-deny window no longer vouches for. Scoped.
    /// </summary>
    /// <param name="evaluator">The caller's access decisions.</param>
    /// <param name="securables">The registered securables.</param>
    /// <param name="runner">The guarded batch runner.</param>
    /// <param name="database">The tenant's database.</param>
    /// <param name="accessor">The scope's request context, whose assurance a sensitive read is held to.</param>
    /// <param name="stepUp">The assurance bar of the sensitive securables.</param>
    /// <param name="planner">The planner.</param>
    /// <param name="traversal">The traversal rule.</param>
    /// <param name="operations">The operation scope.</param>
    /// <param name="time">The clock.</param>
    internal sealed class QueryOperation(
        AccessEvaluator evaluator,
        ISecurableRegistry securables,
        IGuardedBatchRunner runner,
        ITenantDatabase database,
        IRequestContextAccessor accessor,
        StepUpBar stepUp,
        QueryPlanner planner,
        QueryTraversalRule traversal,
        CrudOperations operations,
        TimeProvider time)
    {
        /// <summary>The problem code of a refused securable.</summary>
        private const string ForbiddenCode = "forbidden";

        /// <summary>The dependency a data-access failure names.</summary>
        private const string DatabaseDependency = "database";

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
                () => QueryAsync(surface, request, searchFilter, cancellationToken),
                cancellationToken);
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

            // A path refused by a denial is re-checked once, as a direct read of its entity would be.
            bool rechecked = false;
            while (true)
            {
                string? denied = null;
                try
                {
                    return await ReadAsync(surface, shape, customSearch, entity => denied = entity, cancellationToken).ConfigureAwait(false);
                }
                catch (ForbiddenException) when (!rechecked && denied is string entity)
                {
                    // The evaluator re-checks a denial from a set older than the fast-deny window in one
                    // prologue-only round trip per scope; a grant it finds recomposes the read once.
                    rechecked = true;
                    AccessDecision decision = await evaluator.EvaluateAsync(entity, AccessActions.Read, cancellationToken).ConfigureAwait(false);
                    if (decision.Outcome != AccessOutcome.Unrestricted)
                    {
                        throw;
                    }
                }
            }
        }

        /// <summary>
        ///     The query's one guarded read, with the data layer's failures translated into what the
        ///     caller can act on.
        /// </summary>
        /// <param name="surface">The surface.</param>
        /// <param name="shape">The admitted request.</param>
        /// <param name="customSearch">The service's search branch, or null.</param>
        /// <param name="refusedByDenial">Told the entity whose denial refused a path, before the refusal is thrown.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The page.</returns>
        private async Task<RowPage> ReadAsync(
            QuerySurface surface, QueryShape shape, FilterTree? customSearch, Action<string> refusedByDenial, CancellationToken cancellationToken)
        {
            BatchResult<RowPage>? page = null;
            try
            {
                return await runner.RunAsync(
                    BatchPurpose.Read,
                    (user, batch) =>
                    {
                        page = Compose(surface, shape, customSearch, refusedByDenial, user, batch);
                        return ValueTask.CompletedTask;
                    },
                    (_, _) => page!.Value,
                    cancellationToken).ConfigureAwait(false);
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

        /// <summary>
        ///     Composes the read under one connected user: the root decision from the user's set,
        ///     the plan, the rules after compilation, then the row read.
        /// </summary>
        /// <param name="surface">The surface.</param>
        /// <param name="shape">The admitted request.</param>
        /// <param name="customSearch">The service's search branch, or null.</param>
        /// <param name="refusedByDenial">Told the entity whose denial refused a path, before the refusal is thrown.</param>
        /// <param name="user">The connected user the batch is composed under.</param>
        /// <param name="batch">The batch.</param>
        /// <returns>The row read's handle.</returns>
        private BatchResult<RowPage> Compose(
            QuerySurface surface, QueryShape shape, FilterTree? customSearch, Action<string> refusedByDenial, ConnectedUser user, IDataBatch batch)
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

            // --- The traversal rule over the caller's paths: the first refusal, its entity told when a denial caused it ---
            HashSet<string> fullReads = new(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<TraversalRefusal> refusals = traversal.Check(schema, root.Name, plan.Discovery, Decide, fullReads);
            if (refusals.Count > 0)
            {
                TraversalRefusal first = refusals[0];
                if (Decide(first.Entity).Outcome == AccessOutcome.Denied)
                {
                    refusedByDenial(first.Entity);
                }

                throw new ForbiddenException(ForbiddenCode, first.Entity, AccessActions.Read, ForbiddenReason.RestrictedPath, string.Join('.', first.Path.Segments));
            }

            // --- A sensitive entity read beyond its projection asks for the step-up a direct read of it asks for ---
            RequestContext context = accessor.Current;
            if (!context.IsSystem)
            {
                foreach (string entity in fullReads)
                {
                    if (securables.Find(entity, AccessActions.Read) is { IsSensitive: true })
                    {
                        AssuranceCheck.Ensure(context, stepUp.Requirement, now);
                    }
                }
            }

            // --- The select-item types ---
            QueryPlanner.RequireSelectable(plan, surface.Resource);

            // --- The one row read: the page, its capped count and its ancestors ---
            return batch.Rows(plan.Spec, plan.Arguments, plan.Options);
        }
    }
}
