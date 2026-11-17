// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Data;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.IntegrationTests.Data;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Crud
{
    /// <summary>
    ///     The details reads against a real tenant database, through the real connect prologue,
    ///     runner and evaluator: a contributor's computed extra and the record permissions under a
    ///     filtered grant in one round trip once the caches are warm, a hidden row read as a missing
    ///     one, the related entities of the rows and of their collections, an arithmetic failure in
    ///     an extra passed through, a tree's roots and children in one round trip, a level beyond
    ///     the ceiling refused, and a cacheable list served with no round trip until its tag moves.
    /// </summary>
    /// <param name="fixture">The seeded tenant databases.</param>
    [Trait("Category", "Integration")]
    public sealed class DetailsPipelineTests(DetailsPipelineDatabase fixture) : IClassFixture<DetailsPipelineDatabase>
    {
        /// <summary>The widgets' resource.</summary>
        private const string Widgets = "fixture.Widget";

        /// <summary>The nodes' resource, a tree.</summary>
        private const string Nodes = "fixture.Node";

        /// <summary>The lookups' resource, a cacheable list.</summary>
        private const string Lookups = "fixture.Lookup";

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        // --- The extras ---

        [Fact]
        public async Task A_computed_extra_and_the_permissions_under_a_filtered_grant_ride_one_round_trip_once_the_caches_are_warm()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null), (Widgets, AccessActions.Save, "Price > 20"));
            DetailsRequest request = new() { Include = [WidgetDetails.PartCount, "permissions"] };

            // The first scope warms the caller's connection and set, and the plan's compilations.
            await using (AsyncServiceScope cold = await fixture.ScopeAsync(subject))
            {
                _ = await WidgetService(cold).GetByIdsAsync([3, 1], request, Token);
            }

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            (EntitiesResult<Widget> result, int roundTrips) = await MeasureAsync(scope, () => WidgetService(scope).GetByIdsAsync([3, 1], request, Token));

            Assert.Equal(1, roundTrips);
            Assert.Equal([3L, 1L], result.Ids);
            Assert.Equal([WidgetDetails.PartCount, "permissions"], result.Extras!.Keys);
            Assert.Equal(/*lang=json,strict*/ """{"3":1,"1":2}""", result.Extras[WidgetDetails.PartCount].GetRawText());

            // The save's filter misses widget 1, priced 10.5; the abilities not granted are not listed.
            Assert.Equal(/*lang=json,strict*/ """[{"id":3,"refused":{}},{"id":1,"refused":{"save":"outside-filter"}}]""", result.Extras["permissions"].GetRawText());
        }

        [Fact]
        public async Task An_arithmetic_failure_in_an_extra_passes_through_as_the_failure_it_is()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            ArithmeticFailureException failure = await Assert.ThrowsAsync<ArithmeticFailureException>(
                () => WidgetService(scope).GetByIdAsync(1, new DetailsRequest { Include = [WidgetDetails.Ratio] }, Token));

            Assert.Equal(ArithmeticFailureException.DivideByZero, failure.Reason);
        }

        // --- The rows ---

        [Fact]
        public async Task A_row_the_callers_filter_hides_is_read_as_a_missing_one()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, "Price > 20"));

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            NotFoundException hidden = await Assert.ThrowsAsync<NotFoundException>(() => WidgetService(scope).GetByIdAsync(1, new DetailsRequest(), Token));
            NotFoundException missing = await Assert.ThrowsAsync<NotFoundException>(() => WidgetService(scope).GetByIdAsync(9, new DetailsRequest(), Token));
            EntitiesResult<Widget> some = await WidgetService(scope).GetByIdsAsync([4, 1, 9, 3], new DetailsRequest(), Token);

            Assert.Equal((Widgets, "1"), (hidden.Resource, Assert.Single(hidden.Ids)));
            Assert.Equal((Widgets, "9"), (missing.Resource, Assert.Single(missing.Ids)));
            Assert.Equal([4L, 3L], some.Ids);
            Assert.Equal(["W4", "W3"], some.Entities.Select(static widget => widget.Code));
        }

        [Fact]
        public async Task A_row_comes_with_every_collection_and_what_it_and_its_collections_point_at()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null), (Nodes, AccessActions.Read, null));

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            EntitiesResult<Widget> widget = await WidgetService(scope).GetByIdAsync(1, new DetailsRequest(), Token);
            EntitiesResult<Node> node = await NodeService(scope).GetByIdAsync(3, new DetailsRequest(), Token);

            // --- The widget's parts and their notes ---
            Widget one = Assert.Single(widget.Entities);
            Assert.Equal([11, 12], one.Parts!.Select(static part => part.Id).Order());
            Assert.Equal(["first", "second"], one.Parts!.Single(static part => part.Id == 11).PartNotes!.Select(static note => note.Text).Order(StringComparer.Ordinal));

            // --- The node's parent, and the node its link points at, each read once ---
            Node leaf = Assert.Single(node.Entities);
            Assert.Equal(5, Assert.Single(leaf.Links!).OtherNodeId);
            RelatedEntitySet nodes = Assert.Single(node.Related!.Sets.Values, static set => set.EntityType == typeof(Node));
            Assert.Equal([2, 5], nodes.Entities.Cast<Node>().Select(static related => related.Id).Order());
            Assert.Equal(["N11", "N12"], nodes.Entities.Cast<Node>().Select(static related => related.Code).Order(StringComparer.Ordinal));
        }

        // --- The rows of a tree by parent ---

        [Fact]
        public async Task The_children_of_a_parent_with_the_roots_are_one_round_trip_once_the_caches_are_warm()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Nodes, AccessActions.Read, null));
            ParentIdsRequest request = new([1], "Id, Code", null, null, IncludeRoots: true);

            await using (AsyncServiceScope cold = await fixture.ScopeAsync(subject))
            {
                _ = await NodeService(cold).GetByParentIdsAsync(request, Token);
            }

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            (QueryRowSet rows, int roundTrips) = await MeasureAsync(scope, () => NodeService(scope).GetByParentIdsAsync(request, Token));

            Assert.Equal(1, roundTrips);
            Assert.Equal([1, 2, 4, 5, DetailsPipelineDatabase.CrowdedRoot], (int[])rows.GetBuffer(0));
        }

        [Fact]
        public async Task A_level_with_more_children_than_the_ceiling_is_refused()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Nodes, AccessActions.Read, null));

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            LimitExceededException refused = await Assert.ThrowsAsync<LimitExceededException>(
                () => NodeService(scope).GetByParentIdsAsync(new ParentIdsRequest([DetailsPipelineDatabase.CrowdedRoot], "Id", null, null), Token));

            Assert.Equal(
                (LimitNames.MaxTake, StackLimits.Default.MaxTake + 1, StackLimits.Default.MaxTake),
                (refused.Limit, refused.Actual, refused.Maximum));
        }

        // --- A cacheable list ---

        [Fact]
        public async Task A_cacheable_list_is_served_with_no_round_trip_once_warm_and_afresh_once_its_tag_moves()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Lookups, AccessActions.Read, null));
            DetailsRequest request = new();

            await using (AsyncServiceScope cold = await fixture.ScopeAsync(subject))
            {
                _ = await LookupService(cold).GetAllCachedAsync(request, Token);
            }

            // --- Warm: the cache answers ---
            await using (AsyncServiceScope warm = await fixture.ScopeAsync(subject))
            {
                (EntitiesResult<Lookup> all, int roundTrips) = await MeasureAsync(warm, () => LookupService(warm).GetAllCachedAsync(request, Token));
                Assert.Equal(0, roundTrips);
                Assert.Equal(["L1", "L2"], all.Entities.Select(static lookup => lookup.Code));
            }

            // --- A row written behind the cache with the tag moved, then any batch that reads the tags ---
            await fixture.Connect.ExecuteAsync(
                ConnectDatabase.TenantA,
                DataQueryDatabase.Stamped("fixture.Lookups", "[Id], [Code], [Name], [IsActive]", "(3, 'L3', N'Lookup 3', 1)"),
                $"UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'{VersionTagNames.ForEntity(Lookups)}';");
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            _ = await LookupService(scope).GetByIdAsync(1, request, Token);
            EntitiesResult<Lookup> fresh = await LookupService(scope).GetAllCachedAsync(request, Token);

            Assert.Equal(["L1", "L2", "L3"], fresh.Entities.Select(static lookup => lookup.Code));
        }

        // --- Helpers ---

        /// <summary>Runs a read in a scope and counts the round trips it made.</summary>
        /// <typeparam name="T">What the read returns.</typeparam>
        /// <param name="scope">The scope.</param>
        /// <param name="read">The read.</param>
        /// <returns>What it returned, and its round trips.</returns>
        private static async Task<(T Result, int RoundTrips)> MeasureAsync<T>(AsyncServiceScope scope, Func<Task<T>> read)
        {
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int before = access.RoundTrips;
            T result = await read();
            return (result, access.RoundTrips - before);
        }

        /// <summary>A scope's widget service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<Widget, int> WidgetService(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Widget, int>>();
        }

        /// <summary>A scope's node service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<Node, int> NodeService(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Node, int>>();
        }

        /// <summary>A scope's lookup service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<Lookup, int> LookupService(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Lookup, int>>();
        }
    }
}
