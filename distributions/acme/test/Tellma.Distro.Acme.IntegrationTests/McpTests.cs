// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tellma.Core.AspNetCore;
using Tellma.Distro.Acme.IntegrationTests.Infrastructure;
using Tellma.Distro.Acme.TestSupport;
using Tellma.Identity.TestSupport;

namespace Tellma.Distro.Acme.IntegrationTests
{
    /// <summary>
    ///     The tenant MCP server over Acme's seed tenants: an SDK client holding a token of a test
    ///     issuer the host trusts beside its engine lists the platform's tools and runs each read tool
    ///     against the live tenant; the endpoint refuses a token for another audience or another
    ///     tenant, one naming a second audience, an expired one, one without the API scope, an
    ///     unlisted browser origin and a GET; and a valid token of a service account, a non-member or a
    ///     deactivated member learns no tenant and has every call refused.
    /// </summary>
    /// <param name="sql">The SQL Server the migrator and the host run against.</param>
    [Trait("Category", "Integration")]
    public sealed class McpTests(SqlServerFixture sql)
    {
        /// <summary>Acme's live seed tenant.</summary>
        private const int Live = 1;

        /// <summary>Acme's sandbox seed tenant.</summary>
        private const int Sandbox = 2;

        /// <summary>The subject the migrator binds Acme's Development administrator to.</summary>
        private const string AdministratorSubject = "00000000-0000-0000-0000-000000000001";

        /// <summary>The issuer of the test's tokens, which the host trusts beside its engine.</summary>
        private const string TestIssuer = "https://mcp-tests.test";

        /// <summary>Why a test is skipped until the composition connects a tenant request to its user.</summary>
        private const string NeedsUserConnection =
            "Needs the user connection initializer, which refuses a caller who is not one of the tenant's users.";

        /// <summary>Why a test is skipped until the composition serves the administrator.</summary>
        private const string NeedsUserConnectionAndBootstrap =
            "Needs the user connection initializer and the administrator bootstrap provisioning step, so a tool call "
            + "reaches the tenant's data as the seeded administrator.";

        /// <summary>The key the test issuer signs with.</summary>
        private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "mcp-tests" };

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact(Skip = NeedsUserConnectionAndBootstrap, SkipUnless = nameof(PlatformSeams.ServesTheAdministrator), SkipType = typeof(PlatformSeams))]
        public async Task An_sdk_client_lists_the_tools_and_runs_each_read_tool_against_the_tenant()
        {
            AcmeMigrator acme = new(sql, "acme_mcp_read");
            _ = await acme.MigrateAsync();
            await using AcmeHost host = await StartAsync(acme);
            await JoinAsync(host);
            await using McpClient client = await ConnectAsync(host, Live, UserToken(Resource(Live)));

            // --- The server names the tenant, and the list is the platform's five read tools with its cache hints ---
            Assert.Equal("tellma-tenant", client.ServerInfo.Name);
            Assert.StartsWith("Acme (", client.ServerInfo.Title, StringComparison.Ordinal);
            Assert.Contains("a live tenant", client.ServerInstructions, StringComparison.Ordinal);
            ListToolsResult list = await client.ListToolsAsync(new ListToolsRequestParams(), Token);
            Assert.Equal(["tellma_whoami", "tellma_describe", "tellma_query", "tellma_aggregate", "tellma_get"], list.Tools.Select(static tool => tool.Name));
            Assert.Equal(TimeSpan.FromMinutes(5), list.TimeToLive);
            Assert.All(list.Tools, static tool => Assert.Equal((true, false), (tool.Annotations?.ReadOnlyHint, tool.Annotations?.OpenWorldHint)));
            Assert.All(list.Tools, static tool => Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean()));

            // --- whoami: structured content, the tenant and the administrator's grants ---
            CallToolResult whoami = await CallAsync(client, "tellma_whoami", []);
            JsonElement structured = whoami.StructuredContent!.Value;
            Assert.Equal((Live, "Live"), (structured.GetProperty("tenant").GetProperty("id").GetInt32(), structured.GetProperty("tenant").GetProperty("category").GetString()));
            Assert.Contains(structured.GetProperty("access").EnumerateArray(), static entry => entry.GetProperty("entity").GetString() == "gl.Center");
            Assert.Equal(structured.GetRawText(), Text(whoami));

            // --- describe: the catalogue narrowed by search, then one entity ---
            string catalogue = Text(await CallAsync(client, "tellma_describe", new() { ["search"] = "invoice" }));
            Assert.Contains("acme.SalesInvoice", catalogue, StringComparison.Ordinal);
            Assert.DoesNotContain("gl.Center ", catalogue, StringComparison.Ordinal);
            string center = Text(await CallAsync(client, "tellma_describe", new() { ["entity"] = "GL.CENTER" }));
            Assert.Contains("# gl.Center", center, StringComparison.Ordinal);
            Assert.Contains("## Properties", center, StringComparison.Ordinal);
            Assert.Contains("tellma://queryex/syntax", center, StringComparison.Ordinal);

            // --- query: a Markdown table, then JSON objects with the count ---
            string table = Text(await CallAsync(client, "tellma_query", new() { ["entity"] = "gl.Center", ["select"] = "Id, Code", ["orderBy"] = "Code", ["top"] = 3 }));
            Assert.StartsWith("| Id | Code |\n| --- | --- |\n", table, StringComparison.Ordinal);
            Assert.Contains("returned: 3 rows from skip 0", table, StringComparison.Ordinal);
            string objects = Text(await CallAsync(client, "tellma_query", new() { ["entity"] = "gl.Center", ["select"] = "Id, Code", ["format"] = "objects", ["includeCount"] = true, ["top"] = 2 }));
            using var rows = JsonDocument.Parse(objects[..objects.IndexOf("\n\n", StringComparison.Ordinal)]);
            long id = Id(rows.RootElement[0].GetProperty("Id"));
            Assert.Contains("count: 36, countCapped: false", objects, StringComparison.Ordinal);

            // --- aggregate: a count computed in the database, inactive rows on request ---
            string aggregate = Text(await CallAsync(client, "tellma_aggregate", new() { ["entity"] = "gl.Center", ["select"] = "count()", ["includeInactive"] = true }));
            Assert.Contains("| 40 |", aggregate, StringComparison.Ordinal);

            // --- get: the record as JSON, and an id that names none reported ---
            string get = Text(await CallAsync(client, "tellma_get", new() { ["entity"] = "gl.Center", ["ids"] = new object[] { id, 999999 } }));
            Assert.StartsWith("{\"entities\":[{", get, StringComparison.Ordinal);
            Assert.Contains("absent: 999999", get, StringComparison.Ordinal);

            // --- a child entity, queried under its owner's read ---
            string lines = Text(await CallAsync(client, "tellma_query", new() { ["entity"] = "acme.SalesInvoiceLine", ["top"] = 1 }));
            Assert.Contains("returned: 1 rows from skip 0", lines, StringComparison.Ordinal);

            // --- a tool's refusal is an isError result with the web surface's code ---
            CallToolResult unknown = await client.CallToolAsync("tellma_query", new Dictionary<string, object?> { ["entity"] = "nope.Nothing" }, cancellationToken: Token);
            Assert.True(unknown.IsError);
            Assert.Contains("\"code\":\"bad-request\"", Text(unknown), StringComparison.Ordinal);

            // --- the Queryex reference ---
            ReadResourceResult syntax = await client.ReadResourceAsync("tellma://queryex/syntax", cancellationToken: Token);
            TextResourceContents text = Assert.IsType<TextResourceContents>(Assert.Single(syntax.Contents));
            Assert.Equal("text/markdown", text.MimeType);
            Assert.StartsWith("# Queryex syntax", text.Text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_endpoint_refuses_another_audience_another_tenant_no_scope_a_foreign_origin_and_a_get()
        {
            AcmeMigrator acme = new(sql, "acme_mcp_refusals");
            _ = await acme.MigrateAsync();
            await using AcmeHost host = await StartAsync(acme);
            using HttpClient client = host.Server.CreateClient();
            string metadata = $"{AcmeMigrator.PublicOrigin}/.well-known/oauth-protected-resource/{Live}/mcp";

            // --- No token, and a token for the public API: a challenge naming the tenant's metadata ---
            using (HttpResponseMessage none = await PostAsync(client, Live, token: null))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
                Assert.Equal($"Bearer resource_metadata=\"{metadata}\"", none.Headers.WwwAuthenticate.ToString());
            }

            using (HttpResponseMessage origin = await PostAsync(client, Live, UserToken(AcmeMigrator.PublicOrigin)))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, origin.StatusCode);
                Assert.Equal($"Bearer error=\"invalid_token\", resource_metadata=\"{metadata}\"", origin.Headers.WwwAuthenticate.ToString());
            }

            // --- Tenant 1's token at tenant 2 ---
            using (HttpResponseMessage crossed = await PostAsync(client, Sandbox, UserToken(Resource(Live))))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, crossed.StatusCode);
                Assert.Contains($"/.well-known/oauth-protected-resource/{Sandbox}/mcp", crossed.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
            }

            // --- A token naming a second audience beside the tenant's resource, at either tenant: invalid ---
            foreach (string[] audiences in (string[][])[[Resource(Live), Resource(Sandbox)], [Resource(Live), AcmeMigrator.PublicOrigin]])
            {
                foreach (int tenant in (int[])[Live, Sandbox])
                {
                    using HttpResponseMessage doubled = await PostAsync(client, tenant, UserToken(audiences));
                    Assert.Equal(HttpStatusCode.Unauthorized, doubled.StatusCode);
                    Assert.StartsWith("Bearer error=\"invalid_token\"", doubled.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
                }
            }

            // --- An expired token for the tenant's own resource: invalid ---
            using (HttpResponseMessage expired = await PostAsync(client, Live, UserToken(Resource(Live), expired: true)))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
                Assert.StartsWith("Bearer error=\"invalid_token\"", expired.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
            }

            // --- No API scope: insufficient scope ---
            using (HttpResponseMessage scopeless = await PostAsync(client, Live, UserToken(Resource(Live), scope: "openid")))
            {
                Assert.Equal(HttpStatusCode.Forbidden, scopeless.StatusCode);
                Assert.StartsWith("Bearer error=\"insufficient_scope\", scope=\"tellma_api\"", scopeless.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
            }

            // --- An unlisted browser origin ---
            using (HttpResponseMessage foreign = await PostAsync(client, Live, UserToken(Resource(Live)), origin: "https://evil.example"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
                Assert.Contains("\"code\":\"forbidden\"", await foreign.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
            }

            // --- A GET: the stateless server streams nothing ---
            using (HttpRequestMessage request = new(HttpMethod.Get, Endpoint(Live)))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", UserToken(Resource(Live)));
                request.Headers.Accept.ParseAdd("text/event-stream");
                using HttpResponseMessage get = await client.SendAsync(request, Token);
                Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
            }

            // --- The tenant's metadata names the resource and the distribution alone ---
            using HttpResponseMessage prm = await client.GetAsync(new Uri(metadata), Token);
            Assert.Equal(HttpStatusCode.OK, prm.StatusCode);
            using var document = JsonDocument.Parse(await prm.Content.ReadAsStringAsync(Token));
            Assert.Equal(Resource(Live), document.RootElement.GetProperty("resource").GetString());
        }

        [Fact]
        public async Task A_service_account_s_valid_token_learns_no_tenant_and_every_call_is_refused_as_individual_required()
        {
            AcmeMigrator acme = new(sql, "acme_mcp_service");
            _ = await acme.MigrateAsync();
            await using AcmeHost host = await StartAsync(acme);
            await using McpClient client = await ConnectAsync(host, Live, UserToken(Resource(Live), subject: "acme-agent", service: true));

            await AssertRefusedAsync(client, "\"code\":\"individual-required\"");
        }

        [Fact(Skip = NeedsUserConnection, SkipUnless = nameof(PlatformSeams.ConnectsTheUser), SkipType = typeof(PlatformSeams))]
        public async Task A_non_member_s_valid_token_learns_no_tenant_and_every_call_is_refused()
        {
            AcmeMigrator acme = new(sql, "acme_mcp_stranger");
            _ = await acme.MigrateAsync();
            await using AcmeHost host = await StartAsync(acme);
            await using McpClient client = await ConnectAsync(host, Live, UserToken(Resource(Live), subject: Guid.NewGuid().ToString()));

            await AssertRefusedAsync(client, "\"status\":404");
        }

        [Fact(Skip = NeedsUserConnectionAndBootstrap, SkipUnless = nameof(PlatformSeams.ServesTheAdministrator), SkipType = typeof(PlatformSeams))]
        public async Task A_member_deactivated_in_the_tenant_is_refused_by_the_next_call()
        {
            AcmeMigrator acme = new(sql, "acme_mcp_deactivated");
            _ = await acme.MigrateAsync();
            await using AcmeHost host = await StartAsync(acme);
            await JoinAsync(host);
            await using (McpClient member = await ConnectAsync(host, Live, UserToken(Resource(Live))))
            {
                _ = await CallAsync(member, "tellma_whoami", []);
            }

            // --- Deactivated in the tenant's database: whoami's fresh connect refuses them ---
            int administrator = await SqlServerFixture.ScalarAsync<int?>(
                acme.TenantConnectionString(Live), $"SELECT [Id] FROM [core].[Users] WHERE [Email] = N'{AcmeSignIn.Administrator}';")
                ?? throw new InvalidOperationException($"Tenant {Live} has no user {AcmeSignIn.Administrator}.");
            await SqlServerFixture.ExecuteAsync(
                acme.TenantConnectionString(Live),
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Users] SET [IsActive] = 0 WHERE [Id] = {administrator};"));

            await using McpClient deactivated = await ConnectAsync(host, Live, UserToken(Resource(Live)));
            CallToolResult refused = await deactivated.CallToolAsync("tellma_whoami", cancellationToken: Token);
            Assert.True(refused.IsError);
            Assert.Contains("\"status\":404", Text(refused), StringComparison.Ordinal);
        }

        // --- Helpers ---

        /// <summary>Asserts a connected caller learns no tenant and has a call refused.</summary>
        /// <param name="client">The client.</param>
        /// <param name="expected">What the refusal's text holds.</param>
        /// <returns>A task that completes when the assertions have run.</returns>
        private static async Task AssertRefusedAsync(McpClient client, string expected)
        {
            // The title is the distribution's display name alone, and the instructions name no tenant.
            Assert.DoesNotContain(" (", client.ServerInfo.Title, StringComparison.Ordinal);
            Assert.DoesNotContain("a live tenant", client.ServerInstructions, StringComparison.Ordinal);
            CallToolResult refused = await client.CallToolAsync("tellma_describe", cancellationToken: Token);
            Assert.True(refused.IsError);
            Assert.Contains(expected, Text(refused), StringComparison.Ordinal);
        }

        /// <summary>Starts Acme's host trusting the test issuer beside its engine.</summary>
        /// <param name="acme">The deployment.</param>
        /// <returns>The host.</returns>
        private static Task<AcmeHost> StartAsync(AcmeMigrator acme)
        {
            return AcmeHost.StartAsync(acme.Values, new AcmeHostOptions
            {
                Services = static services => _ = services.PostConfigure<JwtBearerOptions>(TellmaAuthentication.BearerScheme, static bearer =>
                {
                    // The handler adds the engine's issuer and keys to these once it has read its metadata.
                    bearer.TokenValidationParameters.ValidIssuers = [.. bearer.TokenValidationParameters.ValidIssuers ?? [], TestIssuer];
                    bearer.TokenValidationParameters.IssuerSigningKeys = [.. bearer.TokenValidationParameters.IssuerSigningKeys ?? [], SigningKey];
                }),
            });
        }

        /// <summary>Signs the administrator in once through the BFF, so their tenant user has joined.</summary>
        /// <param name="host">The host.</param>
        /// <returns>A task that completes when the administrator has joined.</returns>
        private static async Task JoinAsync(AcmeHost host)
        {
            using OidcFlowClient flow = host.CreateFlowClient();
            await AcmeSignIn.SignInAsync(flow);
        }

        /// <summary>Connects an SDK client to a tenant's server with a token.</summary>
        /// <param name="host">The host.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="token">The bearer token.</param>
        /// <returns>The client.</returns>
        private static async Task<McpClient> ConnectAsync(AcmeHost host, int tenantId, string token)
        {
            HttpClientTransport transport = new(
                new HttpClientTransportOptions
                {
                    Endpoint = Endpoint(tenantId),
                    TransportMode = HttpTransportMode.StreamableHttp,
                    AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token },
                },
                host.Server.CreateClient(),
                ownsHttpClient: true);

            // The SDK abandons its discover probe after five seconds, and a freshly started host's
            // first request builds the route matcher before anything else runs, which under load
            // takes longer. Over the in-memory server the abandoned probe's answer can still arrive
            // and fail the connect, so the probe is bounded by the connect's whole budget alone.
            return await McpClient.CreateAsync(transport, new McpClientOptions { DiscoverProbeTimeout = Timeout.InfiniteTimeSpan }, cancellationToken: Token);
        }

        /// <summary>Calls a tool and asserts it succeeded.</summary>
        /// <param name="client">The client.</param>
        /// <param name="tool">The tool.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The result.</returns>
        private static async Task<CallToolResult> CallAsync(McpClient client, string tool, Dictionary<string, object?> arguments)
        {
            CallToolResult result = await client.CallToolAsync(tool, arguments, cancellationToken: Token);
            Assert.True(result.IsError != true, Text(result));
            return result;
        }

        /// <summary>A result's text block.</summary>
        /// <param name="result">The result.</param>
        /// <returns>The text.</returns>
        private static string Text(CallToolResult result)
        {
            return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        }

        /// <summary>An id as the wire writes it: a number, or a long's digits.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The id.</returns>
        private static long Id(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.String ? long.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetInt64();
        }

        /// <summary>Posts an initialize request to a tenant's endpoint.</summary>
        /// <param name="client">The client.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="token">The bearer token; null for none.</param>
        /// <param name="origin">The browser origin; null for a native client.</param>
        /// <returns>The response.</returns>
        private static async Task<HttpResponseMessage> PostAsync(HttpClient client, int tenantId, string? token, string? origin = null)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, Endpoint(tenantId))
            {
                Content = new StringContent(
                    /*lang=json,strict*/ """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"tests","version":"1.0"}}}""",
                    Encoding.UTF8,
                    "application/json"),
            };
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");
            if (token is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            if (origin is not null)
            {
                request.Headers.Add("Origin", origin);
            }

            return await client.SendAsync(request, Token);
        }

        /// <summary>A tenant's endpoint.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The endpoint.</returns>
        private static Uri Endpoint(int tenantId)
        {
            return new Uri(string.Create(CultureInfo.InvariantCulture, $"{AcmeMigrator.PublicOrigin}/{tenantId}/mcp"));
        }

        /// <summary>A tenant server's resource, the audience its tokens carry.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The resource.</returns>
        private static string Resource(int tenantId)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{AcmeMigrator.PublicOrigin}/{tenantId}/mcp");
        }

        /// <summary>An access token of the test issuer, for the administrator unless another subject is named.</summary>
        /// <param name="audience">The audience: one value, or an array of them.</param>
        /// <param name="scope">The scopes.</param>
        /// <param name="subject">The subject.</param>
        /// <param name="service">Whether the token speaks for a service account.</param>
        /// <param name="expired">Whether the token expired well past the validation's clock skew.</param>
        /// <returns>The token.</returns>
        private static string UserToken(object audience, string scope = "tellma_api", string subject = AdministratorSubject, bool service = false, bool expired = false)
        {
            DateTime now = expired ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow;
            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = TestIssuer,
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = subject,
                    ["azp"] = service ? subject : "claude-code",
                    ["aud"] = audience,
                    ["scope"] = scope,
                    ["tellma_kind"] = service ? "service" : "individual",
                    ["auth_time"] = new DateTimeOffset(now.AddMinutes(-1)).ToUnixTimeSeconds(),
                },
                IssuedAt = now,
                NotBefore = now.AddMinutes(-1),
                Expires = now.AddMinutes(10),
                TokenType = "at+jwt",
                SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
            });
        }
    }
}
