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
        /// <summary>Each read takes its round-trip budget: one for a query, a details read and a child surface's query, none for a cached list.</summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task Each_read_takes_its_round_trip_budget()
        {
            StackConformanceCaller writer = await WriterAsync();
            TEntity stored = await CreateNewAsync();

            // --- A first read warms the caller's connection and access ---
            if (Serves(StackOperations.Query))
            {
                _ = await CountedAsync(writer, scope => Service(scope).QueryAsync(ById(stored.Id), Token));
                AssertTrips(1, await CountedAsync(writer, scope => Service(scope).QueryAsync(ById(stored.Id), Token)), "a query");
            }

            if (Serves(StackOperations.Details))
            {
                AssertTrips(1, await CountedAsync(writer, scope => Service(scope).GetByIdAsync(stored.Id, DetailsRequest.None, Token)), "a details read by id");
                AssertTrips(1, await CountedAsync(writer, scope => Service(scope).GetByIdsAsync([stored.Id], DetailsRequest.None, Token)), "a details read by ids");
            }

            // --- The cached list: one read fills the cache, and the next costs nothing ---
            if (Serves(StackOperations.Details, Cacheable))
            {
                _ = await CountedAsync(writer, scope => Service(scope).GetAllCachedAsync(DetailsRequest.None, Token));
                AssertTrips(0, await CountedAsync(writer, scope => Service(scope).GetAllCachedAsync(DetailsRequest.None, Token)), "a cached list read");
            }

            // --- Each child query surface: one round trip ---
            foreach (ChildQueryDescriptor child in ChildSurfaces())
            {
                QueryRequest request = new() { Select = "Id", Take = 1 };
                AssertTrips(1, await CountedAsync(writer, scope => scope.GetRequiredService<IChildQueryService>().QueryAsync(child.Resource, request, Token)), $"a query of {child.Resource}");
            }
        }

        /// <summary>
        ///     Each write takes its round-trip budget: a create one, or two when it has anything to
        ///     load; an update two; a delete and a built-in action one, or two when a validator judges
        ///     them.
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

            // --- A delete: the delete with its pre-check, behind a validation round when judged ---
            if (Serves(StackOperations.Delete))
            {
                AssertTrips(ValidatesDeletes ? 2 : 1, await CountedAsync(writer, scope => Service(scope).DeleteByIdsAsync([warm.Id], null, Token)), "a delete");
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
