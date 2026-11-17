// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Data;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Crud
{
    /// <summary>
    ///     The query pipeline against a real tenant database, through the real connect prologue,
    ///     runner and evaluator: a page of a tree with its count and ancestors in one round trip once
    ///     the caches are warm, the ancestors under the caller's access filter, a division by zero in
    ///     the caller's expression as a validation error, a child surface under its owner's filtered
    ///     grant in one round trip, a path into an entity the caller may not read refused, one
    ///     refused by a stale denial read once the re-check finds the grant, and the tree position a
    ///     hierarchy predicate reads held to the target's grant and step-up.
    /// </summary>
    /// <param name="fixture">The seeded tenant databases.</param>
    [Trait("Category", "Integration")]
    public sealed class QueryPipelineTests(QueryPipelineDatabase fixture) : IClassFixture<QueryPipelineDatabase>
    {
        /// <summary>The widgets' resource.</summary>
        private const string Widgets = "fixture.Widget";

        /// <summary>The widget parts' resource, a child query surface.</summary>
        private const string Parts = "fixture.WidgetPart";

        /// <summary>The nodes' resource, a tree.</summary>
        private const string Nodes = "fixture.Node";

        /// <summary>The links' resource, a child query surface of the nodes with a second reference to them.</summary>
        private const string Links = "fixture.Link";

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        // --- One round trip on warm caches ---

        [Fact]
        public async Task A_page_of_a_tree_with_its_count_and_ancestors_is_one_round_trip_once_the_caches_are_warm()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Nodes, AccessActions.Read, null));
            QueryRequest request = new()
            {
                Select = "Id, Code",
                Filters = ["Code = 'N111' or Code = 'N12'"],
                OrderBy = "Code",
                IncludeCount = true,
                IncludeAncestors = true,
            };

            // The first scope warms the caller's connection and set, the discovery and the plan.
            await using (AsyncServiceScope cold = await fixture.Connect.ScopeAsync(subject))
            {
                _ = await NodeService(cold).QueryAsync(request, Token);
            }

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            (RowPage page, int roundTrips) = await MeasureAsync(scope, () => NodeService(scope).QueryAsync(request, Token));

            Assert.Equal(1, roundTrips);
            Assert.Equal(["N111", "N12"], Strings(page.Rows, 1));
            Assert.Equal((2, StackLimits.Default.CountCap), (page.Count, page.CountCap));

            // The nodes above the page, none of the page's own.
            Assert.Equal(["N1", "N11"], Strings(Assert.IsType<QueryRowSet>(page.Ancestors), 1));
        }

        [Fact]
        public async Task The_ancestors_are_read_under_the_callers_access_filter()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Nodes, AccessActions.Read, "Code != 'N1'"));

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            RowPage page = await NodeService(scope).QueryAsync(
                new QueryRequest { Select = "Id, Code", Filters = ["Code = 'N111'"], IncludeAncestors = true }, Token);

            Assert.Equal(["N111"], Strings(page.Rows, 1));
            Assert.Equal(["N11"], Strings(Assert.IsType<QueryRowSet>(page.Ancestors), 1));
            Assert.Null(page.Count);
        }

        [Fact]
        public async Task A_child_surface_reads_under_its_owners_filtered_grant_in_one_round_trip_once_the_caches_are_warm()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, "Price > 20"));
            QueryRequest request = new() { Select = "Id, Notes, Widget.Subject", OrderBy = "Id", IncludeCount = true };

            await using (AsyncServiceScope cold = await fixture.Connect.ScopeAsync(subject))
            {
                _ = await Children(cold).QueryAsync(Parts, request, Token);
            }

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            (RowPage page, int roundTrips) = await MeasureAsync(scope, () => Children(scope).QueryAsync(Parts, request, Token));

            // The parts of the widgets priced above 20, the owner's columns read as the root's.
            Assert.Equal(1, roundTrips);
            Assert.Equal([31, 41], (int[])page.Rows.GetBuffer(0));
            Assert.Equal(["s3", "s4"], Strings(page.Rows, 2));
            Assert.Equal(2, page.Count);
        }

        // --- The refusals ---

        [Fact]
        public async Task A_division_by_zero_in_the_callers_expression_is_a_validation_error_at_the_root()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => WidgetService(scope).QueryAsync(
                new QueryRequest
                {
                    Select = "Code, Price / (Id - Id)",
                    Filters = ["Id = @id"],
                    Arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["id"] = JsonSerializer.SerializeToElement(1) },
                },
                Token));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal((ValidationPath.Root, ValidationCodes.QueryArithmeticFailed), (error.Path, error.Code));
            Assert.Equal(ArithmeticFailureException.DivideByZero, error.Arguments["reason"]);
        }

        [Fact]
        public async Task A_path_into_an_entity_the_caller_may_not_read_is_refused_naming_the_entity_and_the_path()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            RowPage projected = await WidgetService(scope).QueryAsync(new QueryRequest { Select = "Code, CreatedBy.Name", Filters = ["Id = 1"] }, Token);
            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(
                () => WidgetService(scope).QueryAsync(new QueryRequest { Select = "Code", Filters = ["CreatedBy.Email != ''"] }, Token));

            // The users' projection is readable without a grant on them; their other columns are not.
            Assert.Equal(["W1"], Strings(projected.Rows, 0));
            Assert.Equal(
                (ForbiddenReason.RestrictedPath, CoreResources.User, AccessActions.Read, "CreatedBy.Email"),
                (refused.Reason, refused.Resource, refused.Action, refused.Path));
        }

        [Fact]
        public async Task A_path_refused_by_a_stale_denial_reads_once_the_re_check_finds_a_grant_saved_since_the_set_was_cached()
        {
            await using ServiceProvider instance = fixture.Connect.CreateInstance(
                static services => services.PostConfigure<AccessOptions>(static options => options.FastDenyWindow = TimeSpan.FromTicks(1)));
            (int caller, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));
            QueryRequest request = new() { Select = "Code, CreatedBy.Email", Filters = ["Id = 1"] };

            // --- The cached set denies the users' read, and the re-check finds nothing new: refused ---
            await using (AsyncServiceScope cold = await fixture.Connect.ScopeAsync(subject, services: instance))
            {
                ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => WidgetService(cold).QueryAsync(request, Token));
                Assert.Equal((ForbiddenReason.RestrictedPath, CoreResources.User), (refused.Reason, refused.Resource));
            }

            // --- A save grants the users' read and moves the tags; the warm set still denies, the re-check reads ---
            await fixture.GrantAsync(caller, (CoreResources.User, AccessActions.Read, null));

            await using AsyncServiceScope warm = await fixture.Connect.ScopeAsync(subject, services: instance);
            RowPage page = await WidgetService(warm).QueryAsync(request, Token);

            Assert.Equal(["W1"], Strings(page.Rows, 0));
        }

        // --- The tree position a hierarchy predicate reads ---

        [Fact]
        public async Task A_hierarchy_predicate_over_a_path_into_the_tree_is_refused_under_an_absent_or_filtered_grant_and_read_under_a_full_one()
        {
            (_, string absent) = await fixture.SeedCallerAsync((Links, AccessActions.Read, null));
            (_, string filtered) = await fixture.SeedCallerAsync((Links, AccessActions.Read, null), (Nodes, AccessActions.Read, "Code != 'N1'"));
            (_, string full) = await fixture.SeedCallerAsync((Links, AccessActions.Read, null), (Nodes, AccessActions.Read, null));
            QueryRequest request = new() { Select = "Id", Filters = ["descendantOf(OtherNode.Id, 2)"] };

            // --- Without a full read of the nodes, the key reads and the position it tests is refused ---
            string[] restricted = [absent, filtered];
            foreach (string subject in restricted)
            {
                await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
                RowPage projected = await Children(scope).QueryAsync(Links, new QueryRequest { Select = "Id", Filters = ["OtherNode.Id = 3"] }, Token);
                ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => Children(scope).QueryAsync(Links, request, Token));

                Assert.Equal([2], (int[])projected.Rows.GetBuffer(0));
                Assert.Equal((ForbiddenReason.RestrictedPath, Nodes, "OtherNode.Node"), (refused.Reason, refused.Resource, refused.Path));
            }

            // --- Under a full read, the link pointing below N11 ---
            await using AsyncServiceScope reader = await fixture.Connect.ScopeAsync(full);
            RowPage page = await Children(reader).QueryAsync(Links, request, Token);

            Assert.Equal([2], (int[])page.Rows.GetBuffer(0));
        }

        [Fact]
        public async Task From_a_child_root_without_a_grant_of_its_own_a_hierarchy_predicate_reads_the_owners_position()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Nodes, AccessActions.Read, "Code != 'N2'"));

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            RowPage page = await Children(scope).QueryAsync(Links, new QueryRequest { Select = "Id", Filters = ["descendantOf(Node.Id, 2)"] }, Token);

            // Among the links of the nodes the caller reads, the one on a node below N11.
            Assert.Equal([1], (int[])page.Rows.GetBuffer(0));
        }

        [Fact]
        public async Task A_hierarchy_predicate_over_a_path_into_a_sensitive_tree_asks_for_the_step_up_a_direct_read_asks_for()
        {
            await using ServiceProvider instance = fixture.Connect.CreateInstance(static services => services.AddSingleton<ISecurableContributor>(new SensitiveNodes()));
            (_, string subject) = await fixture.SeedCallerAsync((Links, AccessActions.Read, null), (Nodes, AccessActions.Read, null));

            // The caller's sign-in never stepped up: the key reads, while the position asks what a direct read asks.
            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject, services: instance);
            RowPage projected = await Children(scope).QueryAsync(Links, new QueryRequest { Select = "Id", Filters = ["OtherNode.Id = 3"] }, Token);
            _ = await Assert.ThrowsAsync<StepUpRequiredException>(() => NodeService(scope).QueryAsync(new QueryRequest(), Token));
            _ = await Assert.ThrowsAsync<StepUpRequiredException>(
                () => Children(scope).QueryAsync(Links, new QueryRequest { Select = "Id", Filters = ["descendantOf(OtherNode.Id, 2)"] }, Token));

            Assert.Equal([2], (int[])projected.Rows.GetBuffer(0));
        }

        /// <summary>Runs a query and counts the round trips it took on the scope's data-access counters.</summary>
        /// <param name="scope">The scope.</param>
        /// <param name="query">The query.</param>
        /// <returns>The page and the round trips.</returns>
        private static async Task<(RowPage Page, int RoundTrips)> MeasureAsync(AsyncServiceScope scope, Func<Task<RowPage>> query)
        {
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int before = access.RoundTrips;
            RowPage page = await query();
            return (page, access.RoundTrips - before);
        }

        /// <summary>A scope's node service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<Node, int> NodeService(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Node, int>>();
        }

        /// <summary>A scope's widget service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<Widget, int> WidgetService(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Widget, int>>();
        }

        /// <summary>A scope's child query service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static IChildQueryService Children(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<IChildQueryService>();
        }

        /// <summary>A text column's values, nulls included.</summary>
        /// <param name="rows">The rows.</param>
        /// <param name="column">The column.</param>
        /// <returns>The values.</returns>
        private static List<string?> Strings(QueryRowSet rows, int column)
        {
            string?[] values = (string?[])rows.GetBuffer(column);
            return [.. values.Select((value, row) => rows.IsNull(column, row) ? null : value)];
        }

        /// <summary>The test's own mark: the nodes' read is sensitive.</summary>
        private sealed class SensitiveNodes : ISecurableContributor
        {
            /// <inheritdoc />
            public void Contribute(SecurableRegistryBuilder builder)
            {
                ArgumentNullException.ThrowIfNull(builder);
                _ = builder.MarkSensitive(Nodes, AccessActions.Read);
            }
        }
    }
}
