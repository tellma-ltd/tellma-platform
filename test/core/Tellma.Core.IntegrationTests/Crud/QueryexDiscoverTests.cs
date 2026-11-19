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
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Query;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.Queryex;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Crud
{
    /// <summary>
    ///     The Queryex authoring endpoint against a real tenant database, through the real connect
    ///     prologue, the action invoker and the access evaluator: an unknown root and the measures
    ///     refused before any round trip; the diagnostics the query refuses the same clauses for; a
    ///     path a filtered grant refuses reported where it is written; an unconstrained parameter
    ///     untyped and unreported; a permission filter answered with what the role save refuses it
    ///     for; the comparisons of two filters with their literals in the wire's encoding; and no
    ///     round trip once the caller is connected.
    /// </summary>
    /// <param name="fixture">The seeded tenant databases.</param>
    [Trait("Category", "Integration")]
    public sealed class QueryexDiscoverTests(QueryPipelineDatabase fixture) : IClassFixture<QueryPipelineDatabase>
    {
        /// <summary>The widgets' resource.</summary>
        private const string Widgets = "fixture.Widget";

        /// <summary>The nodes' resource, a tree.</summary>
        private const string Nodes = "fixture.Node";

        /// <summary>The links' resource, a child query surface of the nodes with a second reference to them.</summary>
        private const string Links = "fixture.Link";

        /// <summary>The engine's code of a parameter used but never declared.</summary>
        private const string UndeclaredParameter = "QX3007";

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        // --- Authorize ---

        [Fact]
        public async Task An_unknown_root_is_not_found_and_measures_are_a_bad_request_before_any_round_trip()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int before = access.RoundTrips;
            NotFoundException missing = await Assert.ThrowsAsync<NotFoundException>(() => DiscoverAsync(scope, new QueryexDiscoverRequest("fixture.Nothing")));
            _ = await Assert.ThrowsAsync<BadRequestException>(() => DiscoverAsync(scope, new QueryexDiscoverRequest(Widgets, Measures: "sum(Price)")));

            Assert.Equal("fixture.Nothing", missing.Resource);
            Assert.Equal(before, access.RoundTrips);
        }

        // --- Discover and compile ---

        [Fact]
        public async Task The_diagnostics_are_the_ones_the_query_refuses_the_same_clauses_for()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));
            QueryRequest query = new() { Select = "Id, Nmae", Filters = ["Code = 'W1'", "Prce > @low"], OrderBy = "Cdoe" };

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            InvalidQueryException refused = await Assert.ThrowsAsync<InvalidQueryException>(
                () => scope.ServiceProvider.GetRequiredService<EntityService<Widget, int>>().QueryAsync(query, Token));
            QueryexDiscoverResult result = await DiscoverAsync(scope, new QueryexDiscoverRequest(Widgets, query.Select, query.Filters, OrderBy: query.OrderBy));

            // The query's own diagnostics, carried to the wire as discovery carries them; the parameter
            // the query refuses as undeclared is one discovery answers untyped instead.
            CompositionMap map = new([new Conjunct(ConjunctKind.UserFilter, 0), new Conjunct(ConjunctKind.UserFilter, 1)], SelectSent: true, OrderingSource.Sent);
            Assert.Contains(refused.Diagnostics, static diagnostic => diagnostic.Code == UndeclaredParameter);
            Assert.Equal(
                refused.Diagnostics.Where(static diagnostic => diagnostic.Code != UndeclaredParameter).Select(diagnostic => Text(QueryWireDiagnostics.ToWire(diagnostic, map))),
                result.Diagnostics.Select(Text));
            Assert.Equal(["filters[1]", "select", "orderBy"], result.Diagnostics.Select(static diagnostic => diagnostic.Clause));
        }

        [Fact]
        public async Task An_unconstrained_parameter_has_no_type_and_a_constrained_one_its_inferred_type()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            QueryexDiscoverResult result = await DiscoverAsync(scope, new QueryexDiscoverRequest(Widgets, Filters: ["@p = @q", "DueOn >= @From"]));

            Assert.Empty(result.Diagnostics);
            Assert.Equal(["p", "q", "From"], result.Parameters.Select(static parameter => parameter.Name));
            Assert.Equal([null, null, QueryexType.QxDate], result.Parameters.Select(static parameter => parameter.Type));
            Assert.Equal(["DueOn"], result.Parameters[2].ComparedWith!);
        }

        // --- Traverse ---

        [Fact]
        public async Task A_path_a_filtered_grant_refuses_is_a_path_not_reachable_diagnostic_at_the_path()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Links, AccessActions.Read, null), (Nodes, AccessActions.Read, "Code != 'N1'"));

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            QueryexDiscoverResult result = await DiscoverAsync(scope, new QueryexDiscoverRequest(Links, "Id", ["Id > 0", "descendantOf(OtherNode.Id, 2)"]));

            // The tree position the predicate reads, refused where the key path is written.
            QueryDiagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(("filters[1]", ValidationCodes.QueryPathNotReachable), (diagnostic.Clause, diagnostic.Code));
            Assert.Equal(new Dictionary<string, object?> { ["entity"] = Nodes, ["path"] = "OtherNode.Node" }, diagnostic.Arguments);
            QueryexPathInfo refused = Assert.Single(result.Paths, static path => !path.Reachable);
            Assert.Equal((diagnostic.Start, diagnostic.Length), (refused.Site.Start, refused.Site.Length));
            Assert.Equal(Nodes, refused.Entity);
        }

        // --- The permission profile ---

        [Fact]
        public async Task A_permission_filter_answers_the_codes_the_role_save_refuses_it_for()
        {
            const string Filter = "Nmae = @x and Price > 'a'";
            (_, string administrator) = await fixture.Connect.SeedAdministratorAsync("Administrator");

            // --- The role save's refusal of the filter ---
            List<object?> saved;
            await using (AsyncServiceScope scope = await fixture.Connect.SteppedUpScopeAsync(administrator))
            {
                Role role = new()
                {
                    Name = "Discovered",
                    Permissions = [new Permission { Resource = Widgets, Action = AccessActions.Read, Filter = Filter }],
                };
                ValidationException refused = await Assert.ThrowsAsync<ValidationException>(
                    () => scope.Roles().SaveAsync([role], new SaveOptions { ReturnEntities = false }, Token));
                saved = [.. refused.Errors.Where(static error => error.Code == AccessCodes.FilterInvalid).Select(static error => error.Arguments["code"])];
            }

            // --- The same text discovered as a permission filter ---
            await using AsyncServiceScope editor = await fixture.Connect.ScopeAsync(administrator);
            QueryexDiscoverResult result = await DiscoverAsync(editor, new QueryexDiscoverRequest(Widgets, Filters: [Filter], Profile: QueryexProfile.Permission));

            Assert.NotEmpty(saved);
            Assert.Equal(saved, result.Diagnostics.Select(static diagnostic => (object?)diagnostic.Code));
            Assert.All(result.Diagnostics, static diagnostic => Assert.Equal("filters[0]", diagnostic.Clause));
        }

        // --- The comparisons ---

        [Fact]
        public async Task The_comparisons_of_two_filters_carry_their_filters_index_and_their_literals_in_the_wires_encoding()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            QueryexDiscoverResult result = await DiscoverAsync(scope, new QueryexDiscoverRequest(
                Widgets,
                Filters: ["Price >= 10.50 and Code = 'W1'", "DueOn < '2026-08-01' and Kind != null"],
                Conjuncts: true));

            Assert.Equal(
                [
                    (0, "Price", ">=", "\"10.50\""),
                    (0, "Code", "=", "\"W1\""),
                    (1, "DueOn", "<", "\"2026-08-01\""),
                    (1, "Kind", "!=", null),
                ],
                result.Conjuncts!.Select(static conjunct => (conjunct.Filter, conjunct.Path!, conjunct.Op, conjunct.Value?.GetRawText())));
            Assert.Equal(QueryexType.QxDate, result.Conjuncts![2].Type);
        }

        // --- No round trip when warm ---

        [Fact]
        public async Task A_discovery_makes_no_round_trip_once_the_caller_is_connected()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));
            QueryexDiscoverRequest request = new(Widgets, "Id, Code, CreatedBy.Email", ["Nmae = @q"], Conjuncts: true);

            // The first scope warms the caller's connection.
            await using (AsyncServiceScope cold = await fixture.Connect.ScopeAsync(subject))
            {
                _ = await DiscoverAsync(cold, request);
            }

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int before = access.RoundTrips;
            QueryexDiscoverResult result = await DiscoverAsync(scope, request);

            Assert.Equal(0, access.RoundTrips - before);
            Assert.Equal(
                [ValidationCodes.QueryPathNotReachable],
                result.Diagnostics.Where(static diagnostic => diagnostic.Clause == "select").Select(static diagnostic => diagnostic.Code));
        }

        /// <summary>Invokes the endpoint in a caller's scope, through the action invoker.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="request">The request.</param>
        /// <returns>The answer.</returns>
        private static async Task<QueryexDiscoverResult> DiscoverAsync(AsyncServiceScope scope, QueryexDiscoverRequest request)
        {
            ApiActionDescriptor action = scope.ServiceProvider.GetRequiredService<IStackRegistry>().Services
                .Single(static service => service.Route == "queryex").Actions
                .Single(static candidate => candidate.Name == "discover");
            return Assert.IsType<QueryexDiscoverResult>(await scope.ServiceProvider.GetRequiredService<IApiActionInvoker>().InvokeAsync(action, request, Token));
        }

        /// <summary>A diagnostic as comparable text: its clause, location, code, span and arguments.</summary>
        /// <param name="diagnostic">The diagnostic.</param>
        /// <returns>The text.</returns>
        private static string Text(QueryDiagnostic diagnostic)
        {
            return JsonSerializer.Serialize(diagnostic, TellmaJson.Options);
        }
    }
}
