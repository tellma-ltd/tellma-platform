// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Surfaces
{
    /// <summary>
    ///     The anonymous platform surfaces: the distribution's info document, the protected-resource
    ///     metadata and the liveness probe, each answered without a tenant or a caller.
    /// </summary>
    public sealed class PlatformSurfaceTests
    {
        [Fact]
        public async Task The_info_document_describes_the_distribution()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options =>
            {
                options.Settings["Tellma:Links:Terms"] = "https://acme.example/terms";
                options.Settings["Tellma:Links:Privacy"] = string.Empty;
            });
            using HttpResponseMessage response = await host.Client.GetAsync("/api/distribution-info", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("public, max-age=60", response.Headers.CacheControl?.ToString());
            using JsonDocument document = await ReadJsonAsync(response);
            JsonElement info = document.RootElement;

            Assert.Equal(TellmaTestHost.Slug, info.GetProperty("slug").GetString());
            Assert.Equal("Acme ERP", info.GetProperty("displayName").GetString());
            Assert.False(string.IsNullOrEmpty(info.GetProperty("platformVersion").GetString()));
            Assert.Equal(TellmaTestHost.Issuer, info.GetProperty("identity").GetProperty("authority").GetString());
            Assert.Equal("Standalone", info.GetProperty("identity").GetProperty("mode").GetString());
            Assert.Equal(TellmaTestHost.Issuer + "/account?client_id=acme", info.GetProperty("identity").GetProperty("accountUrl").GetString());
            Assert.Equal("/{tenantId}/api/web", info.GetProperty("surfaces").GetProperty("web").GetString());
            Assert.Equal("/bff/login", info.GetProperty("login").GetString());
            Assert.Equal([1], info.GetProperty("adminVersions").EnumerateArray().Select(static version => version.GetInt32()));
            Assert.Equal("https://acme.example/terms", info.GetProperty("links").GetProperty("terms").GetString());
            Assert.False(info.GetProperty("links").TryGetProperty("privacy", out _));
            Assert.True(info.GetProperty("support").GetProperty("providerAccess").GetBoolean());

            int idleDays = host.Services.GetRequiredService<IOptions<TellmaSessionOptions>>().Value.IdleDays;
            Assert.Equal(idleDays, info.GetProperty("session").GetProperty("idleDays").GetInt32());

            // Nothing about the tenants, the catalog or a secret.
            string text = document.RootElement.GetRawText();
            Assert.DoesNotContain("Data Source", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("tenant", text.Replace("tenantId", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task The_info_document_announces_no_admin_surface_when_it_is_disabled()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options => options.Settings["Tellma:Admin:Enabled"] = "false");
            using HttpResponseMessage response = await host.Client.GetAsync("/api/distribution-info", TestContext.Current.CancellationToken);
            using JsonDocument document = await ReadJsonAsync(response);

            Assert.Empty(document.RootElement.GetProperty("adminVersions").EnumerateArray());
            Assert.False(document.RootElement.GetProperty("support").GetProperty("providerAccess").GetBoolean());

            // The surface itself is not mapped: its routes fall through to the not-found answer.
            using HttpResponseMessage admin = await host.Client.GetAsync("/api/admin/v1/info", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, admin.StatusCode);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4242)]
        public async Task The_protected_resource_metadata_answers_alike_for_every_tenant_id(int tenantId)
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            using HttpResponseMessage response = await host.Client.GetAsync($"/.well-known/oauth-protected-resource/{tenantId}/mcp", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("public, max-age=300", response.Headers.CacheControl?.ToString());
            using JsonDocument document = await ReadJsonAsync(response);
            JsonElement metadata = document.RootElement;
            Assert.Equal($"{TellmaTestHost.PublicOrigin}/{tenantId}/mcp", metadata.GetProperty("resource").GetString());
            Assert.Equal([TellmaTestHost.Issuer], metadata.GetProperty("authorization_servers").EnumerateArray().Select(static server => server.GetString()));
            Assert.Equal(["tellma_api"], metadata.GetProperty("scopes_supported").EnumerateArray().Select(static scope => scope.GetString()));
            Assert.Equal(["header"], metadata.GetProperty("bearer_methods_supported").EnumerateArray().Select(static method => method.GetString()));
        }

        [Fact]
        public async Task The_liveness_probe_answers_even_when_the_catalog_is_down()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options => options.CatalogFailure = new TimeoutException("The catalog is unreachable."));
            using HttpResponseMessage response = await host.Client.GetAsync("/health/live", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        /// <summary>Reads a JSON body.</summary>
        /// <param name="response">The response.</param>
        /// <returns>The document.</returns>
        private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
        {
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
    }
}
