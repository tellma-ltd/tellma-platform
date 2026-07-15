// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Identity.IntegrationTests.Infrastructure;

namespace Tellma.Identity.IntegrationTests
{
    /// <summary>
    ///     The discovery document and JWKS advertise exactly the configured protocol surface.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class DiscoveryTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task Discovery_document_advertises_the_expected_capabilities()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "iddisc");
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.GetAsync(
                new Uri("/.well-known/openid-configuration", UriKind.Relative),
                TestContext.Current.CancellationToken);

            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            JsonElement root = document.RootElement;

            Assert.Equal("http://localhost", root.GetProperty("issuer").GetString()?.TrimEnd('/'));

            string[] grantTypes = [.. root.GetProperty("grant_types_supported").EnumerateArray().Select(static e => e.GetString()!)];
            Assert.Contains("authorization_code", grantTypes);
            Assert.Contains("refresh_token", grantTypes);
            Assert.Contains("client_credentials", grantTypes);
            Assert.Contains("urn:ietf:params:oauth:grant-type:device_code", grantTypes);
            Assert.Contains("urn:ietf:params:oauth:grant-type:token-exchange", grantTypes);

            string[] scopes = [.. root.GetProperty("scopes_supported").EnumerateArray().Select(static e => e.GetString()!)];
            Assert.Contains("tellma_api", scopes);
            Assert.Contains("tellma_identity", scopes);
            Assert.Contains("tellma_control_plane", scopes);
            Assert.Contains("offline_access", scopes);

            string[] challengeMethods = [.. root.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(static e => e.GetString()!)];
            Assert.Contains("S256", challengeMethods);

            // RFC 9207: the authorization response carries `iss` to prevent mix-up attacks.
            Assert.True(root.GetProperty("authorization_response_iss_parameter_supported").GetBoolean());

            // Core endpoints advertised at the expected paths.
            Assert.Equal("http://localhost/connect/authorize", root.GetProperty("authorization_endpoint").GetString());
            Assert.Equal("http://localhost/connect/token", root.GetProperty("token_endpoint").GetString());
            Assert.Equal("http://localhost/connect/par", root.GetProperty("pushed_authorization_request_endpoint").GetString());
            Assert.Equal("http://localhost/connect/endsession", root.GetProperty("end_session_endpoint").GetString());
            Assert.Equal("http://localhost/connect/device", root.GetProperty("device_authorization_endpoint").GetString());
        }

        [Fact]
        public async Task Jwks_publishes_only_asymmetric_signing_keys()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idjwks");
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.GetAsync(
                new Uri("/.well-known/jwks", UriKind.Relative),
                TestContext.Current.CancellationToken);

            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

            JsonElement[] keys = [.. document.RootElement.GetProperty("keys").EnumerateArray()];
            Assert.NotEmpty(keys);
            Assert.All(keys, static key =>
            {
                // EC or RSA only — a symmetric key here would break offline validation.
                string? type = key.GetProperty("kty").GetString();
                Assert.True(type is "EC" or "RSA", $"Unexpected key type '{type}' in JWKS.");
            });
        }
    }
}
