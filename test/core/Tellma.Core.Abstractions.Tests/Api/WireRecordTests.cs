// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Abstractions.Tests.Api
{
    /// <summary>The request and result records bind the wire's bodies and write the wire's shapes.</summary>
    public class WireRecordTests
    {
        [Fact]
        public void A_query_request_defaults_to_the_first_page_with_nothing_extra()
        {
            QueryRequest request = new();

            Assert.Equal(0, request.Skip);
            Assert.Null(request.Take);
            Assert.False(request.IncludeCount || request.IncludeAncestors || request.IncludeInactive || request.Aggregate);
            Assert.Equal(/*lang=json,strict*/ """{"skip":0,"includeCount":false,"includeAncestors":false,"includeInactive":false,"aggregate":false}""", Write(request));
        }

        [Fact]
        public void A_query_request_binds_the_body_a_grid_sends()
        {
            const string Body = /*lang=json,strict*/ """
                { "select": "Id,Code,Name,Parent.Name,IsActive", "filters": ["Name contains @q"], "orderBy": "Code",
                  "skip": 0, "take": 50, "arguments": { "q": "east" }, "includeCount": true, "includeAncestors": true }
                """;

            QueryRequest request = Read<QueryRequest>(Body);

            Assert.Equal("Id,Code,Name,Parent.Name,IsActive", request.Select);
            Assert.Equal(["Name contains @q"], request.Filters!);
            Assert.Equal("Code", request.OrderBy);
            Assert.Equal(50, request.Take);
            Assert.Equal("east", request.Arguments!["q"].GetString());
            Assert.True(request.IncludeCount && request.IncludeAncestors);
            Assert.Null(request.NamedFilters);
        }

        [Fact]
        public void Ids_arrive_as_numbers_or_strings()
        {
            IdsRequest request = Read<IdsRequest>(/*lang=json,strict*/ """{ "ids": [5, "9007199254740993"] }""");

            Assert.Equal([5L, 9_007_199_254_740_993L], request.Ids);
        }

        [Theory]
        [InlineData(/*lang=json,strict*/ "{}")]
        [InlineData(/*lang=json,strict*/ """{ "ids": null }""")]
        public void A_missing_id_list_binds_empty_so_the_count_rule_refuses_it(string body)
        {
            // An empty list is the id ceiling's refusal (limit-exceeded), never a null the binder trips on.
            Assert.Empty(Read<IdsRequest>(body).Ids);
            Assert.Empty(Read<IdsRequest<CreditArguments>>(body).Ids);
            Assert.Empty(Read<GetByIdsRequest>(body).Ids);
            Assert.Empty(Read<EntityActionRequest>(body).Ids);
            Assert.Empty(Read<EntityActionRequest<CreditArguments>>(body).Ids);
            Assert.Empty(new IdsRequest(null!).Ids);
        }

        [Fact]
        public void A_generated_record_writes_its_long_members_through_the_platform_converter()
        {
            // The records resolve through the generated context, which must still honour the
            // platform's scalar converters.
            Assert.Equal(/*lang=json,strict*/ """{"ids":["5","9007199254740993"]}""", Write(new IdsRequest([5, 9_007_199_254_740_993])));
            Assert.Equal(/*lang=json,strict*/ """{"id":"17"}""", Write(new GetRequest(17, null)));
        }

        [Fact]
        public void A_get_request_binds_its_id_and_extras()
        {
            GetRequest request = Read<GetRequest>(/*lang=json,strict*/ """{ "id": "17", "include": ["permissions"] }""");

            Assert.Equal(17L, request.Id);
            Assert.Equal(["permissions"], request.Include!);
        }

        [Fact]
        public void Get_by_parent_ids_takes_its_defaults_when_members_are_absent()
        {
            ParentIdsRequest request = Read<ParentIdsRequest>(/*lang=json,strict*/ """{ "parentIds": [1, 2] }""");

            Assert.Equal([1L, 2L], request.ParentIds!);
            Assert.False(request.IncludeInactive);
            Assert.False(request.IncludeRoots);
            Assert.Null(request.OrderBy);
            Assert.Null(request.NamedFilters);
        }

        [Fact]
        public void An_entity_action_request_returns_the_rows_by_default()
        {
            EntityActionRequest request = Read<EntityActionRequest>(/*lang=json,strict*/ """{ "ids": [3] }""");

            Assert.True(request.ReturnEntities);
            Assert.Equal([3L], request.Ids);
            Assert.Empty(new EntityActionRequest().Ids);
        }

        [Fact]
        public void An_action_request_carries_typed_arguments()
        {
            const string Body = /*lang=json,strict*/ """{ "ids": [3], "returnEntities": false, "arguments": { "creditLimit": "250000.0000" } }""";

            EntityActionRequest<CreditArguments> rows = Read<EntityActionRequest<CreditArguments>>(Body);
            IdsRequest<CreditArguments> ids = Read<IdsRequest<CreditArguments>>(Body);
            IdRequest<CreditArguments> one = Read<IdRequest<CreditArguments>>(/*lang=json,strict*/ """{ "id": 3, "arguments": { "creditLimit": 1.5 } }""");

            Assert.Equal((250000.0000m, false), (rows.Arguments.CreditLimit, rows.ReturnEntities));
            Assert.Equal(250000.0000m, ids.Arguments.CreditLimit);
            Assert.Equal((3L, 1.5m), (one.Id, one.Arguments.CreditLimit));
        }

        [Fact]
        public void A_delete_by_query_request_binds_its_expected_count()
        {
            DeleteByQueryRequest request = Read<DeleteByQueryRequest>(/*lang=json,strict*/ """{ "filter": "IsActive = false", "expectedCount": 12 }""");

            Assert.Equal(("IsActive = false", 12), (request.Filter, request.ExpectedCount));
            Assert.Null(request.Arguments);
        }

        [Fact]
        public void A_job_hand_off_omits_a_missing_resource()
        {
            Assert.Equal(/*lang=json,strict*/ """{"jobId":5}""", Write(new JobAccepted(5, null)));
            Assert.Equal(/*lang=json,strict*/ """{"jobId":5,"resourceId":9}""", Write(new JobAccepted(5, 9)));
        }

        [Fact]
        public void An_affected_count_and_a_securable_summary_have_their_wire_shapes()
        {
            Assert.Equal(/*lang=json,strict*/ """{"count":3}""", Write(new AffectedResult(3)));
            Assert.Equal(/*lang=json,strict*/ """{"resource":"gl.Center","action":"Read","hasFilter":true}""", Write(new SecurableSummary("gl.Center", "Read", true)));
        }

        [Fact]
        public void A_query_diagnostic_carries_its_clause_and_the_compiler_fields()
        {
            QueryDiagnostic diagnostic = new("filters[1]", "QX3001", 4, 6, null, new Dictionary<string, object?> { ["property"] = "Nmae" });

            Assert.Equal(
                /*lang=json,strict*/ """{"clause":"filters[1]","code":"QX3001","start":4,"length":6,"arguments":{"property":"Nmae"}}""",
                Write(diagnostic));
        }

        /// <summary>Serializes a value with the platform's options.</summary>
        /// <typeparam name="T">The value's type.</typeparam>
        /// <param name="value">The value.</param>
        /// <returns>The JSON text.</returns>
        private static string Write<T>(T value)
        {
            return JsonSerializer.Serialize(value, TellmaJson.Options);
        }

        /// <summary>Deserializes a value with the platform's options.</summary>
        /// <typeparam name="T">The value's type.</typeparam>
        /// <param name="json">The JSON text.</param>
        /// <returns>The value.</returns>
        private static T Read<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, TellmaJson.Options)!;
        }

        /// <summary>An action's arguments record.</summary>
        /// <param name="CreditLimit">A decimal argument.</param>
        private sealed record CreditArguments(decimal CreditLimit);
    }
}
