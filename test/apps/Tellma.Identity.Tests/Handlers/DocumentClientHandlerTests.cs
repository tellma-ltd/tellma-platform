// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using System.Text.Json;
using Tellma.Identity.Handlers;
using Tellma.Identity.Options;
using Tellma.Identity.Services;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.ClientMetadata;
using Tellma.Identity.Services.Provisioning;
using Tellma.Identity.Tests.Provisioning;
using Tellma.Identity.Tests.Services.ClientMetadata;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Tellma.Identity.Tests.Handlers
{
    /// <summary>
    ///     The document-client pipeline handler and the discovery advertisement: where the
    ///     handler sits, which requests it leaves alone, how it finds the client id of a client
    ///     that names itself only in its assertion, that an unresolvable document or a full cap
    ///     refuses the request with <c>invalid_client</c>, and that a resolved document leaves a
    ///     row behind for the built-in checks to read.
    /// </summary>
    public sealed class DocumentClientHandlerTests : IDisposable
    {
        /// <summary>The engine options, the cap among them.</summary>
        private readonly TellmaIdentityOptions _options = new() { Issuer = new Uri("https://id.tellma.com") };

        /// <summary>The in-memory stores, the converger and the audit log.</summary>
        private readonly ServiceProvider _services;

        /// <summary>The documents the fake resolver answers with, and the ids it was asked for.</summary>
        private readonly FakeResolver _resolver = new();

        /// <summary>The document cache the handler drops a copy the row cannot take from.</summary>
        private readonly ClientMetadataCache _cache;

        /// <summary>Builds the services over in-memory stores.</summary>
        public DocumentClientHandlerTests()
        {
            _cache = new ClientMetadataCache(Microsoft.Extensions.Options.Options.Create(_options));
            ServiceCollection services = new();
            services.AddInMemoryOpenIddict();
            services.AddSingleton<AuditRecorder>();
            services.AddScoped<IAuditLogger>(static provider => provider.GetRequiredService<AuditRecorder>());
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(_options));
            services.AddSingleton(TimeProvider.System);
            services.AddScoped<ClientRegistrationConverger>();
            services.AddScoped<IDocumentClientStore, ZeroDocumentClientStore>();
            _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        [Fact]
        public void The_handler_runs_after_token_extraction_and_before_the_client_assertion_is_validated()
        {
            int order = DocumentClientHandler.Descriptor.Order;

            Assert.True(order > OpenIddictServerHandlers.ValidateRequiredTokens.Descriptor.Order);
            Assert.True(order < OpenIddictServerHandlers.ValidateClientAssertion.Descriptor.Order);
            Assert.True(order < OpenIddictServerHandlers.ValidateClientId.Descriptor.Order);
        }

        [Theory]
        [InlineData("tellma-cli")]
        [InlineData("http://agent.example.com/client.json")]
        public async Task A_registered_client_is_left_to_the_built_in_checks(string clientId)
        {
            ProcessAuthenticationContext context = Context(OpenIddictServerEndpointType.Token, new OpenIddictRequest { ClientId = clientId });

            await HandleAsync(context);

            Assert.False(context.IsRejected);
            Assert.Empty(_resolver.Asked);
        }

        [Theory]
        [InlineData(OpenIddictServerEndpointType.Introspection)]
        [InlineData(OpenIddictServerEndpointType.EndSession)]
        [InlineData(OpenIddictServerEndpointType.DeviceAuthorization)]
        public async Task An_endpoint_a_document_client_has_no_permission_for_is_left_alone(OpenIddictServerEndpointType endpoint)
        {
            ProcessAuthenticationContext context = Context(endpoint, new OpenIddictRequest { ClientId = MetadataDocuments.ClientId });

            await HandleAsync(context);

            Assert.False(context.IsRejected);
            Assert.Empty(_resolver.Asked);
        }

        [Fact]
        public async Task An_unresolvable_document_is_invalid_client()
        {
            _resolver.Refusal = new ClientMetadataException(ClientMetadataRejectionReasons.Unreachable, "unreachable");
            ProcessAuthenticationContext context = Context(OpenIddictServerEndpointType.Token, new OpenIddictRequest { ClientId = MetadataDocuments.ClientId });

            await HandleAsync(context);

            Assert.True(context.IsRejected);
            Assert.Equal(Errors.InvalidClient, context.Error);
        }

        [Fact]
        public async Task A_client_naming_itself_only_in_its_assertion_is_resolved_by_the_assertions_issuer()
        {
            _resolver.Refusal = new ClientMetadataException(ClientMetadataRejectionReasons.Unreachable, "unreachable");
            string assertion = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor { Issuer = MetadataDocuments.ClientId });
            ProcessAuthenticationContext context = Context(OpenIddictServerEndpointType.Token, new OpenIddictRequest
            {
                ClientAssertion = assertion,
                ClientAssertionType = ClientAssertionTypes.JwtBearer,
            });

            await HandleAsync(context);

            Assert.Equal(MetadataDocuments.ClientId, Assert.Single(_resolver.Asked).OriginalString);
            Assert.True(context.IsRejected);
        }

        [Fact]
        public async Task A_resolved_document_leaves_its_row_for_the_built_in_checks()
        {
            _resolver.Document = Document();
            ProcessAuthenticationContext context = Context(OpenIddictServerEndpointType.PushedAuthorization, new OpenIddictRequest
            {
                ClientId = MetadataDocuments.ClientId,
                RedirectUri = "http://127.0.0.1:50123/callback",
            });

            await HandleAsync(context);

            Assert.False(context.IsRejected);
            Assert.NotNull(_services.GetRequiredService<InMemoryApplicationStore>().Find(MetadataDocuments.ClientId));

            // The requested loopback port is one the document's redirect already covers.
            Assert.Equal(0, _resolver.Revalidations);
        }

        [Fact]
        public async Task An_unlisted_redirect_fetches_the_document_again_before_the_redirect_check()
        {
            _resolver.Document = Document();
            ProcessAuthenticationContext context = Context(OpenIddictServerEndpointType.Authorization, new OpenIddictRequest
            {
                ClientId = MetadataDocuments.ClientId,
                RedirectUri = "https://agent.example.com/new-callback",
            });

            await HandleAsync(context);

            Assert.Equal(1, _resolver.Revalidations);
        }

        [Fact]
        public async Task At_the_cap_a_new_document_client_is_invalid_client_with_the_cap_reason()
        {
            _options.ClientMetadata.MaxClients = 0;
            _resolver.Document = Document();
            _cache.Set(_resolver.Document, DateTimeOffset.UtcNow);
            ProcessAuthenticationContext context = Context(OpenIddictServerEndpointType.Token, new OpenIddictRequest { ClientId = MetadataDocuments.ClientId });

            await HandleAsync(context);

            Assert.True(context.IsRejected);
            Assert.Equal(Errors.InvalidClient, context.Error);
            AuditEventEntry entry = Assert.Single(_services.GetRequiredService<AuditRecorder>().Entries);
            Assert.Equal(AuditActions.ClientMetadataRejected, entry.Action);
            Assert.Equal("cap", JsonDocument.Parse(entry.DetailsJson!).RootElement.GetProperty("reason").GetString());
            Assert.Null(_services.GetRequiredService<InMemoryApplicationStore>().Find(MetadataDocuments.ClientId));

            // A copy no row can take holds no cache room.
            Assert.False(_cache.TryGet(MetadataDocuments.ClientId, out _));
        }

        [Fact]
        public async Task A_document_the_store_refuses_is_invalid_client_with_the_document_reason()
        {
            // The protocol engine splits a query on ';' as well as '&', so it refuses this
            // redirect for carrying its own 'iss' parameter. The refusal is the document's fault,
            // not a server error.
            DateTimeOffset now = DateTimeOffset.UtcNow;
            _resolver.Document = new ClientMetadataDocument(
                new Uri(MetadataDocuments.ClientId),
                "Example Agent",
                [new Uri("https://agent.example.com/cb?a=1;iss=x")],
                ClientMetadataDocumentParser.AuthMethodNone,
                null,
                null,
                now,
                now.AddMinutes(10));
            _cache.Set(_resolver.Document, now);
            ProcessAuthenticationContext context = Context(OpenIddictServerEndpointType.Token, new OpenIddictRequest { ClientId = MetadataDocuments.ClientId });

            await HandleAsync(context);

            Assert.True(context.IsRejected);
            Assert.Equal(Errors.InvalidClient, context.Error);
            AuditEventEntry entry = Assert.Single(_services.GetRequiredService<AuditRecorder>().Entries);
            Assert.Equal("document", JsonDocument.Parse(entry.DetailsJson!).RootElement.GetProperty("reason").GetString());
            Assert.Null(_services.GetRequiredService<InMemoryApplicationStore>().Find(MetadataDocuments.ClientId));
            Assert.False(_cache.TryGet(MetadataDocuments.ClientId, out _));
        }

        [Fact]
        public async Task A_copy_older_than_the_rows_proceeds_on_the_row_and_is_dropped()
        {
            // Another instance wrote the row from a newer copy, which added a redirect URI. This
            // instance's older copy must neither rewrite the row nor refuse the request.
            DateTimeOffset now = DateTimeOffset.UtcNow;
            ClientMetadataDocument newer = new(
                new Uri(MetadataDocuments.ClientId),
                "Example Agent",
                [new Uri(MetadataDocuments.LoopbackRedirect), new Uri("https://agent.example.com/new-callback")],
                ClientMetadataDocumentParser.AuthMethodNone,
                null,
                null,
                now,
                now.AddMinutes(10));
            _resolver.Document = newer;
            await HandleAsync(Context(OpenIddictServerEndpointType.Token, new OpenIddictRequest { ClientId = MetadataDocuments.ClientId }));

            _resolver.Document = Document() with { FetchedAt = now.AddMinutes(-5) };
            _cache.Set(_resolver.Document, now);
            ProcessAuthenticationContext context = Context(OpenIddictServerEndpointType.Token, new OpenIddictRequest { ClientId = MetadataDocuments.ClientId });

            await HandleAsync(context);

            Assert.False(context.IsRejected);
            Assert.Equal(2, _services.GetRequiredService<InMemoryApplicationStore>().Find(MetadataDocuments.ClientId)!.RedirectUris.Length);
            Assert.False(_cache.TryGet(MetadataDocuments.ClientId, out _));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Discovery_advertises_document_clients_only_while_they_are_on(bool enabled)
        {
            _options.ClientMetadata.Enabled = enabled;
            HandleConfigurationRequestContext context = new(new OpenIddictServerTransaction
            {
                EndpointType = OpenIddictServerEndpointType.Configuration,
                Logger = NullLogger.Instance,
                Options = new OpenIddictServerOptions(),
            });

            await new AdvertiseDocumentClientsHandler(Microsoft.Extensions.Options.Options.Create(_options)).HandleAsync(context);

            Assert.Equal(enabled, context.Metadata.ContainsKey(AdvertiseDocumentClientsHandler.SupportedMetadata));
            Assert.Equal(enabled, context.TokenEndpointAuthenticationMethods.Contains(ClientAuthenticationMethods.None));
        }

        [Fact]
        public void The_assertion_issuer_is_read_without_validation_and_garbage_reads_as_nothing()
        {
            string assertion = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor { Issuer = MetadataDocuments.ClientId });

            Assert.Equal(MetadataDocuments.ClientId, DocumentClientHandler.PeekIssuer(assertion));
            Assert.Null(DocumentClientHandler.PeekIssuer("not-a-jwt"));
            Assert.Null(DocumentClientHandler.PeekIssuer(null));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _services.Dispose();
        }

        /// <summary>A public document for the test client, listing its loopback redirect.</summary>
        private static ClientMetadataDocument Document()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return new ClientMetadataDocument(
                new Uri(MetadataDocuments.ClientId),
                "Example Agent",
                [new Uri(MetadataDocuments.LoopbackRedirect)],
                ClientMetadataDocumentParser.AuthMethodNone,
                null,
                null,
                now,
                now.AddMinutes(10));
        }

        /// <summary>An authentication context at an endpoint.</summary>
        private static ProcessAuthenticationContext Context(OpenIddictServerEndpointType endpoint, OpenIddictRequest request)
        {
            return new ProcessAuthenticationContext(new OpenIddictServerTransaction
            {
                EndpointType = endpoint,
                Logger = NullLogger.Instance,
                Options = new OpenIddictServerOptions(),
                Request = request,
            });
        }

        /// <summary>Runs the handler in a scope of its own, as OpenIddict does.</summary>
        private async Task HandleAsync(ProcessAuthenticationContext context)
        {
            using IServiceScope scope = _services.CreateScope();
            DocumentClientHandler handler = new(
                _resolver,
                scope.ServiceProvider.GetRequiredService<ClientRegistrationConverger>(),
                _cache,
                new ClientMetadataAuditor(_services.GetRequiredService<IServiceScopeFactory>()),
                TimeProvider.System);
            await handler.HandleAsync(context);
        }

        /// <summary>Answers with a configured document or refusal, recording what it was asked.</summary>
        private sealed class FakeResolver : IClientMetadataDocumentResolver
        {
            /// <summary>The document every resolution answers with.</summary>
            public ClientMetadataDocument? Document { get; set; }

            /// <summary>The refusal every resolution throws instead, when set.</summary>
            public ClientMetadataException? Refusal { get; set; }

            /// <summary>The client ids asked for.</summary>
            public List<Uri> Asked { get; } = [];

            /// <summary>How many revalidations were asked for.</summary>
            public int Revalidations { get; private set; }

            /// <inheritdoc />
            public Task<ClientMetadataDocument?> ResolveAsync(Uri clientId, CancellationToken cancellationToken = default)
            {
                Asked.Add(clientId);
                return Refusal is { } refusal ? Task.FromException<ClientMetadataDocument?>(refusal) : Task.FromResult(Document);
            }

            /// <inheritdoc />
            public Task<ClientMetadataDocument?> RevalidateAsync(Uri clientId, CancellationToken cancellationToken = default)
            {
                Revalidations++;
                return Task.FromResult(Document);
            }
        }

        /// <summary>A store with no document-client rows, so only the configured cap decides.</summary>
        private sealed class ZeroDocumentClientStore : IDocumentClientStore
        {
            /// <inheritdoc />
            public Task<int> CountAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(0);
            }

            /// <inheritdoc />
            public Task<string?> FindClientIdAsync(string clientId, CancellationToken cancellationToken)
            {
                return Task.FromResult<string?>(null);
            }

            /// <inheritdoc />
            public Task<bool> IsAuthorizedAsync(string clientId, CancellationToken cancellationToken)
            {
                return Task.FromResult(false);
            }

            /// <inheritdoc />
            public Task<int> PruneAsync(DateTimeOffset createdBefore, DateTimeOffset now, CancellationToken cancellationToken)
            {
                return Task.FromResult(0);
            }
        }
    }
}
