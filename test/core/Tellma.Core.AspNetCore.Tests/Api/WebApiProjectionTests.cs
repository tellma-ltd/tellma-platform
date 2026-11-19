// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Api;
using Tellma.Core.AspNetCore.Api;
using Tellma.Core.AspNetCore.Routing;
using Tellma.Core.AspNetCore.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Api
{
    /// <summary>
    ///     The web API's projection: the host maps exactly the route model's routes, each with its
    ///     metadata, binds every body shape to the service call it selects, and refuses an outdated
    ///     client, an oversized save and an over-long confirmation header before anything runs.
    /// </summary>
    public sealed class WebApiProjectionTests
    {
        /// <summary>The web surface's path for tenant 1.</summary>
        private const string Web = "/1/api/web/";

        [Fact]
        public async Task The_host_maps_exactly_the_route_model()
        {
            await using TellmaTestHost host = await StartAsync();
            IReadOnlyList<RouteContract> routes = host.Services.GetRequiredService<IRouteContracts>().Routes;
            List<RouteEndpoint> projected = Projected(host);

            // The core stacks and the suite's model alike: one endpoint per route, in mapping order.
            Assert.Equal([.. routes.Select(static route => route.Template)], [.. projected.Select(static endpoint => endpoint.Metadata.GetRequiredMetadata<ProjectedRouteMetadata>().Route.Template)]);
            Assert.All(projected, static endpoint =>
            {
                RouteContract route = endpoint.Metadata.GetRequiredMetadata<ProjectedRouteMetadata>().Route;
                Assert.Equal(route.Template["POST /{tenantId}".Length..], endpoint.RoutePattern.RawText!["/{tenantId:int:min(1)}".Length..]);
                Assert.Equal(["POST"], endpoint.Metadata.GetRequiredMetadata<HttpMethodMetadata>().HttpMethods);
            });
            Assert.Contains(routes, static route => route.Template == RouteContracts.TemplatePrefix + "centers/get-by-parent-ids");
            Assert.Contains(routes, static route => route.Template == RouteContracts.TemplatePrefix + "center-notes/query");
            Assert.Contains(routes, static route => route.Template == RouteContracts.TemplatePrefix + "currencies/all");
            Assert.Contains(routes, static route => route.Template == RouteContracts.TemplatePrefix + "inbox/summary");
        }

        /// <summary>The projection table: each route kind's grant, tenant facts, contract, timeout, polling and step-up metadata.</summary>
        /// <returns>The route, its securable (null for a member route), whether it mutates, whether a read-only tenant admits it, whether it runs long, whether it stamps no activity, whether it asks for step-up, and its kind.</returns>
        public static TheoryData<string, string?, bool, bool, bool, bool, bool, string> Table()
        {
            return new TheoryData<string, string?, bool, bool, bool, bool, bool, string>
            {
                { "centers/query", "pj.PjCenter:Read", false, false, false, false, false, "Stack" },
                { "centers/get-by-parent-ids", "pj.PjCenter:Read", false, false, false, false, false, "Stack" },
                { "centers/save", "pj.PjCenter:Save", true, false, false, false, false, "Stack" },
                { "centers/delete-with-descendants", "pj.PjCenter:Delete", true, false, false, false, false, "Stack" },
                { "centers/activate", "pj.PjCenter:Activate", true, false, false, false, false, "Stack" },
                { "centers/count-lines", "pj.PjCenter:CountLines", false, false, false, false, false, "Stack" },
                { "centers/transfer", "pj.PjCenter:Transfer", true, true, false, false, true, "Stack" },
                { "centers/rebuild", "pj.PjCenter:Rebuild", true, false, true, false, false, "Stack" },
                { "centers/summary", "pj.PjCenter:Read", false, false, false, false, false, "Stack" },
                { "centers/ping", null, false, false, false, false, false, "Stack" },
                { "center-notes/query", "pj.PjCenterNote:Read", false, false, false, false, false, "ChildQuery" },
                { "currencies/all", "pj.PjCurrency:Read", false, false, false, false, false, "Stack" },
                { "ledgers/delete-by-query", "pj.PjLedger:Delete", true, false, false, false, false, "Stack" },
                { "inbox/summary", null, false, false, false, true, false, "Service" },
                { "inbox/purge", "pj.Inbox:Purge", true, false, false, false, false, "Service" },
                { "users/save", "core.User:Save", true, false, false, false, true, "Stack" },
            };
        }

        [Theory]
        [MemberData(nameof(Table))]
        public async Task Each_route_carries_the_metadata_of_its_contract(
            string relative, string? securable, bool mutation, bool readOnly, bool isLong, bool polling, bool stepUp, string kind)
        {
            await using TellmaTestHost host = await StartAsync();
            RouteEndpoint endpoint = Assert.Single(Projected(host), endpoint => endpoint.RoutePattern.RawText!.EndsWith("/api/web/" + relative, StringComparison.Ordinal));
            EndpointMetadataCollection metadata = endpoint.Metadata;
            ProjectedRouteMetadata projected = metadata.GetRequiredMetadata<ProjectedRouteMetadata>();

            // --- The tenant facts and the one grant ---
            Assert.Equal(new TenantEndpointMetadata(TenantSurface.Web, mutation, readOnly), metadata.GetRequiredMetadata<TenantEndpointMetadata>());
            SecurableEndpointMetadata? required = metadata.GetMetadata<SecurableEndpointMetadata>();
            Assert.Equal(securable, required is null ? null : required.Resource + ":" + required.Action);
            Assert.Equal(securable is null, metadata.GetMetadata<MemberEndpointMetadata>() is not null);

            // --- The contract, the kind, the timeout, the activity stamp and the step-up ---
            Assert.Equal(projected.Route.Fingerprint, metadata.GetRequiredMetadata<ContractMetadata>().Fingerprint);
            Assert.Equal(kind, projected.Kind.ToString());
            Assert.Equal(isLong ? TellmaRequestTimeouts.Long : null, metadata.GetMetadata<RequestTimeoutAttribute>()?.PolicyName);
            Assert.Equal(polling, metadata.GetMetadata<NoActivityStampMetadata>() is not null);
            Assert.Equal(stepUp, metadata.GetMetadata<RequireAssuranceMetadata>() is not null);

            // --- What every projected endpoint states: API answers, the problems it may answer, the body ceiling ---
            Assert.NotNull(metadata.GetMetadata<IDisableCookieRedirectMetadata>());
            Assert.Null(metadata.GetMetadata<IAllowCookieRedirectMetadata>());
            Assert.Equal(
                [400, 401, 403, 404, 409, 412, 413, 422, 503],
                [.. metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Where(static produces => produces.StatusCode >= 400).Select(static produces => produces.StatusCode).Order()]);
            Assert.Equal(8 * 1024 * 1024, metadata.GetRequiredMetadata<IRequestSizeLimitMetadata>().MaxRequestBodySize);
        }

        [Fact]
        public async Task The_HTTP_JSON_options_follow_the_platform_rules()
        {
            await using TellmaTestHost host = await StartAsync();
            JsonSerializerOptions options = host.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;

            Assert.Equal(JsonNamingPolicy.CamelCase, options.PropertyNamingPolicy);
            Assert.Equal(TellmaJson.Options.MaxDepth, options.MaxDepth);
            Assert.Equal(JsonUnmappedMemberHandling.Skip, options.UnmappedMemberHandling);
            Assert.Equal(
                [.. TellmaJson.Options.Converters.Select(static converter => converter.GetType())],
                [.. options.Converters.Select(static converter => converter.GetType()).Take(TellmaJson.Options.Converters.Count)]);
        }

        [Fact]
        public async Task The_standard_operations_bind_their_bodies_to_the_service()
        {
            await using TellmaTestHost host = await StartAsync();
            string cookie = host.Cookie();

            // --- Reads: the extras become the details request; get-by-parent-ids answers a page of rows alone ---
            await PostAsync(host, cookie, "centers/get", /*lang=json,strict*/ """{"id":4,"include":["history"]}""", HttpStatusCode.OK);
            await PostAsync(host, cookie, "centers/get-by-ids", /*lang=json,strict*/ """{"ids":[3,1]}""", HttpStatusCode.OK);
            using JsonDocument page = await PostAsync(host, cookie, "centers/get-by-parent-ids", /*lang=json,strict*/ """{"parentIds":[1],"includeRoots":true}""", HttpStatusCode.OK);
            Assert.True(page.RootElement.TryGetProperty("rows", out _));
            Assert.False(page.RootElement.TryGetProperty("count", out _));
            await PostAsync(host, cookie, "currencies/all", "{}", HttpStatusCode.OK);

            // --- Writes: a web save, a delete by ids, a long-keyed delete and the tree's delete ---
            await PostAsync(host, cookie, "centers/save", /*lang=json,strict*/ """{"entities":[{"code":"C1","name":"One"}],"returnEntities":false,"concurrency":"Override"}""", HttpStatusCode.OK);
            await PostAsync(host, cookie, "centers/delete", /*lang=json,strict*/ """{"ids":[7]}""", HttpStatusCode.OK);
            await PostAsync(host, cookie, "ledgers/delete", /*lang=json,strict*/ """{"ids":[3000000000]}""", HttpStatusCode.OK);
            await PostAsync(host, cookie, "centers/delete-with-descendants", /*lang=json,strict*/ """{"ids":[2]}""", HttpStatusCode.OK);

            IReadOnlyList<PipelineCall> calls = host.Services.GetRequiredService<PipelineLog>().Calls;
            Assert.Equal(["get", "get-by-ids", "get-by-parent-ids", "all", "save", "delete", "delete", "delete-with-descendants"], [.. calls.Select(static call => call.Operation)]);
            Assert.Equal<object>([4], calls[0].Ids);
            Assert.Equal(["history"], Assert.IsType<DetailsRequest>(calls[0].Options).Include);
            Assert.Equal<object>([3, 1], calls[1].Ids);
            Assert.Same(DetailsRequest.None, calls[1].Options);
            Assert.Equal([1L], Assert.IsType<ParentIdsRequest>(calls[2].Arguments).ParentIds);

            SaveOptions save = Assert.IsType<SaveOptions>(calls[4].Options);
            Assert.Equal(SaveSource.Web, save.Source);
            Assert.False(save.ReturnEntities);
            Assert.Equal(ConcurrencyMode.Override, save.Concurrency);
            Assert.Equal("C1", Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<PjCenter>>(calls[4].Arguments)).Code);

            Assert.Equal<object>([7], calls[5].Ids);
            Assert.Null(calls[5].Arguments);
            Assert.Equal<object>([3000000000L], calls[6].Ids);
            Assert.Equal<object>([2], calls[7].Ids);
        }

        [Fact]
        public async Task Each_entity_action_shape_binds_its_body_to_the_action_call()
        {
            await using TellmaTestHost host = await StartAsync();
            string cookie = host.Cookie();

            // --- Entities results: EntityActionRequest and EntityActionRequest<T>, the built-in activation among them ---
            using JsonDocument archived = await PostAsync(host, cookie, "centers/archive", /*lang=json,strict*/ """{"ids":[1,2],"returnEntities":false,"include":["history"]}""", HttpStatusCode.OK);
            Assert.Equal("[1,2]", archived.RootElement.GetProperty("ids").GetRawText());
            await PostAsync(host, cookie, "centers/reprice", /*lang=json,strict*/ """{"ids":[3],"arguments":{"percent":5}}""", HttpStatusCode.OK);
            await PostAsync(host, cookie, "centers/activate", /*lang=json,strict*/ """{"ids":[4]}""", HttpStatusCode.OK);

            // --- Own results: IdsRequest, IdsRequest<T>, IdRequest and IdRequest<T> ---
            using JsonDocument counted = await PostAsync(host, cookie, "centers/count-lines", /*lang=json,strict*/ """{"ids":[5,6]}""", HttpStatusCode.OK);
            Assert.Equal(7, counted.RootElement.GetInt32());
            await PostAsync(host, cookie, "centers/score", /*lang=json,strict*/ """{"ids":[7],"arguments":{"weight":2}}""", HttpStatusCode.OK);
            using JsonDocument taken = await PostAsync(host, cookie, "centers/take-over", /*lang=json,strict*/ """{"id":8}""", HttpStatusCode.OK);
            Assert.Equal("done", taken.RootElement.GetString());
            await PostAsync(host, cookie, "centers/transfer", /*lang=json,strict*/ """{"id":9,"arguments":{"toParentId":1}}""", HttpStatusCode.OK, stepUp: true);

            IReadOnlyList<PipelineCall> calls = host.Services.GetRequiredService<PipelineLog>().Calls;
            Assert.Equal(
                ["action:archive", "action:reprice", "action:activate", "action:count-lines", "action:score", "action:take-over", "action:transfer"],
                [.. calls.Select(static call => call.Operation)]);

            ActionOptions archive = Assert.IsType<ActionOptions>(calls[0].Options);
            Assert.False(archive.ReturnEntities);
            Assert.Equal(["history"], archive.Details!.Include);
            Assert.Null(calls[0].Arguments);
            Assert.Equal(new PjRepriceArguments(5), calls[1].Arguments);
            Assert.True(Assert.IsType<ActionOptions>(calls[1].Options).ReturnEntities);
            Assert.Equal<object>([4], calls[2].Ids);
            Assert.Equal<object>([5, 6], calls[3].Ids);
            Assert.Equal(new PjScoreArguments(2), calls[4].Arguments);
            Assert.Equal<object>([8], calls[5].Ids);
            Assert.Equal<object>([9], calls[6].Ids);
            Assert.Equal(new PjTransferArguments(1), calls[6].Arguments);
        }

        [Theory]
        [InlineData("centers/delete", /*lang=json,strict*/ """{"ids":[1,3000000000]}""", "ids[1]")]
        [InlineData("centers/score", /*lang=json,strict*/ """{"ids":[3000000000],"arguments":{"weight":2}}""", "ids[0]")]
        [InlineData("centers/take-over", /*lang=json,strict*/ """{"id":-3000000000}""", "id")]
        [InlineData("centers/get", /*lang=json,strict*/ """{"id":3000000000}""", "id")]
        public async Task An_id_outside_an_int_key_is_a_bad_request_naming_its_path(string relative, string body, string path)
        {
            await using TellmaTestHost host = await StartAsync();

            using HttpResponseMessage response = await SendAsync(host, host.Cookie(), relative, body);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Problem problem = await Problem.ReadAsync(response);
            Assert.Equal("bad-request", problem.Code);
            Assert.Equal(path, problem.Argument("path"));
            Assert.Empty(host.Services.GetRequiredService<PipelineLog>().Calls);
        }

        [Fact]
        public async Task An_action_without_arguments_it_requires_is_a_bad_request()
        {
            await using TellmaTestHost host = await StartAsync();

            using HttpResponseMessage response = await SendAsync(host, host.Cookie(), "centers/score", /*lang=json,strict*/ """{"ids":[1]}""");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(host.Services.GetRequiredService<PipelineLog>().Calls);
        }

        [Fact]
        public async Task The_API_actions_child_queries_and_routed_services_reach_their_services()
        {
            await using TellmaTestHost host = await StartAsync();
            string cookie = host.Cookie();

            using JsonDocument summary = await PostAsync(host, cookie, "centers/summary", /*lang=json,strict*/ """{"language":"ar"}""", HttpStatusCode.OK);
            Assert.Equal("done", summary.RootElement.GetString());
            await PostAsync(host, cookie, "centers/ping", null, HttpStatusCode.NoContent);
            await PostAsync(host, cookie, "center-notes/query", /*lang=json,strict*/ """{"top":5}""", HttpStatusCode.OK);
            using JsonDocument inbox = await PostAsync(host, cookie, "inbox/summary", null, HttpStatusCode.OK);
            Assert.Equal(3, inbox.RootElement.GetProperty("unread").GetInt32());

            IReadOnlyList<PipelineCall> calls = host.Services.GetRequiredService<PipelineLog>().Calls;
            Assert.Equal(["api:summary", "api:ping", "child-query:" + ProjectionModel.Notes, "api:summary"], [.. calls.Select(static call => call.Operation)]);
            Assert.Equal(new PjSummaryRequest("ar"), calls[0].Arguments);
            Assert.Null(calls[1].Arguments);
            _ = Assert.IsType<QueryRequest>(calls[2].Arguments);
        }

        [Fact]
        public async Task A_job_hand_off_answers_202_with_its_polling_target()
        {
            await using TellmaTestHost host = await StartAsync();

            using JsonDocument accepted = await PostAsync(host, host.Cookie(), "centers/rebuild", /*lang=json,strict*/ """{"ids":[1]}""", HttpStatusCode.Accepted);

            Assert.Equal(42, accepted.RootElement.GetProperty("jobId").GetInt32());
        }

        [Fact]
        public async Task A_pre_serialized_document_is_written_as_its_bytes()
        {
            await using TellmaTestHost host = await StartAsync();

            using HttpResponseMessage response = await SendAsync(host, host.Cookie(), "inbox/document", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(PipelineLog.Document.ToArray(), await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task A_workbook_streams_as_an_attachment_and_is_disposed()
        {
            await using TellmaTestHost host = await StartAsync();

            using HttpResponseMessage response = await SendAsync(host, host.Cookie(), "inbox/export", null);

            // --- The workbook's media type, its file name in ASCII and in UTF-8, and no length ---
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(ActionAnswers.WorkbookContentType, response.Content.Headers.ContentType?.MediaType);
            System.Net.Http.Headers.ContentDispositionHeaderValue disposition = Assert.IsType<System.Net.Http.Headers.ContentDispositionHeaderValue>(response.Content.Headers.ContentDisposition);
            Assert.Equal("attachment", disposition.DispositionType);
            Assert.Equal("R_sum_-2026.xlsx", disposition.FileName?.Trim('"'));
            Assert.Equal(PipelineLog.WorkbookName, disposition.FileNameStar);
            Assert.False(response.Content.Headers.NonValidated.Contains("Content-Length"));

            // --- The bytes, then the outcome disposed once the response completed ---
            Assert.Equal(PipelineLog.WorkbookBytes.ToArray(), await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
            ObservedStream workbook = Assert.Single(host.Services.GetRequiredService<PipelineLog>().Workbooks);
            await workbook.Disposed.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }

        [Theory]
        [InlineData("centers/query", "pj.PjCenter", "query")]
        [InlineData("centers/count-lines", "pj.PjCenter", "count-lines")]
        [InlineData("inbox/summary", "inbox", "summary")]
        public async Task A_projected_request_tags_its_activity_with_its_resource_and_operation(string relative, string resource, string operation)
        {
            // Requests of other suites running alongside carry other trace ids.
            var trace = ActivityTraceId.CreateRandom();
            ConcurrentQueue<Activity> started = new();
            using ActivityListener listener = new()
            {
                ShouldListenTo = static source => source.Name == "Microsoft.AspNetCore",
                Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = activity =>
                {
                    if (activity.TraceId == trace)
                    {
                        started.Enqueue(activity);
                    }
                },
            };
            ActivitySource.AddActivityListener(listener);
            await using TellmaTestHost host = await StartAsync();

            using HttpRequestMessage request = Request(relative, relative.StartsWith("inbox", StringComparison.Ordinal) ? null : /*lang=json,strict*/ """{"ids":[1]}""", host.Cookie());
            request.Headers.Add("traceparent", $"00-{trace.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01");
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            // A stack route tags its stack's resource; a routed service without one falls back to its route.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Activity activity = Assert.Single(started);
            Assert.Equal(resource, activity.GetTagItem(ApiTelemetryNames.ResourceTag));
            Assert.Equal(operation, activity.GetTagItem(ApiTelemetryNames.OperationTag));
        }

        [Fact]
        public async Task Tenant_JSON_answers_carry_no_store()
        {
            await using TellmaTestHost host = await StartAsync();
            string cookie = host.Cookie();

            using HttpResponseMessage answer = await SendAsync(host, cookie, "centers/query", "{}");
            using HttpResponseMessage refused = await SendAsync(host, cookie, "centers/delete", /*lang=json,strict*/ """{"ids":[3000000000]}""");
            using HttpResponseMessage probe = await host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/probe", cookie), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.All([answer, refused, probe], static response => Assert.True(response.Headers.CacheControl?.NoStore));
        }

        [Fact]
        public async Task A_stated_contract_that_differs_is_refused_before_anything_runs()
        {
            CapturingLoggerProvider logs = new();
            await using TellmaTestHost host = await StartAsync(options => Capture(options, logs));
            using MetricCollector<long> rejections = new(
                host.Services.GetRequiredService<IMeterFactory>(), ApiTelemetryNames.MeterName, ApiTelemetryNames.RequestsRejected);
            string cookie = host.Cookie();
            string fingerprint = host.Services.GetRequiredService<IRouteContracts>().For(RouteContracts.TemplatePrefix + "centers/save")!.Fingerprint;

            // --- A stale client: 412, nothing executed, even a body that would not bind ---
            using HttpResponseMessage stale = await SendAsync(host, cookie, "centers/save", "not json", contract: "AAAAAAAAAAAA");
            Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
            Assert.Equal("client-outdated", (await Problem.ReadAsync(stale)).Code);
            Assert.True(stale.Headers.CacheControl?.NoStore);
            Assert.Empty(host.Services.GetRequiredService<PipelineLog>().Calls);

            // --- Counted by reason and logged by route ---
            CollectedMeasurement<long> rejection = Assert.Single(rejections.GetMeasurementSnapshot());
            Assert.Equal(1, rejection.Value);
            Assert.Equal("client_outdated", rejection.Tags[ApiTelemetryNames.ReasonTag]);
            CapturedEvent logged = Assert.Single(logs.Events, static captured => captured.Message.Contains("was refused before its endpoint ran", StringComparison.Ordinal));
            Assert.Equal(LogLevel.Information, logged.Level);
            Assert.Contains("/api/web/centers/save", logged.Message, StringComparison.Ordinal);

            // --- Before the tenant: a tenant that does not exist is never looked up, so the refusal is still 412 ---
            using HttpRequestMessage unknown = TellmaTestHost.SpaRequest(HttpMethod.Post, "/99/api/web/centers/save", cookie, /*lang=json,strict*/ """{"entities":[]}""");
            unknown.Headers.Add(TellmaHeaders.Contract, "AAAAAAAAAAAA");
            using HttpResponseMessage beforeTenant = await host.Client.SendAsync(unknown, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.PreconditionFailed, beforeTenant.StatusCode);

            // --- The matching contract and no contract at all both pass ---
            using HttpResponseMessage current = await SendAsync(host, cookie, "centers/save", /*lang=json,strict*/ """{"entities":[]}""", contract: fingerprint);
            using HttpResponseMessage tooling = await SendAsync(host, cookie, "centers/save", /*lang=json,strict*/ """{"entities":[]}""");
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
            Assert.Equal(HttpStatusCode.OK, tooling.StatusCode);
            Assert.Equal(2, host.Services.GetRequiredService<PipelineLog>().Calls.Count);
        }

        [Fact]
        public async Task Seventeen_confirmation_keys_are_a_bad_request()
        {
            await using TellmaTestHost host = await StartAsync();
            string keys = string.Join(", ", Enumerable.Range(1, 17).Select(static i => "Pj.Key" + i));

            using HttpResponseMessage response = await SendAsync(host, host.Cookie(), "centers/delete", /*lang=json,strict*/ """{"ids":[1]}""", confirm: keys);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("bad-request", (await Problem.ReadAsync(response)).Code);
            Assert.Empty(host.Services.GetRequiredService<PipelineLog>().Calls);
        }

        [Theory]
        [InlineData("Pj.Back dated")]
        [InlineData("Pj.Bäckdated")]
        public async Task A_confirmation_key_outside_visible_ASCII_is_a_bad_request(string key)
        {
            await using TellmaTestHost host = await StartAsync();

            using HttpRequestMessage request = Request("centers/delete", /*lang=json,strict*/ """{"ids":[1]}""", host.Cookie());
            _ = request.Headers.TryAddWithoutValidation(TellmaHeaders.Confirm, System.Text.Encoding.Latin1.GetString(System.Text.Encoding.Latin1.GetBytes(key)));
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(host.Services.GetRequiredService<PipelineLog>().Calls);
        }

        [Theory]
        [InlineData(100, HttpStatusCode.OK)]
        [InlineData(101, HttpStatusCode.BadRequest)]
        public async Task A_confirmation_key_is_at_most_100_characters(int length, HttpStatusCode status)
        {
            await using TellmaTestHost host = await StartAsync();
            string key = "Pj." + new string('K', length - 3);

            using HttpResponseMessage response = await SendAsync(host, host.Cookie(), "centers/delete", /*lang=json,strict*/ """{"ids":[1]}""", confirm: key);

            Assert.Equal(status, response.StatusCode);
            Assert.Equal(status == HttpStatusCode.OK ? 1 : 0, host.Services.GetRequiredService<PipelineLog>().Calls.Count);
        }

        [Fact]
        public async Task Sixteen_confirmation_keys_seed_the_declared_ones_before_the_call()
        {
            await using TellmaTestHost host = await StartAsync();
            string keys = string.Join(",", ProjectionModel.ConfirmationKeys.Concat(Enumerable.Range(1, 14).Select(static i => "Pj.Undeclared" + i)));

            await PostAsync(host, host.Cookie(), "centers/delete", /*lang=json,strict*/ """{"ids":[1]}""", HttpStatusCode.OK, confirm: keys);

            PipelineCall call = Assert.Single(host.Services.GetRequiredService<PipelineLog>().Calls);
            Assert.Equal(ProjectionModel.ConfirmationKeys.Order(StringComparer.Ordinal), call.Confirmed.Order(StringComparer.Ordinal));
        }

        [Fact]
        public async Task A_save_above_the_web_ceiling_is_refused_before_the_service_runs()
        {
            await using TellmaTestHost host = await StartAsync(static options => options.Settings["Tellma:Api:MaxEntitiesPerSave"] = "2");
            string cookie = host.Cookie();

            using HttpResponseMessage refused = await SendAsync(host, cookie, "centers/save", /*lang=json,strict*/ """{"entities":[{"code":"A","name":"A"},{"code":"B","name":"B"},{"code":"C","name":"C"}]}""");
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, refused.StatusCode);
            Problem problem = await Problem.ReadAsync(refused);
            Assert.Equal("limit-exceeded", problem.Code);
            Assert.Equal(LimitNames.Entities, problem.Argument("limit"));
            Assert.Equal("3", problem.Argument("actual"));
            Assert.Equal("2", problem.Argument("maximum"));
            Assert.Empty(host.Services.GetRequiredService<PipelineLog>().Calls);

            // At the ceiling, the save runs.
            await PostAsync(host, cookie, "centers/save", /*lang=json,strict*/ """{"entities":[{"code":"A","name":"A"},{"code":"B","name":"B"}]}""", HttpStatusCode.OK);
            Assert.Single(host.Services.GetRequiredService<PipelineLog>().Calls);
        }

        [Fact]
        public async Task A_stack_ceiling_below_the_web_ceiling_wins()
        {
            // The web ceiling is the default 1,000; the ledger's own is 2.
            await using TellmaTestHost host = await StartAsync();
            string cookie = host.Cookie();

            using HttpResponseMessage refused = await SendAsync(host, cookie, "ledgers/save", /*lang=json,strict*/ """{"entities":[{"name":"A"},{"name":"B"},{"name":"C"}]}""");
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, refused.StatusCode);
            Problem problem = await Problem.ReadAsync(refused);
            Assert.Equal("3", problem.Argument("actual"));
            Assert.Equal(PjLedger.LedgerMaxSaveCount.ToString(System.Globalization.CultureInfo.InvariantCulture), problem.Argument("maximum"));
            Assert.Empty(host.Services.GetRequiredService<PipelineLog>().Calls);

            await PostAsync(host, cookie, "ledgers/save", /*lang=json,strict*/ """{"entities":[{"name":"A"},{"name":"B"}]}""", HttpStatusCode.OK);
            Assert.Single(host.Services.GetRequiredService<PipelineLog>().Calls);
        }

        [Fact]
        public async Task The_projected_child_query_surfaces_are_logged_once_at_startup()
        {
            CapturingLoggerProvider logs = new();
            await using TellmaTestHost host = await StartAsync(options => Capture(options, logs));

            CapturedEvent logged = Assert.Single(logs.Events, static captured => captured.Message.Contains("child query surfaces", StringComparison.Ordinal));
            Assert.Equal(LogLevel.Information, logged.Level);
            // Every projected surface is counted, the core stacks' beside the model's.
            int surfaces = Projected(host).Count(static endpoint => endpoint.Metadata.GetRequiredMetadata<ProjectedRouteMetadata>().Kind == ProjectedRouteKind.ChildQuery);
            Assert.StartsWith($"The web surface projects {surfaces} child query surfaces: ", logged.Message, StringComparison.Ordinal);
            Assert.Contains("pj.PjCenterNote (center-notes, owner pj.PjCenter)", logged.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_success_without_its_witness_is_an_internal_error()
        {
            await using TellmaTestHost host = await StartAsync();
            PipelineLog log = host.Services.GetRequiredService<PipelineLog>();
            string cookie = host.Cookie();

            // The same call answers once its service checks access, and not when it skips the check.
            await PostAsync(host, cookie, "centers/query", "{}", HttpStatusCode.OK);
            log.SkipWitness = true;
            using HttpResponseMessage response = await SendAsync(host, cookie, "centers/query", "{}");

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("internal", (await Problem.ReadAsync(response)).Code);
            Assert.Equal(2, log.Calls.Count);
        }

        [Fact]
        public async Task A_member_route_is_witnessed_by_the_connection()
        {
            await using TellmaTestHost host = await StartAsync();
            host.Services.GetRequiredService<PipelineLog>().SkipWitness = true;

            await PostAsync(host, host.Cookie(), "centers/ping", null, HttpStatusCode.NoContent);
        }

        [Fact]
        public void A_child_query_surface_on_a_reserved_word_fails_startup()
        {
            TellmaCompositionException exception = Assert.Throws<TellmaCompositionException>(() => TellmaTestHost.Build(static options =>
            {
                options.Compose = static tellma => tellma.AddFeature(ProjectionModel.Feature(static contribution => contribution.Entity<PjReservingLedger>()));
                options.ConfigureServices = StandIns;
            }));

            CompositionProblem problem = Assert.Single(exception.Problems);
            Assert.Equal(TellmaEndpointAudit.CheckName, problem.Source);
            Assert.StartsWith("The child query surface at /{tenantId}/settings takes 'settings'", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_writable_property_JSON_ignores_fails_the_realised_gate()
        {
            (WebApplication app, _, _) = TellmaTestHost.Build(static options =>
            {
                options.Compose = static tellma => tellma.AddFeature(ProjectionModel.Feature(static contribution => contribution.Entity<PjHiddenCenter>()));
                options.ConfigureServices = StandIns;
            });
            await using (app)
            {
                TellmaCompositionException exception = await Assert.ThrowsAsync<TellmaCompositionException>(() => app.StartAsync(TestContext.Current.CancellationToken));

                CompositionProblem problem = Assert.Single(exception.Problems, static problem => problem.Problem.Contains("[JsonIgnore]", StringComparison.Ordinal));
                Assert.Equal(TellmaEndpointAudit.CheckName, problem.Source);
                Assert.StartsWith("The mapped property PjHiddenCenter.Name is [JsonIgnore] but editable", problem.Problem, StringComparison.Ordinal);
            }
        }

        [Fact]
        public async Task A_result_type_without_a_JSON_contract_fails_the_realised_gate()
        {
            (WebApplication app, _, _) = TellmaTestHost.Build(static options =>
            {
                options.Compose = ProjectionModel.Compose;
                options.ConfigureServices = static services =>
                {
                    StandIns(services);
                    _ = services.ConfigureHttpJsonOptions(static json => json.SerializerOptions.Converters.Add(new RefusingConverterFactory(typeof(PjInboxSummary))));
                };
            });
            await using (app)
            {
                TellmaCompositionException exception = await Assert.ThrowsAsync<TellmaCompositionException>(() => app.StartAsync(TestContext.Current.CancellationToken));

                CompositionProblem problem = Assert.Single(exception.Problems, static problem => problem.Problem.Contains("JSON contract", StringComparison.Ordinal));
                Assert.StartsWith("The web surface's type PjInboxSummary has no JSON contract", problem.Problem, StringComparison.Ordinal);
            }
        }

        [Fact]
        public async Task A_result_member_JSON_never_writes_fails_the_realised_gate()
        {
            (WebApplication app, _, _) = TellmaTestHost.Build(static options =>
            {
                options.Compose = static tellma => tellma.AddFeature(ProjectionModel.Feature(static contribution => contribution.ApiService<PjOddsService>()));
                options.ConfigureServices = StandIns;
            });
            await using (app)
            {
                TellmaCompositionException exception = await Assert.ThrowsAsync<TellmaCompositionException>(() => app.StartAsync(TestContext.Current.CancellationToken));

                // A multi-dimensional array, a type and a delegate: each resolves a contract that writes no value.
                List<CompositionProblem> problems = [.. exception.Problems.Where(static problem => problem.Problem.Contains("has no JSON contract", StringComparison.Ordinal))];
                Assert.Equal(3, problems.Count);
                Assert.All(problems, static problem => Assert.Equal(TellmaEndpointAudit.CheckName, problem.Source));
                Assert.Contains(problems, static problem => problem.Problem.Contains("[,]", StringComparison.Ordinal));
                Assert.Contains(problems, static problem => problem.Problem.Contains("type Type ", StringComparison.Ordinal));
                Assert.Contains(problems, static problem => problem.Problem.Contains("Func<", StringComparison.Ordinal));
            }
        }

        [Fact]
        public async Task A_member_JSON_ignores_needs_no_contract()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options =>
            {
                options.Compose = static tellma => tellma.AddFeature(ProjectionModel.Feature(static contribution => contribution.Entity<PjArchive>()));
                options.ConfigureServices = StandIns;
            });

            // The ignored member's type really has no contract, yet the host started and maps the archive.
            JsonSerializerOptions options = host.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
            _ = Assert.Throws<InvalidOperationException>(() => options.GetTypeInfo(typeof(PjLegacy)));
            Assert.Contains(Projected(host), static endpoint => endpoint.RoutePattern.RawText!.EndsWith("/api/web/archives/save", StringComparison.Ordinal));
        }

        [Fact]
        public void A_pre_serialized_document_outside_the_platform_fails_startup()
        {
            Type foreign = ForeignService();

            TellmaCompositionException exception = Assert.Throws<TellmaCompositionException>(() => TellmaTestHost.Build(options =>
            {
                options.Compose = tellma => tellma.AddFeature(ProjectionModel.Feature(contribution =>
                    typeof(FeatureContribution).GetMethod(nameof(FeatureContribution.ApiService))!.MakeGenericMethod(foreign).Invoke(contribution, null)));
                options.ConfigureServices = StandIns;
            }));

            CompositionProblem problem = Assert.Single(exception.Problems);
            Assert.Equal(TellmaEndpointAudit.CheckName, problem.Source);
            Assert.Contains("foreign/document", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("declared in Acme.Foreign, outside the platform", problem.Problem, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A routed service from an assembly outside the platform whose one action answers the
        ///     platform's pre-serialized document shape, <c>(byte[] Json, string WireTag)</c>.
        /// </summary>
        /// <returns>The service type.</returns>
        private static Type ForeignService()
        {
            // --- An assembly whose name no platform assembly carries ---
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Acme.Foreign"), AssemblyBuilderAccess.Run);
            ModuleBuilder module = assembly.DefineDynamicModule("Acme.Foreign");
            TypeBuilder service = module.DefineType("Acme.Foreign.DocumentService", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class);
            service.SetCustomAttribute(new CustomAttributeBuilder(typeof(ApiRouteAttribute).GetConstructor([typeof(string)])!, ["foreign"]));
            _ = service.DefineDefaultConstructor(MethodAttributes.Public);

            // --- [ApiAction("document", MemberOnly = true, Mutation = false, Idempotent = true)], answering null ---
            MethodBuilder method = service.DefineMethod("DocumentAsync", MethodAttributes.Public, typeof(Task<(byte[] Json, string WireTag)>), Type.EmptyTypes);
            method.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(ApiActionAttribute).GetConstructor([typeof(string)])!,
                ["document"],
                [
                    typeof(ApiActionAttribute).GetProperty(nameof(ApiActionAttribute.MemberOnly))!,
                    typeof(ApiActionAttribute).GetProperty(nameof(ApiActionAttribute.Mutation))!,
                    typeof(ApiActionAttribute).GetProperty(nameof(ApiActionAttribute.Idempotent))!,
                ],
                [true, false, true]));
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);

            return service.CreateType();
        }

        /// <summary>Starts a host composing the suite's model over the recording stand-ins.</summary>
        /// <param name="configure">More changes to the host.</param>
        /// <returns>The started host.</returns>
        private static Task<TellmaTestHost> StartAsync(Action<TestHostOptions>? configure = null)
        {
            return TellmaTestHost.StartAsync(options =>
            {
                options.Compose = ProjectionModel.Compose;
                options.ConfigureServices = StandIns;
                configure?.Invoke(options);
            });
        }

        /// <summary>Adds a capturing logger provider to a host's services, after the stand-ins.</summary>
        /// <param name="options">The host's options, whose services already hold the stand-ins.</param>
        /// <param name="logs">The provider that keeps every event.</param>
        private static void Capture(TestHostOptions options, CapturingLoggerProvider logs)
        {
            Action<IServiceCollection>? prior = options.ConfigureServices;
            options.ConfigureServices = services =>
            {
                prior?.Invoke(services);
                _ = services.AddSingleton<ILoggerProvider>(logs);
            };
        }

        /// <summary>Registers the recording pipeline, invoker and child query service, and the log they share.</summary>
        /// <param name="services">The host's services.</param>
        internal static void StandIns(IServiceCollection services)
        {
            _ = services.AddSingleton<PipelineLog>();
            _ = services.AddScoped<RecordingPipelineFactory>();
            services.Replace(ServiceDescriptor.Scoped<IApiActionInvoker, RecordingApiActionInvoker>());
            services.Replace(ServiceDescriptor.Scoped<IChildQueryService, RecordingChildQueryService>());
        }

        /// <summary>The projected endpoints, in mapping order.</summary>
        /// <param name="host">The host.</param>
        /// <returns>The endpoints carrying a route model entry.</returns>
        private static List<RouteEndpoint> Projected(TellmaTestHost host)
        {
            return [.. host.Services.GetRequiredService<MappedEndpoints>().Endpoints()
                .OfType<RouteEndpoint>()
                .Where(static endpoint => endpoint.Metadata.GetMetadata<ProjectedRouteMetadata>() is not null)];
        }

        /// <summary>A web request to a projected route, as the SPA sends it.</summary>
        /// <param name="relative">The route under the web surface.</param>
        /// <param name="body">The JSON body; null for none.</param>
        /// <param name="cookie">The session cookie.</param>
        /// <returns>The request.</returns>
        private static HttpRequestMessage Request(string relative, string? body, string cookie)
        {
            HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, Web + relative, cookie, body);
            if (body is not null && !body.StartsWith('{'))
            {
                request.Content!.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            }

            return request;
        }

        /// <summary>Sends a web request to a projected route.</summary>
        /// <param name="host">The host.</param>
        /// <param name="cookie">The session cookie.</param>
        /// <param name="relative">The route under the web surface.</param>
        /// <param name="body">The JSON body; null for none.</param>
        /// <param name="contract">The <c>Tellma-Contract</c> header; null for none.</param>
        /// <param name="confirm">The <c>Tellma-Confirm</c> header; null for none.</param>
        /// <param name="stepUp">Whether the session stepped up to the host's bar, for a sensitive securable.</param>
        /// <returns>The response.</returns>
        private static async Task<HttpResponseMessage> SendAsync(
            TellmaTestHost host, string cookie, string relative, string? body, string? contract = null, string? confirm = null, bool stepUp = false)
        {
            using HttpRequestMessage request = Request(relative, body, stepUp ? host.Cookie(acr: host.Services.GetRequiredService<StepUpBar>().Requirement.Acr, authTime: host.Time.GetUtcNow()) : cookie);
            if (contract is not null)
            {
                request.Headers.Add(TellmaHeaders.Contract, contract);
            }

            if (confirm is not null)
            {
                request.Headers.Add(TellmaHeaders.Confirm, confirm);
            }

            return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        /// <summary>Sends a web request and reads its JSON answer, asserting its status.</summary>
        /// <param name="host">The host.</param>
        /// <param name="cookie">The session cookie.</param>
        /// <param name="relative">The route under the web surface.</param>
        /// <param name="body">The JSON body; null for none.</param>
        /// <param name="status">The expected status.</param>
        /// <param name="confirm">The <c>Tellma-Confirm</c> header; null for none.</param>
        /// <param name="stepUp">Whether the session stepped up to the host's bar, for a sensitive securable.</param>
        /// <returns>The answer's JSON; an empty object for an answer without a body.</returns>
        private static async Task<JsonDocument> PostAsync(
            TellmaTestHost host, string cookie, string relative, string? body, HttpStatusCode status, string? confirm = null, bool stepUp = false)
        {
            using HttpResponseMessage response = await SendAsync(host, cookie, relative, body, confirm: confirm, stepUp: stepUp);
            string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(status == response.StatusCode, $"{relative} answered {(int)response.StatusCode}: {text}");
            return JsonDocument.Parse(text.Length == 0 ? "{}" : text);
        }

        /// <summary>A converter factory that claims one type and cannot build its converter, as a broken converter would.</summary>
        /// <param name="refused">The type it claims.</param>
        private sealed class RefusingConverterFactory(Type refused) : JsonConverterFactory
        {
            /// <inheritdoc />
            public override bool CanConvert(Type typeToConvert)
            {
                return typeToConvert == refused;
            }

            /// <inheritdoc />
            public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotSupportedException($"{typeToConvert.Name} has no converter.");
            }
        }
    }
}
