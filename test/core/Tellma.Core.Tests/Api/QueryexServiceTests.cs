// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;
using Tellma.Core.Crud.Query;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Crud.Query;

namespace Tellma.Core.Tests.Api
{
    /// <summary>
    ///     The Queryex authoring endpoint over the query model, through the action invoker: the
    ///     diagnostics a query would refuse the clauses for, located in the request's clauses; the
    ///     parameters with their confirmed, inferred, unconstrained and conflicting types and the
    ///     entity they stand for; the paths with the entity each reads and whether the caller may read
    ///     through it; the filters' comparisons in the wire's encoding; a permission filter judged as
    ///     the role save judges it; the admission's refusals; and no batch at all.
    /// </summary>
    public sealed class QueryexServiceTests : IAsyncDisposable
    {
        /// <summary>The longest permission filter the rig admits, so a test reaches the length rule.</summary>
        private const int MaxFilterLength = 24;

        /// <summary>The query model, with a short permission filter length.</summary>
        private readonly QueryRig _rig = new(
            QueryModel.Compose,
            static services => services.PostConfigure<AccessOptions>(static options => options.MaxFilterLength = MaxFilterLength));

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _rig.DisposeAsync();
        }

        // --- The query profile's diagnostics ---

        [Fact]
        public async Task The_diagnostics_are_the_ones_the_query_refuses_the_same_clauses_for_each_at_its_requests_clause()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            QueryRequest query = new() { Select = "Id, Nmae", Filters = [" ", "Prce > 1"], OrderBy = "Cdoe" };

            // --- What the query refuses, carried to the wire as discovery carries it ---
            await using (AsyncServiceScope scope = _rig.Scope())
            {
                InvalidQueryException refused = await Assert.ThrowsAsync<InvalidQueryException>(() => Operation(scope).RunAsync(Surface(QueryModel.Accounts), query, null, Token));
                CompositionMap map = new([new Conjunct(ConjunctKind.UserFilter, 1)], SelectSent: true, OrderingSource.Sent);
                QueryexDiscoverResult result = await DiscoverAsync(new QueryexDiscoverRequest(QueryModel.Accounts, query.Select, query.Filters, OrderBy: query.OrderBy));

                Assert.Equal(
                    refused.Diagnostics.Select(diagnostic => Text(QueryWireDiagnostics.ToWire(diagnostic, map))),
                    result.Diagnostics.Select(Text));
            }

            // Each at its clause, in the order the engine binds the clauses: the filter at its index in
            // the request, then the select and the ordering, each one text of its own.
            QueryexDiscoverResult discovered = await DiscoverAsync(new QueryexDiscoverRequest(QueryModel.Accounts, query.Select, query.Filters, OrderBy: query.OrderBy));
            Assert.Equal(
                [("filters[1]", null, 0, 4), ("select", null, 4, 4), ("orderBy", null, 0, 4)],
                discovered.Diagnostics.Select(static diagnostic => (diagnostic.Clause, diagnostic.Location, diagnostic.Start, diagnostic.Length)));
        }

        [Fact]
        public async Task Clauses_that_compile_answer_no_diagnostic_and_text_that_does_not_parse_still_answers_its_paths()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));

            QueryexDiscoverResult clean = await DiscoverAsync(new QueryexDiscoverRequest(QueryModel.Accounts, "Id, Code", ["Price > 1"], OrderBy: "Code"));
            QueryexDiscoverResult broken = await DiscoverAsync(new QueryexDiscoverRequest(QueryModel.Accounts, "Id, Code", ["Price > 1", "Code = "]));

            Assert.Empty(clean.Diagnostics);
            Assert.Equal(["Price", "Id", "Code", "Code"], clean.Paths.Select(static path => string.Join('.', path.Segments)));
            Assert.All(broken.Diagnostics, static diagnostic => Assert.Equal("filters[1]", diagnostic.Clause));
            Assert.NotEmpty(broken.Diagnostics);
            Assert.Contains(broken.Paths, static path => path.Segments.SequenceEqual(["Price"]));
        }

        [Fact]
        public async Task A_having_clause_is_located_at_the_having_clause()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));

            QueryexDiscoverResult result = await DiscoverAsync(new QueryexDiscoverRequest(QueryModel.Accounts, "Code, sum(Price)", Having: "sum(Prce) > 0", Aggregate: true));

            QueryDiagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(("having", null, 4, 4), (diagnostic.Clause, diagnostic.Location, diagnostic.Start, diagnostic.Length));
            Assert.True(result.UsesAggregation);
        }

        // --- The parameters ---

        [Fact]
        public async Task A_parameter_takes_its_inferred_type_and_the_entity_the_key_it_meets_identifies()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));

            QueryexDiscoverResult result = await DiscoverAsync(new QueryexDiscoverRequest(QueryModel.Accounts, Filters: ["CreatedBy.Id = @user and Price >= @From"]));

            Assert.Empty(result.Diagnostics);
            QueryexParameterInfo user = result.Parameters.Single(static parameter => parameter.Name == "user");
            QueryexParameterInfo from = result.Parameters.Single(static parameter => parameter.Name == "From");
            Assert.Equal((QueryexType.QxNumeric, (string?)CoreResources.User), (user.Type, user.Entity));
            Assert.Equal(["CreatedBy", "Id"], user.ComparedWith!);
            Assert.Equal(QueryexType.QxNumeric, from.Type);
            Assert.Null(from.Entity);
            Assert.Equal(new QueryexSite("filters[0]", null, 34, 5), Assert.Single(from.Sites));
            Assert.Empty(from.Conflicts);
        }

        [Fact]
        public async Task A_foreign_key_identifies_its_navigations_target_and_a_path_names_the_entity_its_last_segment_is_read_on()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));

            QueryexDiscoverResult result = await DiscoverAsync(new QueryexDiscoverRequest(QueryModel.Lines, Filters: ["AccountId = @account and Account.Code != ''"]));

            Assert.Equal(QueryModel.Accounts, Assert.Single(result.Parameters).Entity);
            Assert.Equal(
                [("AccountId", QueryModel.Lines, true), ("Account.Code", QueryModel.Accounts, true)],
                result.Paths.Select(static path => (string.Join('.', path.Segments), path.Entity, path.Reachable)));
        }

        [Fact]
        public async Task An_unconstrained_parameter_has_no_type_and_its_undeclared_report_is_dropped()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));

            QueryexDiscoverResult result = await DiscoverAsync(new QueryexDiscoverRequest(QueryModel.Accounts, Filters: ["@p = @q"]));

            Assert.Empty(result.Diagnostics);
            Assert.Equal(["p", "q"], result.Parameters.Select(static parameter => parameter.Name));
            Assert.All(result.Parameters, static parameter => Assert.Null(parameter.Type));
        }

        [Fact]
        public async Task A_confirmed_type_wins_and_conflicting_uses_leave_a_parameter_untyped_with_their_types()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));

            QueryexDiscoverResult result = await DiscoverAsync(new QueryexDiscoverRequest(
                QueryModel.Accounts,
                Filters: ["@p = @q", "Price = @c or Code = @c"],
                Parameters: [new QueryexParameterInput("P", QueryexType.QxDate)]));

            QueryexParameterInfo confirmed = result.Parameters.Single(static parameter => parameter.Name == "p");
            QueryexParameterInfo conflicting = result.Parameters.Single(static parameter => parameter.Name == "c");
            Assert.Equal(QueryexType.QxDate, confirmed.Type);
            Assert.Null(conflicting.Type);
            Assert.NotEmpty(conflicting.Conflicts);
            Assert.Equal(conflicting.Conflicts.Distinct(), conflicting.Conflicts);
        }

        // --- The traversal rule ---

        [Fact]
        public async Task A_path_the_callers_filtered_grant_refuses_is_unreachable_and_a_path_not_reachable_diagnostic_at_the_path()
        {
            _rig.Grant((QueryModel.Tickets, AccessActions.Read, null), (QueryModel.Counters, AccessActions.Read, "Number > 0"));

            QueryexDiscoverResult result = await DiscoverAsync(new QueryexDiscoverRequest(QueryModel.Tickets, "Id, Counter.Label", ["Counter.Number > 1"]));

            QueryDiagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(("select", null, 4, 13, ValidationCodes.QueryPathNotReachable), (diagnostic.Clause, diagnostic.Location, diagnostic.Start, diagnostic.Length, diagnostic.Code));
            Assert.Equal(new Dictionary<string, object?> { ["entity"] = QueryModel.Counters, ["path"] = "Counter.Label" }, diagnostic.Arguments);
            Assert.Equal(
                [("Counter.Number", true), ("Id", true), ("Counter.Label", false)],
                result.Paths.Select(static path => (string.Join('.', path.Segments), path.Reachable)));
        }

        [Fact]
        public async Task A_path_into_a_sensitive_entity_is_reachable_and_asks_for_no_step_up()
        {
            _rig.Grant((QueryModel.Tickets, AccessActions.Read, null), (QueryModel.Counters, AccessActions.Read, null));
            _rig.Assurance = null;

            QueryexDiscoverResult result = await DiscoverAsync(new QueryexDiscoverRequest(QueryModel.Tickets, "Id, Counter.Label"));

            Assert.Empty(result.Diagnostics);
            Assert.All(result.Paths, static path => Assert.True(path.Reachable));
        }

        // --- The comparisons ---

        [Fact]
        public async Task The_comparisons_of_every_filter_are_answered_with_their_filters_index_and_their_literal_in_the_wires_encoding()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            string[] filters = ["Price >= 10.50 and Code = 'A1'", " ", "IsActive = false and Name != null"];

            QueryexDiscoverResult asked = await DiscoverAsync(new QueryexDiscoverRequest(QueryModel.Accounts, Filters: filters, Conjuncts: true));
            QueryexDiscoverResult unasked = await DiscoverAsync(new QueryexDiscoverRequest(QueryModel.Accounts, Filters: filters));

            Assert.Equal(
                [
                    (0, "Price", ">=", "\"10.50\"", QueryexType.QxNumeric),
                    (0, "Code", "=", "\"A1\"", QueryexType.QxString),
                    (2, "IsActive", "=", "false", QueryexType.QxBool),
                    (2, "Name", "!=", null, QueryexType.QxString),
                ],
                asked.Conjuncts!.Select(static conjunct => (conjunct.Filter, conjunct.Path!, conjunct.Op, conjunct.Value?.GetRawText(), conjunct.Type)));
            Assert.Null(unasked.Conjuncts);
        }

        // --- The permission profile ---

        [Fact]
        public async Task A_permission_filter_answers_what_the_role_save_refuses_it_for_its_parameter_included()
        {
            QueryexDiscoverResult result = await DiscoverAsync(new QueryexDiscoverRequest(QueryModel.Accounts, Filters: ["Nmae = @x"], Profile: QueryexProfile.Permission));

            // The role save's own judgement of the same text: one refusal per diagnostic, each with its code.
            QueryexEngine engine = _rig.Services.GetRequiredService<QueryexEngine>();
            PermissionFilterVerdict verdict = PermissionFilterRules.Check("Nmae = @x", _rig.Schema.FindEntity(QueryModel.Accounts), _rig.Schema, QueryexLanguage.Version, MaxFilterLength, engine.Validate);
            Assert.False(verdict.Valid);
            Assert.Equal(
                verdict.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Span.Start, diagnostic.Span.Length)),
                result.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Start, diagnostic.Length)));
            Assert.All(result.Diagnostics, static diagnostic => Assert.Equal("filters[0]", diagnostic.Clause));
            Assert.All(result.Diagnostics, static diagnostic => Assert.Null(diagnostic.Location));
            Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "QX3007");
            Assert.All(result.Paths, static path => Assert.True(path.Reachable));
        }

        [Fact]
        public async Task A_permission_filter_past_the_length_is_refused_at_the_part_past_it()
        {
            const string Filter = "Code = 'abcdefghijklmnopqrstuvwxyz'";

            QueryexDiscoverResult result = await DiscoverAsync(new QueryexDiscoverRequest(QueryModel.Accounts, Filters: [Filter], Profile: QueryexProfile.Permission));

            QueryDiagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal((AccessCodes.FilterTooLong, MaxFilterLength, Filter.Length - MaxFilterLength), (diagnostic.Code, diagnostic.Start, diagnostic.Length));
            Assert.Equal(MaxFilterLength, diagnostic.Arguments["maxLength"]);
        }

        [Fact]
        public async Task A_valid_permission_filter_answers_no_diagnostic_and_no_filter_answers_nothing()
        {
            QueryexDiscoverResult valid = await DiscoverAsync(new QueryexDiscoverRequest(QueryModel.Accounts, Filters: ["CreatedBy.Id = me()"], Profile: QueryexProfile.Permission));
            QueryexDiscoverResult blank = await DiscoverAsync(new QueryexDiscoverRequest(QueryModel.Accounts, Filters: [" "], Profile: QueryexProfile.Permission, Conjuncts: true));

            Assert.Empty(valid.Diagnostics);
            Assert.Equal(["CreatedBy.Id"], valid.Paths.Select(static path => string.Join('.', path.Segments)));
            Assert.Equal((0, 0, 0, 0), (blank.Diagnostics.Count, blank.Parameters.Count, blank.Paths.Count, blank.Conjuncts!.Count));
        }

        // --- The admission ---

        [Fact]
        public async Task An_unknown_root_is_not_found()
        {
            NotFoundException missing = await Assert.ThrowsAsync<NotFoundException>(() => DiscoverAsync(new QueryexDiscoverRequest("qm.QmNothing")));

            Assert.Equal("qm.QmNothing", missing.Resource);
        }

        public static TheoryData<QueryexDiscoverRequest> Malformed => new(
            new QueryexDiscoverRequest(" "),
            new QueryexDiscoverRequest(QueryModel.Accounts, Measures: "sum(Price)"),
            new QueryexDiscoverRequest(QueryModel.Accounts, Measures: "sum(Price)", Aggregate: true),
            new QueryexDiscoverRequest(QueryModel.Accounts, "Code", Having: "Code != ''"),
            new QueryexDiscoverRequest(QueryModel.Accounts, Parameters: [new("p", QueryexType.QxDate), new("P", QueryexType.QxString)]),
            new QueryexDiscoverRequest(QueryModel.Accounts, Parameters: [new("p", QueryexType.QxGeography)]),
            new QueryexDiscoverRequest(QueryModel.Accounts, Parameters: [new("p", (QueryexType)99)]),
            new QueryexDiscoverRequest(QueryModel.Accounts, Parameters: [new(" ", QueryexType.QxDate)]),
            new QueryexDiscoverRequest(QueryModel.Accounts, Parameters: [null!]),
            new QueryexDiscoverRequest(QueryModel.Accounts, Profile: (QueryexProfile)7),
            new QueryexDiscoverRequest(QueryModel.Accounts, Filters: ["Code != ''", "Price > 0"], Profile: QueryexProfile.Permission),
            new QueryexDiscoverRequest(QueryModel.Accounts, "Code", ["Code != ''"], Profile: QueryexProfile.Permission),
            new QueryexDiscoverRequest(QueryModel.Accounts, Filters: ["Code != @x"], Parameters: [new("x", QueryexType.QxString)], Profile: QueryexProfile.Permission));

        [Theory]
        [MemberData(nameof(Malformed))]
        public async Task A_malformed_request_is_a_bad_request_before_any_round_trip(QueryexDiscoverRequest request)
        {
            _ = await Assert.ThrowsAsync<BadRequestException>(() => DiscoverAsync(request));
            Assert.Empty(_rig.Script.Batches);
        }

        [Fact]
        public async Task A_discovery_composes_no_batch()
        {
            _rig.Grant((QueryModel.Tickets, AccessActions.Read, null), (QueryModel.Counters, AccessActions.Read, "Number > 0"));

            _ = await DiscoverAsync(new QueryexDiscoverRequest(QueryModel.Tickets, "Id, Counter.Label", ["Nmae = 1"], Conjuncts: true));

            Assert.Empty(_rig.Script.Batches);
        }

        /// <summary>Invokes the endpoint in a scope of the rig's caller, through the action invoker.</summary>
        /// <param name="request">The request.</param>
        /// <returns>The answer.</returns>
        private async Task<QueryexDiscoverResult> DiscoverAsync(QueryexDiscoverRequest request)
        {
            await using AsyncServiceScope scope = _rig.Scope();
            ApiActionDescriptor action = scope.ServiceProvider.GetRequiredService<IStackRegistry>().Services
                .Single(static service => service.Route == "queryex").Actions
                .Single(static candidate => candidate.Name == "discover");
            return Assert.IsType<QueryexDiscoverResult>(await scope.ServiceProvider.GetRequiredService<IApiActionInvoker>().InvokeAsync(action, request, Token));
        }

        /// <summary>A scope's query operation.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The operation.</returns>
        private static QueryOperation Operation(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<QueryOperation>();
        }

        /// <summary>A stack's or a child surface's query surface.</summary>
        /// <param name="resource">The resource.</param>
        /// <returns>The surface.</returns>
        private QuerySurface Surface(string resource)
        {
            return _rig.Surfaces.Stack(resource) ?? _rig.Surfaces.Child(resource)!;
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
