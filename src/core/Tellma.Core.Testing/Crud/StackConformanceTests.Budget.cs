// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Xunit;

namespace Tellma.Core.Testing.Crud
{
    // Spec 0015 §16.2: the round-trip budget of every operation, measured by the platform's own
    // count and, when the harness observes the driver, by the server's.
    /// <content>The round-trip budget.</content>
    public abstract partial class StackConformanceTests<TEntity, TKey>
    {
        /// <summary>
        ///     Each read takes its round-trip budget: one for a query (with its count, and with its
        ///     ancestors on a tree), a details read, a read by parent ids and a child surface's query;
        ///     none for a cached list, or two on a table beyond the list's row limit.
        /// </summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task Each_read_takes_its_round_trip_budget()
        {
            StackConformanceCaller writer = await WriterAsync();
            TEntity stored = await CreateNewAsync();

            // --- Each read the stack serves and its budget. Excel rows join this table, at one round
            // trip per page, once the platform serves them ---
            List<(string What, int Budget, Func<IServiceProvider, Task> Call)> reads = [];
            if (Serves(StackOperations.Query))
            {
                QueryRequest counted = ById(stored.Id);
                counted.IncludeCount = true;
                reads.Add(("a query", 1, scope => Service(scope).QueryAsync(ById(stored.Id), Token)));
                reads.Add(("a query with its count", 1, scope => Service(scope).QueryAsync(counted, Token)));
            }

            if (Serves(StackOperations.Query, Tree))
            {
                QueryRequest ancestors = ById(stored.Id);
                ancestors.IncludeAncestors = true;
                reads.Add(("a query with its ancestors", 1, scope => Service(scope).QueryAsync(ancestors, Token)));
                reads.Add(("a read by parent ids", 1, scope => Service(scope).Pipeline.GetByParentIdsAsync(ByParent(stored.Id), Token)));
            }

            if (Serves(StackOperations.Details))
            {
                reads.Add(("a details read by id", 1, scope => Service(scope).GetByIdAsync(stored.Id, DetailsRequest.None, Token)));
                reads.Add(("a details read by ids", 1, scope => Service(scope).GetByIdsAsync([stored.Id], DetailsRequest.None, Token)));
            }

            foreach (ChildQueryDescriptor child in ChildSurfaces())
            {
                QueryRequest request = new() { Select = "Id", Take = 1 };
                reads.Add(($"a query of {child.Resource}", 1, scope => scope.GetRequiredService<IChildQueryService>().QueryAsync(child.Resource, request, Token)));
            }

            // --- Each read once to warm its caller, its plan and its caches, then counted ---
            foreach ((string what, int budget, Func<IServiceProvider, Task> call) in reads)
            {
                _ = await CountedAsync(writer, async scope =>
                {
                    await call(scope);
                    return 0;
                });
                AssertTrips(budget, await CountedAsync(writer, async scope =>
                {
                    await call(scope);
                    return 0;
                }), what);
            }

            // --- The cached list: one read fills the cache, and the next costs nothing; beyond the
            // list's row limit, every read probes and re-reads ---
            if (Serves(StackOperations.Details, Cacheable))
            {
                bool? oversized = await OversizedAsync();
                Assert.SkipWhen(oversized is null, $"The stack {Stack.Resource} serves no query to count its rows by, so the suite cannot tell whether its cached list is beyond its row limit.");
                _ = await CountedAsync(writer, scope => Service(scope).GetAllCachedAsync(DetailsRequest.None, Token));
                AssertTrips(oversized == true ? 2 : 0, await CountedAsync(writer, scope => Service(scope).GetAllCachedAsync(DetailsRequest.None, Token)), "a cached list read");
            }
        }

        /// <summary>
        ///     Each write takes its round-trip budget: a create one, or two when it has anything to
        ///     load; an update two; a delete by query one; a delete by ids or with descendants and a
        ///     built-in action one, or two when a validator judges them; a custom action two; each plus
        ///     the validators' dependent rounds where they apply.
        /// </summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task Each_write_takes_its_round_trip_budget()
        {
            Assert.SkipUnless(Serves(StackOperations.Save) || Serves(StackOperations.Delete), $"The stack {Stack.Resource} serves no write.");
            StackConformanceCaller writer = await WriterAsync();

            // --- A first create warms the caller and the id buffers ---
            TEntity warm = await CreateNewAsync();

            if (Serves(StackOperations.Save))
            {
                // --- A create: its persist and read-back, behind a validation round when it loads ---
                TEntity created = NewValidEntity(NextOrdinal());
                AssertTrips(CreateBudget(created), await CountedAsync(writer, scope => Service(scope).SaveAsync([created], new SaveOptions(), Token)), "a create");

                // --- An update: the before image on the first round trip, the persist on the second ---
                TEntity loaded = await LoadAsync(warm.Id);
                Change(loaded, NextOrdinal());
                AssertTrips(2 + DependentValidationRounds, await CountedAsync(writer, scope => Service(scope).SaveAsync([loaded], new SaveOptions(), Token)), "an update");
            }

            // --- The built-in actions: the update with its pre-check, behind a validation round when judged ---
            if (ServesActivation())
            {
                int budget = ValidatesActions ? 2 : 1;
                AssertTrips(budget, await CountedAsync(writer, scope => Service(scope).ExecuteActionAsync(DeactivateAction, [warm.Id], null, new ActionOptions(), Token)), "a deactivate");
                AssertTrips(budget, await CountedAsync(writer, scope => Service(scope).ExecuteActionAsync(ActivateAction, [warm.Id], null, new ActionOptions(), Token)), "an activate");
            }

            // --- Each custom action: its targets with the first round, its persist on the second,
            // and the rounds its loads depend on ---
            foreach (EntityActionDescriptor action in Stack.Actions.OfType<EntityActionDescriptor>().Where(static action => !action.IsBuiltIn))
            {
                AssertTrips(
                    2 + DependentActionRounds(action),
                    await CountedAsync(writer, scope => Service(scope).ExecuteActionAsync(action.Name, [warm.Id], ActionArguments(action), new ActionOptions(), Token)),
                    $"the action {action.Name}");
            }

            // --- A delete by query: its count and delete in one round trip, judged or not, since it
            // names no ids a validator could see (spec 0015 §10.2) ---
            if (Serves(StackOperations.DeleteByQuery))
            {
                TEntity matched = await CreateNewAsync();
                DeleteByQueryRequest request = new($"{Metadata.Key.Name} = {Text(matched.Id)}", null, 1);
                AssertTrips(1, await CountedAsync(writer, scope => Service(scope).DeleteByQueryAsync(request, Token)), "a delete by query");
            }

            // --- The deletes that name ids: each with its pre-check, behind a validation round when judged ---
            int deleteBudget = ValidatesDeletes ? 2 : 1;

            if (Serves(StackOperations.Delete, Tree))
            {
                TEntity parent = await CreateNewAsync();
                _ = await CreateAsync(NewChildOf(parent, NextOrdinal()));
                AssertTrips(deleteBudget, await CountedAsync(writer, scope => Service(scope).Pipeline.DeleteWithDescendantsAsync([parent.Id], Token)), "a delete with descendants");
            }

            if (Serves(StackOperations.Delete))
            {
                AssertTrips(deleteBudget, await CountedAsync(writer, scope => Service(scope).DeleteByIdsAsync([warm.Id], null, Token)), "a delete");
            }
        }

        /// <summary>
        ///     The round trips a create of an entity takes: its persist, behind one validation round
        ///     when the pipeline or a validator has anything to load, and the validators' own rounds.
        /// </summary>
        /// <param name="entity">The entity to create.</param>
        /// <returns>The budget.</returns>
        private int CreateBudget(TEntity entity)
        {
            return 1 + (ValidatorsLoad || Loads(Metadata, entity) ? 1 : 0) + DependentValidationRounds;
        }

        /// <summary>
        ///     Whether a create of a row, or of any row its collections hold, has anything for the
        ///     pipeline to load: a unique value to probe, or a reference the client sets to resolve.
        /// </summary>
        /// <param name="entity">The row's metadata.</param>
        /// <param name="row">The row.</param>
        /// <param name="parentKey">The row's parent key, which its nesting sets; null for a root.</param>
        /// <returns>True when it loads.</returns>
        private static bool Loads(EntityMetadata entity, object row, PropertyMetadata? parentKey = null)
        {
            // --- The values a client writes: neither the key, a blob column nor a server-owned one ---
            HashSet<PropertyMetadata> blobs = new(entity.BlobReferences.Select(static blob => blob.Property), ReferenceEqualityComparer.Instance);
            bool Written(PropertyMetadata property)
            {
                return !ReferenceEquals(property, entity.Key)
                    && !ReferenceEquals(property, parentKey)
                    && !blobs.Contains(property)
                    && property.Ownership is PropertyOwnership.Editable or PropertyOwnership.WriteOnce
                    && property.Getter(row) is not null and not 0 and not 0L;
            }

            if (entity.Properties.Any(property => property.IsUnique && Written(property))
                || entity.References.Any(reference => Written(reference.ForeignKey)))
            {
                return true;
            }

            // --- The rows its collections hold, each under its own parent key ---
            return entity.Children.Any(collection => collection.Getter(row)?.Any(child => Loads(collection.Child, child, collection.ParentKey)) == true);
        }
    }
}
