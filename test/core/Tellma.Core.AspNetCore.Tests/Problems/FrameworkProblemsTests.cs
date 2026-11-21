// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Testing.WebHost;

namespace Tellma.Core.AspNetCore.Tests.Problems
{
    /// <summary>
    ///     The refusals the framework raises around a handler (a body it cannot read, a verb or a
    ///     route it does not have, a status a component set without a body) and a credential the
    ///     surface does not take answer the same problem body as a handler's exception.
    /// </summary>
    public sealed class FrameworkProblemsTests
    {
        [Fact]
        public async Task A_body_that_is_not_JSON_is_a_bad_request_for_malformed_JSON()
        {
            await using TellmaTestHost host = await StartAsync();

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/api/web/echo", host.Cookie(), "{\"name\": ");
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Problem problem = await Problem.ReadAsync(response);
            Assert.Equal("bad-request", problem.Code);
            Assert.Equal("malformed-json", problem.Argument("reason"));

            // The framework's own message names the parameter and its reader: never on the wire.
            string text = problem.Document.GetRawText();
            Assert.DoesNotContain("parameter", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(nameof(EchoBody), text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_body_of_another_media_type_is_unsupported_media()
        {
            await using TellmaTestHost host = await StartAsync();

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/api/web/echo", host.Cookie());
            request.Content = new StringContent("name=x", Encoding.UTF8, "text/plain");
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
            Assert.Equal("unsupported-media", (await Problem.ReadAsync(response)).Code);
        }

        [Fact]
        public async Task A_body_over_its_ceiling_names_the_body_limit()
        {
            await using TellmaTestHost host = await ProblemMappingTests.StartAsync();

            using HttpResponseMessage response = await ProblemMappingTests.ThrowAsync(host, "body-too-large");

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
            Problem problem = await Problem.ReadAsync(response);
            Assert.Equal("limit-exceeded", problem.Code);
            Assert.Equal(LimitNames.Body, problem.Argument("limit"));
            Assert.False(problem.Document.TryGetProperty("detail", out _));
        }

        [Theory]
        [InlineData(400, "bad-request")]
        [InlineData(401, "unauthenticated")]
        [InlineData(403, "forbidden")]
        [InlineData(404, "not-found")]
        [InlineData(405, "method-not-allowed")]
        [InlineData(413, "limit-exceeded")]
        [InlineData(429, "rate-limited")]
        [InlineData(500, "internal")]
        [InlineData(504, "timeout")]
        public async Task A_bare_status_a_component_set_answers_its_problem(int status, string code)
        {
            await using TellmaTestHost host = await StartAsync();

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/status/" + status, host.Cookie());
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(status, (int)response.StatusCode);
            Problem problem = await Problem.ReadAsync(response);
            Assert.Equal(code, problem.Code);
            Assert.Equal(Assert.Single(response.Headers.GetValues("Tellma-Trace-Id")), problem.Instance);

            // A detail is rendered text, never a message pattern its arguments failed to fill.
            if (problem.Document.TryGetProperty("detail", out JsonElement detail))
            {
                Assert.DoesNotContain('{', detail.GetString()!);
            }
        }

        [Fact]
        public async Task A_bare_forbidden_renders_the_general_refusal()
        {
            await using TellmaTestHost host = await StartAsync();

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/status/403", host.Cookie());
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Problem problem = await Problem.ReadAsync(response);
            Assert.Equal("forbidden", problem.Code);
            Assert.Equal("Ask your administrator for access.", problem.Document.GetProperty("detail").GetString());
            Assert.False(problem.Document.TryGetProperty("arguments", out _));
        }

        [Fact]
        public async Task A_route_the_tenant_surface_does_not_have_is_route_not_found()
        {
            await using TellmaTestHost host = await StartAsync();

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/no-such-route", host.Cookie());
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("route-not-found", (await Problem.ReadAsync(response)).Code);
        }

        [Fact]
        public async Task A_verb_the_route_does_not_take_is_method_not_allowed_naming_the_verbs_it_takes()
        {
            await using TellmaTestHost host = await StartAsync();

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Delete, "/1/api/web/probe", host.Cookie());
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
            Assert.Equal("method-not-allowed", (await Problem.ReadAsync(response)).Code);
            Assert.Equal(["GET", "POST"], response.Content.Headers.Allow.Order(StringComparer.Ordinal));
        }

        [Fact]
        public async Task A_wrong_verb_answers_the_same_for_a_tenant_that_does_not_exist()
        {
            await using TellmaTestHost host = await StartAsync();

            // The answer reads the path alone: it never tells whether the tenant exists.
            using HttpRequestMessage existing = TellmaTestHost.SpaRequest(HttpMethod.Delete, "/1/api/web/probe", cookie: null);
            using HttpRequestMessage absent = TellmaTestHost.SpaRequest(HttpMethod.Delete, "/99/api/web/probe", cookie: null);
            using HttpResponseMessage first = await host.Client.SendAsync(existing, TestContext.Current.CancellationToken);
            using HttpResponseMessage second = await host.Client.SendAsync(absent, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.MethodNotAllowed, first.StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, second.StatusCode);
        }

        [Fact]
        public async Task A_path_no_route_serves_under_any_verb_stays_a_route_miss()
        {
            await using TellmaTestHost host = await StartAsync();

            // The status route's constraint refuses a word, so its verb does not make this a 405.
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/api/web/status/word", host.Cookie());
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("route-not-found", (await Problem.ReadAsync(response)).Code);
        }

        [Fact]
        public async Task A_token_on_the_web_surface_is_an_unsupported_credential_with_or_without_a_session()
        {
            await using TellmaTestHost host = await StartAsync();
            string token = TellmaTestHost.UserToken();

            // --- A token alone ---
            using HttpRequestMessage bare = TellmaTestHost.BearerRequest(HttpMethod.Get, "/1/api/web/probe", token);
            using HttpResponseMessage refused = await host.Client.SendAsync(bare, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
            Assert.Equal("unsupported-credential", (await Problem.ReadAsync(refused)).Code);

            // --- A token beside a valid session: the token still disqualifies the request ---
            using HttpRequestMessage both = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/probe", host.Cookie());
            both.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage mixed = await host.Client.SendAsync(both, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, mixed.StatusCode);
            Assert.Equal("unsupported-credential", (await Problem.ReadAsync(mixed)).Code);
        }

        [Fact]
        public async Task A_web_request_without_a_session_is_unauthenticated()
        {
            await using TellmaTestHost host = await StartAsync();

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/probe", cookie: null);
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("unauthenticated", (await Problem.ReadAsync(response)).Code);
        }

        /// <summary>Starts a host whose web surface maps a JSON-body endpoint and one that answers a bare status.</summary>
        /// <returns>The started host.</returns>
        private static Task<TellmaTestHost> StartAsync()
        {
            return TellmaTestHost.StartAsync(static options => options.Map = static (_, endpoints) => Map(endpoints));
        }

        /// <summary>Maps the suite's endpoints on the web surface.</summary>
        /// <param name="endpoints">The platform's groups.</param>
        private static void Map(TellmaEndpoints endpoints)
        {
            _ = endpoints.Web.MapPost("/echo", static (EchoBody body) => Results.Ok(body)).WithMutation(true).AllowMember();
            _ = endpoints.Web.MapGet("/status/{status:int}", static (int status) => Results.StatusCode(status)).WithMutation(false).AllowMember();
        }

        /// <summary>The JSON body the echo endpoint reads.</summary>
        /// <param name="Name">A value.</param>
        internal sealed record EchoBody(string Name);
    }
}
