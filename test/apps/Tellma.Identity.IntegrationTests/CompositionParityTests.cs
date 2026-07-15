// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using System.Text.Json;
using Tellma.Identity.IntegrationTests.Infrastructure;

namespace Tellma.Identity.IntegrationTests
{
    /// <summary>
    ///     The architecture tests: both hosting shapes (standalone and in-proc) share one
    ///     registration path, so the authority they serve is identical, and the standalone host
    ///     stays composition-only.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class CompositionParityTests(SqlServerFixture fixture)
    {
        [Fact]
        public void Standalone_host_references_no_protocol_stack_directly()
        {
            // The host must compose only through AddTellmaIdentity; a direct OpenIddict or
            // Identity reference would let the two hosting shapes drift.
            AssemblyName[] references = typeof(Web.WebHostMarker).Assembly.GetReferencedAssemblies();
            Assert.DoesNotContain(references, static reference =>
                reference.Name!.StartsWith("OpenIddict", StringComparison.Ordinal)
                || reference.Name.StartsWith("Microsoft.AspNetCore.Identity", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Both_compositions_serve_the_same_authority()
        {
            using StandaloneFactory standalone = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idpar1");
            using InProcFactory inProc = await DatabaseBackedFactory.CreateInProcAsync(fixture, "idpar2");

            using JsonDocument standaloneDiscovery = await FetchDiscoveryAsync(
                standalone.CreateClient(), "/.well-known/openid-configuration");
            using JsonDocument inProcDiscovery = await FetchDiscoveryAsync(
                inProc.CreateClient(), "/id/.well-known/openid-configuration");

            JsonElement left = standaloneDiscovery.RootElement;
            JsonElement right = inProcDiscovery.RootElement;

            // Identical protocol capabilities.
            Assert.Equal(SortedStrings(left, "grant_types_supported"), SortedStrings(right, "grant_types_supported"));
            Assert.Equal(SortedStrings(left, "response_types_supported"), SortedStrings(right, "response_types_supported"));
            Assert.Equal(SortedStrings(left, "scopes_supported"), SortedStrings(right, "scopes_supported"));
            Assert.Equal(SortedStrings(left, "code_challenge_methods_supported"), SortedStrings(right, "code_challenge_methods_supported"));

            // Issuers differ only by the reserved path base.
            Assert.Equal("http://localhost", left.GetProperty("issuer").GetString()?.TrimEnd('/'));
            Assert.Equal("http://localhost/id", right.GetProperty("issuer").GetString()?.TrimEnd('/'));

            // Every endpoint differs only by the /id prefix.
            foreach (string endpoint in (string[])
                ["authorization_endpoint", "token_endpoint", "userinfo_endpoint", "end_session_endpoint",
                 "device_authorization_endpoint", "pushed_authorization_request_endpoint",
                 "introspection_endpoint", "revocation_endpoint", "jwks_uri"])
            {
                string? standaloneUrl = left.GetProperty(endpoint).GetString();
                string? inProcUrl = right.GetProperty(endpoint).GetString();
                Assert.Equal(
                    standaloneUrl,
                    inProcUrl?.Replace("http://localhost/id/", "http://localhost/", StringComparison.Ordinal));
            }
        }

        [Fact]
        public async Task InProc_host_still_serves_its_own_routes()
        {
            using InProcFactory inProc = await DatabaseBackedFactory.CreateInProcAsync(fixture, "idpar3");
            using HttpClient client = inProc.CreateClient();
            string body = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal("Distribution host", body);
        }

        /// <summary>Fetches and parses a discovery document.</summary>
        private static async Task<JsonDocument> FetchDiscoveryAsync(HttpClient client, string path)
        {
            using (client)
            {
                using HttpResponseMessage response = await client.GetAsync(
                    new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
                response.EnsureSuccessStatusCode();
                return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }
        }

        /// <summary>Reads a string array property in sorted order for order-insensitive comparison.</summary>
        private static string[] SortedStrings(JsonElement element, string property)
        {
            return [.. element.GetProperty(property).EnumerateArray().Select(static e => e.GetString()!).OrderBy(static s => s, StringComparer.Ordinal)];
        }
    }
}
