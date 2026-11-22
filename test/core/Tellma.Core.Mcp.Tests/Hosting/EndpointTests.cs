// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Net;
using System.Text;
using Tellma.Testing.WebHost;

namespace Tellma.Core.Mcp.Tests.Hosting
{
    /// <summary>
    ///     The endpoint composed into the web host: the bearer events inert on every other surface, a
    ///     token valid only where its one audience points, the front door's refusals as error results
    ///     that name no tenant, and a distribution tool's failures mapped as the web surface maps them.
    /// </summary>
    /// <param name="fixture">The host.</param>
    public sealed class EndpointTests(McpHost fixture) : IClassFixture<McpHost>
    {
        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task The_public_api_s_challenge_and_tokens_are_untouched_by_the_mcp_events()
        {
            // --- No token: the scheme's own challenge, naming no MCP metadata ---
            using (HttpResponseMessage bare = await fixture.Host.Client.GetAsync(new Uri("/1/api/v1/probe", UriKind.Relative), Token))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, bare.StatusCode);
                Assert.DoesNotContain("resource_metadata", bare.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
            }

            // --- The public API's token, whose audience the MCP rule would refuse, passes there ---
            using HttpRequestMessage request = TellmaTestHost.BearerRequest(HttpMethod.Get, "/1/api/v1/probe", TellmaTestHost.UserToken());
            using HttpResponseMessage admitted = await fixture.Host.Client.SendAsync(request, Token);
            Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        }

        [Fact]
        public async Task A_token_naming_a_second_audience_is_refused()
        {
            string twoTenants = McpHost.Token(new[] { McpHost.Resource(1), McpHost.Resource(2) });
            string tenantAndOrigin = McpHost.Token(new[] { McpHost.Resource(1), TellmaTestHost.PublicOrigin });

            foreach (string token in (string[])[twoTenants, tenantAndOrigin])
            {
                using HttpResponseMessage refused = await InitializeAsync(1, token);
                Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
                Assert.StartsWith("Bearer error=\"invalid_token\"", refused.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
            }

            // --- The tenant's own resource alone is admitted ---
            using HttpResponseMessage admitted = await InitializeAsync(1, McpHost.Token(McpHost.Resource(1)));
            Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        }

        [Fact]
        public async Task A_member_is_served_and_learns_the_tenant_s_name()
        {
            await using McpClient client = await fixture.ConnectAsync(1, McpHost.Token(McpHost.Resource(1)));

            Assert.Equal("Tenant 1 (Acme ERP)", client.ServerInfo.Title);
            ListToolsResult list = await client.ListToolsAsync(new ListToolsRequestParams(), Token);
            Assert.Equal(
                [McpToolNames.Whoami, McpToolNames.Describe, McpToolNames.Query, McpToolNames.Aggregate, McpToolNames.Get, "faulty_bad_request", "faulty_bug", "faulty_forbidden"],
                list.Tools.Select(static tool => tool.Name));
            CallToolResult catalogue = await client.CallToolAsync(McpToolNames.Describe, cancellationToken: Token);
            Assert.True(catalogue.IsError != true, Text(catalogue));
        }

        [Fact]
        public async Task A_door_slower_than_the_sdk_s_default_discover_probe_is_waited_out_in_one_request()
        {
            int before = McpHost.ColdDoorRuns.Reads;

            await using McpClient client = await fixture.ConnectAsync(1, McpHost.Token(McpHost.Resource(1), subject: McpHost.ColdDoor));

            // An abandoned probe would have run the door again for the initialize that follows it, or failed the connect.
            Assert.Equal("Tenant 1 (Acme ERP)", client.ServerInfo.Title);
            Assert.Equal(before + 1, McpHost.ColdDoorRuns.Reads);
        }

        [Fact]
        public async Task A_stranger_s_valid_token_learns_no_tenant_and_every_call_is_refused()
        {
            await using McpClient client = await fixture.ConnectAsync(1, McpHost.Token(McpHost.Resource(1), subject: TellmaTestHost.Stranger));

            Assert.Equal("Acme ERP", client.ServerInfo.Title);
            Assert.DoesNotContain("Tenant 1", client.ServerInstructions, StringComparison.Ordinal);
            CallToolResult refused = await client.CallToolAsync(McpToolNames.Describe, cancellationToken: Token);
            Assert.True(refused.IsError);
            Assert.Contains("\"status\":404", Text(refused), StringComparison.Ordinal);
            Assert.DoesNotContain("Tenant 1", Text(refused), StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_deactivated_member_is_refused_by_whoami_s_fresh_connect()
        {
            const string subject = "sub-deactivated";
            _ = fixture.Host.Users.Deactivated.TryAdd((1, subject), true);
            await using McpClient client = await fixture.ConnectAsync(1, McpHost.Token(McpHost.Resource(1), subject: subject));

            CallToolResult refused = await client.CallToolAsync(McpToolNames.Whoami, cancellationToken: Token);

            Assert.True(refused.IsError);
            Assert.Contains("\"status\":404", Text(refused), StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_service_account_is_refused_as_individual_required()
        {
            await using McpClient client = await fixture.ConnectAsync(1, McpHost.Token(McpHost.Resource(1), subject: "svc-agent", service: true));

            Assert.Equal("Acme ERP", client.ServerInfo.Title);
            CallToolResult refused = await client.CallToolAsync(McpToolNames.Describe, cancellationToken: Token);
            Assert.True(refused.IsError);
            Assert.Contains("\"code\":\"individual-required\"", Text(refused), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("faulty_forbidden", "\"status\":403")]
        [InlineData("faulty_bad_request", "\"code\":\"bad-request\"")]
        [InlineData("faulty_bug", "\"status\":500")]
        public async Task A_distribution_tool_s_failure_is_an_error_result_mapped_as_the_web_surface_maps_it(string tool, string expected)
        {
            await using McpClient client = await fixture.ConnectAsync(1, McpHost.Token(McpHost.Resource(1)));

            CallToolResult result = await client.CallToolAsync(tool, cancellationToken: Token);

            Assert.True(result.IsError);
            Assert.Contains(expected, Text(result), StringComparison.Ordinal);
            Assert.DoesNotContain("secret internals", Text(result), StringComparison.Ordinal);
        }

        /// <summary>A result's text block.</summary>
        /// <param name="result">The result.</param>
        /// <returns>The text.</returns>
        private static string Text(CallToolResult result)
        {
            return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        }

        /// <summary>Posts an initialize request to a tenant's endpoint.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="token">The bearer token.</param>
        /// <returns>The response.</returns>
        private async Task<HttpResponseMessage> InitializeAsync(int tenantId, string token)
        {
            using HttpRequestMessage request = TellmaTestHost.BearerRequest(HttpMethod.Post, $"/{tenantId}/mcp", token);
            request.Content = new StringContent(
                /*lang=json,strict*/ """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"tests","version":"1.0"}}}""",
                Encoding.UTF8,
                "application/json");
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");
            return await fixture.Host.Client.SendAsync(request, Token);
        }
    }
}
