// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using System.Buffers.Text;
using System.Collections.Immutable;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.ClientMetadata;
using Tellma.Identity.TestSupport;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     Document clients end to end: a client whose id is the URL of its metadata document
    ///     signs a user in with no registration step, the consent screen names the host the
    ///     document came from, its row follows the document as the document changes, an expired
    ///     document that can no longer be fetched stops the client, a key set published beside
    ///     the document authenticates it at the token endpoint, and the sweep removes only the
    ///     rows nobody consented to. Every document is served from memory.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class DocumentClientTests(SqlServerFixture fixture)
    {
        /// <summary>The client id, which is also where the document is published.</summary>
        private const string ClientId = "https://agent.example.com/oauth/client.json";

        /// <summary>Where the private-key client publishes its key set.</summary>
        private const string JwksUri = "https://agent.example.com/oauth/jwks.json";

        /// <summary>The port-less loopback redirect the documents list.</summary>
        private const string ListedRedirect = "http://127.0.0.1/callback";

        /// <summary>The redirect the client actually uses: the listed one on a port of its choosing.</summary>
        private const string PortedRedirect = "http://127.0.0.1:50123/callback";

        /// <summary>The audience a client assertion names: the issuer of the test host.</summary>
        private const string AssertionAudience = "http://localhost/";

        [Fact]
        public async Task A_document_client_signs_a_user_in_and_the_consent_screen_names_its_host()
        {
            LocalDocumentServer server = new();
            server.Publish(ClientId, Document(ClientId, "Example Agent", [ListedRedirect]));
            using StandaloneFactory factory = CreateFactory("iddocflow", server);
            using OidcFlowClient flow = factory.CreateFlowClient();
            await ConsentTests.SignInAsync(factory, flow, "docflow@example.com");

            (string verifier, string challenge) = OidcFlowClient.CreatePkcePair();
            using HttpResponseMessage consentPage = await AuthorizeAsync(flow, ClientId, PortedRedirect, challenge);

            // The name is the document's own claim; the host beside it is what a person can check.
            Assert.Equal(HttpStatusCode.OK, consentPage.StatusCode);
            string html = await consentPage.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("Example Agent", html, StringComparison.Ordinal);
            Assert.Contains("<bdi dir=\"ltr\">agent.example.com</bdi>", html, StringComparison.Ordinal);

            string code = await AcceptForCodeAsync(flow, consentPage, PortedRedirect);
            using JsonDocument tokens = await flow.ExchangeAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = ClientId,
                ["code"] = code,
                ["redirect_uri"] = PortedRedirect,
                ["code_verifier"] = verifier,
            });

            Assert.True(tokens.RootElement.TryGetProperty("access_token", out _));
            Assert.True(tokens.RootElement.TryGetProperty("id_token", out _));
        }

        [Fact]
        public async Task The_row_is_created_from_the_document_and_updated_when_the_document_changes()
        {
            LocalDocumentServer server = new();
            server.Publish(ClientId, Document(ClientId, "Agent One", [ListedRedirect]));
            using StandaloneFactory factory = CreateFactory("iddocupdate", server);
            using OidcFlowClient flow = factory.CreateFlowClient();

            await PushAsync(flow, ClientId, PortedRedirect);
            (string? name, ImmutableArray<string> redirects) = await ReadRowAsync(factory, ClientId);
            Assert.Equal("Agent One", name);
            Assert.Equal([ListedRedirect], redirects);

            // A redirect the cached document does not list is fetched again before it is refused,
            // so a client that has just published a new callback can use it straight away.
            const string NewRedirect = "http://127.0.0.1/other";
            server.Publish(ClientId, Document(ClientId, "Agent Two", [ListedRedirect, NewRedirect]));
            await PushAsync(flow, ClientId, NewRedirect);

            (name, redirects) = await ReadRowAsync(factory, ClientId);
            Assert.Equal("Agent Two", name);
            Assert.Equal([ListedRedirect, NewRedirect], redirects.Order(StringComparer.Ordinal));
        }

        [Fact]
        public async Task An_expired_document_that_cannot_be_fetched_again_is_invalid_client()
        {
            LocalDocumentServer server = new();
            server.Publish(ClientId, Document(ClientId, "Example Agent", [ListedRedirect]));
            using StandaloneFactory factory = CreateFactory("iddocexpiry", server, new Dictionary<string, string?>
            {
                ["TellmaIdentity:ClientMetadata:MinCache"] = "00:00:01",
                ["TellmaIdentity:ClientMetadata:MaxCache"] = "00:00:01",
            });
            using OidcFlowClient flow = factory.CreateFlowClient();
            await PushAsync(flow, ClientId, PortedRedirect);

            // The row is still in the database, but a client is only as good as a document the
            // server can currently vouch for: once the cached copy expires and the host cannot be
            // reached, the client is refused rather than run on a stale copy.
            server.Fail(ClientId);
            await Task.Delay(TimeSpan.FromSeconds(1.5), TestContext.Current.CancellationToken);

            string? error = await flow.ExchangeExpectingErrorAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = ClientId,
                ["code"] = "not-a-code",
                ["redirect_uri"] = PortedRedirect,
                ["code_verifier"] = "not-a-verifier",
            });

            Assert.Equal(OpenIddictConstants.Errors.InvalidClient, error);
            Assert.Equal(2, server.Fetched.Count(url => url.AbsoluteUri == ClientId));
        }

        [Fact]
        public async Task A_private_key_client_exchanges_its_code_with_an_assertion_signed_by_its_published_key()
        {
            using var rsa = RSA.Create(2048);
            RSAParameters publicKey = rsa.ExportParameters(includePrivateParameters: false);
            LocalDocumentServer server = new();
            server.Publish(ClientId, Document(ClientId, "Keyed Agent", [ListedRedirect], "private_key_jwt", JwksUri));
            server.Publish(JwksUri, new JsonObject
            {
                ["keys"] = new JsonArray(new JsonObject
                {
                    ["kty"] = "RSA",
                    ["use"] = "sig",
                    ["alg"] = "RS256",
                    ["kid"] = "agent-key",
                    ["n"] = Base64Url.EncodeToString(publicKey.Modulus),
                    ["e"] = Base64Url.EncodeToString(publicKey.Exponent),
                }),
            }.ToJsonString());
            using StandaloneFactory factory = CreateFactory("iddocjwt", server);
            using OidcFlowClient flow = factory.CreateFlowClient();
            await ConsentTests.SignInAsync(factory, flow, "docjwt@example.com");

            (string verifier, string challenge) = OidcFlowClient.CreatePkcePair();
            using HttpResponseMessage consentPage = await AuthorizeAsync(flow, ClientId, PortedRedirect, challenge);
            Assert.Equal(HttpStatusCode.OK, consentPage.StatusCode);
            string code = await AcceptForCodeAsync(flow, consentPage, PortedRedirect);

            Dictionary<string, string> exchange = new()
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = ClientId,
                ["code"] = code,
                ["redirect_uri"] = PortedRedirect,
                ["code_verifier"] = verifier,
                ["client_assertion_type"] = OpenIddictConstants.ClientAssertionTypes.JwtBearer,
                ["client_assertion"] = CreateAssertion(new RsaSecurityKey(rsa) { KeyId = "agent-key" }),
            };
            using JsonDocument tokens = await flow.ExchangeAsync(exchange);

            Assert.True(tokens.RootElement.TryGetProperty("access_token", out _));
            Assert.Contains(server.Fetched, url => url.AbsoluteUri == JwksUri);
        }

        [Fact]
        public async Task A_private_key_client_without_an_assertion_is_refused()
        {
            using var rsa = RSA.Create(2048);
            RSAParameters publicKey = rsa.ExportParameters(includePrivateParameters: false);
            LocalDocumentServer server = new();
            server.Publish(ClientId, Document(ClientId, "Keyed Agent", [ListedRedirect], "private_key_jwt", JwksUri));
            server.Publish(JwksUri, new JsonObject
            {
                ["keys"] = new JsonArray(new JsonObject
                {
                    ["kty"] = "RSA",
                    ["kid"] = "agent-key",
                    ["n"] = Base64Url.EncodeToString(publicKey.Modulus),
                    ["e"] = Base64Url.EncodeToString(publicKey.Exponent),
                }),
            }.ToJsonString());
            using StandaloneFactory factory = CreateFactory("iddocjwtnone", server);
            using OidcFlowClient flow = factory.CreateFlowClient();

            // The document asks for key authentication, so presenting only the id is not enough.
            string? error = await flow.ExchangeExpectingErrorAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = ClientId,
                ["code"] = "not-a-code",
                ["redirect_uri"] = PortedRedirect,
                ["code_verifier"] = "not-a-verifier",
            });

            Assert.Equal(OpenIddictConstants.Errors.InvalidClient, error);

            // The refusal is OpenIddict's client authentication, not a failed resolution: the
            // document and its key set were fetched, and the row the request left behind is a
            // confidential client holding the published key.
            Assert.Contains(server.Fetched, static url => url.AbsoluteUri == JwksUri);
            using IServiceScope scope = factory.Services.CreateScope();
            IOpenIddictApplicationManager applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            object row = (await applications.FindByClientIdAsync(ClientId, TestContext.Current.CancellationToken))!;
            Assert.NotNull(row);
            Assert.True(await applications.HasClientTypeAsync(row, OpenIddictConstants.ClientTypes.Confidential, TestContext.Current.CancellationToken));
            JsonWebKeySet keys = (await applications.GetJsonWebKeySetAsync(row, TestContext.Current.CancellationToken))!;
            Assert.Equal("agent-key", Assert.Single(keys.Keys).Kid);
        }

        [Fact]
        public async Task A_client_id_differing_from_a_stored_one_only_in_case_is_invalid_client()
        {
            const string Stored = "https://agent.example.com/oauth/Client.json";
            const string Variant = "https://agent.example.com/oauth/client.json";
            LocalDocumentServer server = new();
            server.Publish(Stored, Document(Stored, "First Agent", [ListedRedirect]));
            server.Publish(Variant, Document(Variant, "Second Agent", [ListedRedirect]));
            using StandaloneFactory factory = CreateFactory("iddoccase", server);
            using OidcFlowClient flow = factory.CreateFlowClient();
            await PushAsync(flow, Stored, PortedRedirect);

            // Both are valid document URLs, but the store holds client ids without regard to case,
            // so the second can never have a row of its own; it is refused rather than failing.
            using HttpClient client = factory.CreateClient();
            (_, string challenge) = OidcFlowClient.CreatePkcePair();
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/par", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = Variant,
                    ["redirect_uri"] = PortedRedirect,
                    ["response_type"] = "code",
                    ["scope"] = "openid profile",
                    ["code_challenge"] = challenge,
                    ["code_challenge_method"] = "S256",
                }),
                TestContext.Current.CancellationToken);

            // A client the server cannot accept is answered 401 at the pushed-request endpoint.
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal(OpenIddictConstants.Errors.InvalidClient, body.RootElement.GetProperty("error").GetString());
            (string? name, _) = await ReadRowAsync(factory, Stored);
            Assert.Equal("First Agent", name);
        }

        [Fact]
        public async Task The_sweep_removes_an_unconsented_row_and_keeps_a_consented_one()
        {
            const string ConsentedId = "https://agent.example.com/oauth/consented.json";
            const string IdleId = "https://agent.example.com/oauth/idle.json";
            LocalDocumentServer server = new();
            server.Publish(ConsentedId, Document(ConsentedId, "Consented Agent", [ListedRedirect]));
            server.Publish(IdleId, Document(IdleId, "Idle Agent", [ListedRedirect]));
            using StandaloneFactory factory = CreateFactory("iddocprune", server);
            using OidcFlowClient flow = factory.CreateFlowClient();
            await ConsentTests.SignInAsync(factory, flow, "docprune@example.com");

            // One client is consented to, which leaves an authorization behind; the other is only
            // ever fetched, which leaves a row and nothing else.
            (_, string challenge) = OidcFlowClient.CreatePkcePair();
            using (HttpResponseMessage consentPage = await AuthorizeAsync(flow, ConsentedId, PortedRedirect, challenge))
            {
                await AcceptForCodeAsync(flow, consentPage, PortedRedirect);
            }

            await PushAsync(flow, IdleId, PortedRedirect);

            // Swept as if a day had passed: both rows are old enough and the pushed request the
            // idle client left behind has lapsed, so only consent decides which row survives.
            DateTimeOffset later = DateTimeOffset.UtcNow.AddDays(1);
            int deleted;
            using (IServiceScope scope = factory.Services.CreateScope())
            {
                deleted = await scope.ServiceProvider.GetRequiredService<IDocumentClientStore>()
                    .PruneAsync(later, later, TestContext.Current.CancellationToken);
            }

            Assert.Equal(1, deleted);
            using IServiceScope check = factory.Services.CreateScope();
            IOpenIddictApplicationManager applications = check.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            Assert.NotNull(await applications.FindByClientIdAsync(ConsentedId, TestContext.Current.CancellationToken));
            Assert.Null(await applications.FindByClientIdAsync(IdleId, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task The_sweep_keeps_an_old_row_whose_pushed_request_is_still_live()
        {
            LocalDocumentServer server = new();
            server.Publish(ClientId, Document(ClientId, "Example Agent", [ListedRedirect]));
            using StandaloneFactory factory = CreateFactory("iddocinflight", server);
            using OidcFlowClient flow = factory.CreateFlowClient();
            await PushAsync(flow, ClientId, PortedRedirect);

            // The row is old enough to go, but the request it just pushed has not lapsed: the
            // user may be on the consent screen, and deleting the row would end that flow.
            using IServiceScope scope = factory.Services.CreateScope();
            int deleted = await scope.ServiceProvider.GetRequiredService<IDocumentClientStore>()
                .PruneAsync(DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

            Assert.Equal(0, deleted);
            IOpenIddictApplicationManager applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            Assert.NotNull(await applications.FindByClientIdAsync(ClientId, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task The_scheduled_sweep_keeps_a_row_younger_than_the_unused_lifetime()
        {
            LocalDocumentServer server = new();
            server.Publish(ClientId, Document(ClientId, "Example Agent", [ListedRedirect]));
            using StandaloneFactory factory = CreateFactory("iddocyoung", server);
            using OidcFlowClient flow = factory.CreateFlowClient();
            await PushAsync(flow, ClientId, PortedRedirect);

            // The job reads its cutoff from the configured lifetime (an hour by default), so a
            // row that was created just now is not yet unused for long enough.
            using IServiceScope scope = factory.Services.CreateScope();
            int deleted = await scope.ServiceProvider.GetRequiredService<DocumentClientPruneJob>()
                .PruneAsync(TestContext.Current.CancellationToken);

            Assert.Equal(0, deleted);
            IOpenIddictApplicationManager applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            Assert.NotNull(await applications.FindByClientIdAsync(ClientId, TestContext.Current.CancellationToken));
        }

        /// <summary>
        ///     Boots a host whose metadata client is served by <paramref name="server" />, with a
        ///     per-host fetch limit high enough that a test's own requests never meet it.
        /// </summary>
        private StandaloneFactory CreateFactory(
            string databasePrefix, LocalDocumentServer server, Dictionary<string, string?>? overrides = null)
        {
            Dictionary<string, string?> settings = new(overrides ?? [])
            {
                ["TellmaIdentity:ClientMetadata:FetchesPerHostPerMinute"] = "100",
            };
            StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, databasePrefix, settings);
            server.Attach(factory);
            return factory;
        }

        /// <summary>A metadata document naming its own URL as its client id.</summary>
        private static string Document(
            string clientId, string name, string[] redirectUris, string authMethod = "none", string? jwksUri = null)
        {
            JsonObject document = new()
            {
                ["client_id"] = clientId,
                ["client_name"] = name,
                ["redirect_uris"] = new JsonArray([.. redirectUris.Select(static uri => JsonValue.Create(uri))]),
                ["token_endpoint_auth_method"] = authMethod,
            };
            if (jwksUri is not null)
            {
                document["jwks_uri"] = jwksUri;
            }

            return document.ToJsonString();
        }

        /// <summary>Starts an authorization for a document client on the established session.</summary>
        private static async Task<HttpResponseMessage> AuthorizeAsync(
            OidcFlowClient flow, string clientId, string redirectUri, string challenge)
        {
            string url = "/connect/authorize?client_id=" + Uri.EscapeDataString(clientId)
                + "&response_type=code"
                + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
                + "&scope=" + Uri.EscapeDataString("openid profile")
                + "&code_challenge=" + challenge + "&code_challenge_method=S256";
            return await flow.Browser.GetAsync(new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken);
        }

        /// <summary>Accepts a consent page and returns the code the redirect carries.</summary>
        private static async Task<string> AcceptForCodeAsync(OidcFlowClient flow, HttpResponseMessage consentPage, string redirectUri)
        {
            using HttpResponseMessage granted = await ConsentTests.AcceptAsync(flow, consentPage);
            string body = await granted.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(granted.StatusCode == HttpStatusCode.Redirect, $"Expected the redirect back, got {(int)granted.StatusCode}. Body:\n{body}");

            Uri location = granted.Headers.Location!;
            Assert.StartsWith(redirectUri, location.ToString(), StringComparison.Ordinal);
            string query = location.Query.TrimStart('?');
            string pair = query.Split('&').Single(static part => part.StartsWith("code=", StringComparison.Ordinal));
            return Uri.UnescapeDataString(pair["code=".Length..]);
        }

        /// <summary>Pushes an authorization request for a public document client.</summary>
        private static async Task PushAsync(OidcFlowClient flow, string clientId, string redirectUri)
        {
            (_, string challenge) = OidcFlowClient.CreatePkcePair();
            await flow.PushAuthorizationRequestAsync(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = "openid profile",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
            });
        }

        /// <summary>Reads a document client's row: its display name and its registered redirects.</summary>
        private static async Task<(string? Name, ImmutableArray<string> Redirects)> ReadRowAsync(StandaloneFactory factory, string clientId)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            IOpenIddictApplicationManager applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            object row = (await applications.FindByClientIdAsync(clientId, TestContext.Current.CancellationToken))!;
            Assert.NotNull(row);
            return (
                await applications.GetDisplayNameAsync(row, TestContext.Current.CancellationToken),
                await applications.GetRedirectUrisAsync(row, TestContext.Current.CancellationToken));
        }

        /// <summary>
        ///     A client assertion as a private-key client signs one: issued by and about the client,
        ///     addressed to the server, typed for client authentication and good for one use.
        /// </summary>
        private static string CreateAssertion(SecurityKey key)
        {
            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = ClientId,
                Audience = AssertionAudience,
                TokenType = "client-authentication+jwt",
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = ClientId,
                    ["jti"] = Guid.NewGuid().ToString("N"),
                },
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
            });
        }
    }
}
