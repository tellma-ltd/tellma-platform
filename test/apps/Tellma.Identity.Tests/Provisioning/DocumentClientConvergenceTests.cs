// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using System.Globalization;
using Tellma.Identity.Options;
using Tellma.Identity.Services;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.ClientMetadata;
using Tellma.Identity.Services.Provisioning;
using Tellma.Identity.Tests.Services.ClientMetadata;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Tests.Provisioning
{
    /// <summary>
    ///     A document client's application row: what the descriptor holds for each kind of
    ///     document, that the real application manager accepts it, and how the row converges —
    ///     created under the cap, rewritten when the document changes (its keys included),
    ///     untouched when it does not, and never written over a row that is not a document
    ///     client's.
    /// </summary>
    public sealed class DocumentClientConvergenceTests : IDisposable
    {
        private const string Issuer = "https://id.tellma.com";

        private static readonly DateTimeOffset Created = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

        /// <summary>The first key set's JSON, so a test can build an equal set again.</summary>
        private static readonly string FirstKeySet = MetadataDocuments.KeySet(MetadataDocuments.RsaKey());

        /// <summary>The engine options the converger reads, the cap among them.</summary>
        private readonly TellmaIdentityOptions _options = new() { Issuer = new Uri(Issuer) };

        /// <summary>The in-memory stores and the converger.</summary>
        private readonly ServiceProvider _services;

        /// <summary>Builds the services over in-memory stores.</summary>
        public DocumentClientConvergenceTests()
        {
            ServiceCollection services = new();
            services.AddInMemoryOpenIddict();
            services.AddSingleton<IAuditLogger, AuditRecorder>();
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(_options));
            services.AddSingleton(TimeProvider.System);
            services.AddScoped<ClientRegistrationConverger>();
            services.AddScoped<IDocumentClientStore, InMemoryDocumentClientStore>();
            _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        /// <summary>The application rows of the test's store.</summary>
        private InMemoryApplicationStore Applications => _services.GetRequiredService<InMemoryApplicationStore>();

        [Fact]
        public void A_public_document_gives_a_public_native_row_with_the_fixed_permissions()
        {
            OpenIddictApplicationDescriptor descriptor = ClientDescriptorFactory.DocumentClient(
                Document(redirectUris: ["http://127.0.0.1:8400/callback"]), Created);

            Assert.Equal(MetadataDocuments.ClientId, descriptor.ClientId);
            Assert.Equal("Example Agent", descriptor.DisplayName);
            Assert.Equal(ClientTypes.Public, descriptor.ClientType);
            Assert.Equal(ApplicationTypes.Native, descriptor.ApplicationType);
            Assert.Equal(ConsentTypes.Explicit, descriptor.ConsentType);
            Assert.Null(descriptor.ClientSecret);
            Assert.Null(descriptor.JsonWebKeySet);

            // The loopback redirect loses its port, so the engine's native relaxation lets any
            // port match it.
            Assert.Equal([new Uri("http://127.0.0.1/callback")], descriptor.RedirectUris);
            Assert.True(
                new HashSet<string>(StringComparer.Ordinal)
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.PushedAuthorization,
                    Permissions.Endpoints.Token,
                    Permissions.Endpoints.Revocation,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code,
                    Permissions.Scopes.Email,
                    Permissions.Scopes.Profile,
                    Permissions.Prefixes.Scope + TellmaIdentityConstants.ApiScope,
                }.SetEquals(descriptor.Permissions),
                string.Join(' ', descriptor.Permissions));
            Assert.DoesNotContain(descriptor.Permissions, static permission => permission.StartsWith(Permissions.Prefixes.Resource, StringComparison.Ordinal));
            Assert.Equal([Requirements.Features.ProofKeyForCodeExchange], descriptor.Requirements);

            Assert.True(TellmaClientProperties.IsSet(descriptor.Properties, TellmaClientProperties.DocumentClient));
            Assert.False(descriptor.Properties.ContainsKey(TellmaClientProperties.FirstParty));
            Assert.False(descriptor.Properties.ContainsKey(TellmaClientProperties.CallsDistributionApis));
            Assert.Equal(
                Created,
                DateTimeOffset.Parse(TellmaClientProperties.Get(descriptor.Properties, TellmaClientProperties.CreatedUtc)!, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void A_private_key_jwt_document_gives_a_confidential_row_holding_its_keys()
        {
            JsonWebKeySet keys = KeySet();
            OpenIddictApplicationDescriptor descriptor = ClientDescriptorFactory.DocumentClient(
                Document(redirectUris: ["https://agent.example.com/callback"], keys: keys, name: null), Created);

            Assert.Equal(ClientTypes.Confidential, descriptor.ClientType);
            Assert.Equal(ApplicationTypes.Web, descriptor.ApplicationType);
            Assert.Null(descriptor.ClientSecret);
            Assert.Same(keys, descriptor.JsonWebKeySet);

            // With no client_name, the host names the client.
            Assert.Equal("agent.example.com", descriptor.DisplayName);
        }

        [Fact]
        public async Task The_application_manager_accepts_both_kinds_of_row()
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            using IServiceScope scope = _services.CreateScope();
            IOpenIddictApplicationManager manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

            await manager.CreateAsync(ClientDescriptorFactory.DocumentClient(Document(), Created), cancellationToken);
            await manager.CreateAsync(
                ClientDescriptorFactory.DocumentClient(Document(clientId: "https://agent.example.com/jwt.json", keys: KeySet()), Created),
                cancellationToken);

            Assert.Equal(2, Applications.Rows.Count);
        }

        [Fact]
        public async Task A_document_client_row_is_created_left_alone_and_rewritten_when_its_document_changes()
        {
            Assert.Equal(DocumentClientConvergence.Created, await ConvergeAsync(Document(), Created));
            int updates = Applications.Updates;

            // The same document, fetched again later, writes nothing.
            Assert.Equal(DocumentClientConvergence.Unchanged, await ConvergeAsync(Document(), Created.AddHours(1)));
            Assert.Equal(updates, Applications.Updates);

            // A changed document rewrites the row, which keeps the time it was first created.
            Assert.Equal(
                DocumentClientConvergence.Updated,
                await ConvergeAsync(Document(name: "Renamed Agent", redirectUris: [MetadataDocuments.LoopbackRedirect, "http://[::1]/callback"]), Created.AddHours(2)));
            InMemoryApplication row = Applications.Find(MetadataDocuments.ClientId)!;
            Assert.Equal("Renamed Agent", row.DisplayName);
            Assert.Equal(2, row.RedirectUris.Length);
            Assert.Equal(
                Created,
                DateTimeOffset.Parse(TellmaClientProperties.Get(row.Properties, TellmaClientProperties.CreatedUtc)!, CultureInfo.InvariantCulture));
        }

        [Fact]
        public async Task A_rotated_key_set_rewrites_the_row()
        {
            const string clientId = "https://agent.example.com/jwt.json";
            Assert.Equal(DocumentClientConvergence.Created, await ConvergeAsync(Document(clientId: clientId, keys: KeySet()), Created));
            Assert.Equal(DocumentClientConvergence.Unchanged, await ConvergeAsync(Document(clientId: clientId, keys: KeySet()), Created));

            Assert.Equal(DocumentClientConvergence.Updated, await ConvergeAsync(Document(clientId: clientId, keys: KeySet(kid: "rotated")), Created));
            Assert.Equal("rotated", Assert.Single(Applications.Find(clientId)!.JsonWebKeySet!.Keys).Kid);
        }

        [Fact]
        public async Task At_the_cap_a_new_document_client_is_refused_and_an_existing_one_still_converges()
        {
            _options.ClientMetadata.MaxClients = 1;
            Assert.Equal(DocumentClientConvergence.Created, await ConvergeAsync(Document(), Created));

            Assert.Equal(DocumentClientConvergence.Cap, await ConvergeAsync(Document(clientId: "https://other.example.com/c.json"), Created));
            Assert.Null(Applications.Find("https://other.example.com/c.json"));

            Assert.Equal(DocumentClientConvergence.Updated, await ConvergeAsync(Document(name: "Renamed"), Created));
        }

        [Fact]
        public async Task A_row_that_is_not_a_document_clients_is_never_written()
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            using (IServiceScope scope = _services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>().CreateAsync(
                    new OpenIddictApplicationDescriptor
                    {
                        ClientId = MetadataDocuments.ClientId,
                        DisplayName = "Someone else",
                        ClientType = ClientTypes.Public,
                        RedirectUris = { new Uri("https://someone.example.com/cb") },
                        Permissions = { Permissions.Endpoints.Authorization },
                    },
                    cancellationToken);
            }

            int updates = Applications.Updates;

            Assert.Equal(DocumentClientConvergence.Registered, await ConvergeAsync(Document(), Created));
            Assert.Equal(updates, Applications.Updates);
            Assert.Equal("Someone else", Applications.Find(MetadataDocuments.ClientId)!.DisplayName);
        }

        [Fact]
        public void Key_sets_are_compared_by_their_public_keys()
        {
            JsonWebKeySet first = KeySet();
            Assert.True(ClientRegistrationConverger.HaveSameKeys(null, null));
            Assert.False(ClientRegistrationConverger.HaveSameKeys(first, null));
            Assert.False(ClientRegistrationConverger.HaveSameKeys(null, first));
            Assert.True(ClientRegistrationConverger.HaveSameKeys(first, KeySet()));
            Assert.False(ClientRegistrationConverger.HaveSameKeys(first, KeySet(kid: "other")));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _services.Dispose();
        }

        /// <summary>A key set: the first one again, or a fresh key under another key id.</summary>
        private static JsonWebKeySet KeySet(string? kid = null)
        {
            return JsonWebKeySet.Create(kid is null ? FirstKeySet : MetadataDocuments.KeySet(MetadataDocuments.RsaKey(kid: kid)));
        }

        /// <summary>A validated document, as the resolver would hand it over.</summary>
        private static ClientMetadataDocument Document(
            string clientId = MetadataDocuments.ClientId,
            string? name = "Example Agent",
            string[]? redirectUris = null,
            JsonWebKeySet? keys = null)
        {
            return new ClientMetadataDocument(
                new Uri(clientId),
                name,
                [.. (redirectUris ?? [MetadataDocuments.LoopbackRedirect]).Select(static uri => new Uri(uri))],
                keys is null ? ClientMetadataDocumentParser.AuthMethodNone : ClientMetadataDocumentParser.AuthMethodPrivateKeyJwt,
                keys is null ? null : new Uri(clientId.Replace(".json", ".jwks", StringComparison.Ordinal)),
                keys,
                Created,
                Created.AddHours(1));
        }

        /// <summary>Converges a document's row the way the handler does.</summary>
        private async Task<DocumentClientConvergence> ConvergeAsync(ClientMetadataDocument document, DateTimeOffset now)
        {
            using IServiceScope scope = _services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ClientRegistrationConverger>()
                .ConvergeDocumentClientAsync(document, now, TestContext.Current.CancellationToken);
        }

        /// <summary>Counts the document-client rows of the in-memory store, as the SQL query does.</summary>
        /// <param name="applications">The in-memory application rows.</param>
        private sealed class InMemoryDocumentClientStore(InMemoryApplicationStore applications) : IDocumentClientStore
        {
            /// <inheritdoc />
            public Task<int> CountAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(applications.Rows.Count(static row =>
                    row.ClientId!.StartsWith("https://", StringComparison.Ordinal)
                    && TellmaClientProperties.IsSet(row.Properties, TellmaClientProperties.DocumentClient)));
            }

            /// <inheritdoc />
            public Task<int> PruneAsync(DateTimeOffset createdBefore, DateTimeOffset now, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }
    }
}
