// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using System.Text.Json;
using Tellma.Identity.Options;
using Tellma.Identity.Services;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.Provisioning;
using Tellma.Identity.Tests.Provisioning;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Tests.Services
{
    /// <summary>
    ///     The request-time resource decision: what each kind of requester is evaluated against —
    ///     its own grants, the distribution-origin list for a document client and (exactly) for the
    ///     control plane, or the presented grant's own resources — and that every refusal is
    ///     audited. Runs OpenIddict's real managers over in-memory stores.
    /// </summary>
    public sealed class ResourceGrantValidatorTests : IAsyncLifetime
    {
        private const string Issuer = "https://identity.tellma.com";
        private const string Origin = "https://acme.example.com";
        private const string OtherOrigin = "https://globex.example.com";
        private const string OffListOrigin = "https://initech.example.com";

        private const string CliId = "tellma-cli";
        private const string ControlPlaneId = "tellma-control-plane";
        private const string DocumentClientId = "https://agent.example.com/client.json";
        private const string PathGrantId = "path-grant";

        private readonly ServiceProvider _services = BuildServices();

        /// <summary>The audit rows the validator wrote.</summary>
        private List<AuditEventEntry> Audits => _services.GetRequiredService<RecordingAuditLogger>().Entries;

        [Fact]
        public async Task A_request_naming_no_resource_has_nothing_to_refuse()
        {
            Assert.Null(await FindRefusedAsync(Client(CliId, [])));
            Assert.Null(await FindRefusedAsync(Client("unknown-client", [])));
            Assert.Empty(Audits);
        }

        [Fact]
        public async Task A_client_is_granted_its_origin_and_the_tenant_resources_under_it()
        {
            Assert.Null(await FindRefusedAsync(Client(CliId, [Origin])));
            Assert.Null(await FindRefusedAsync(Client(CliId, [Origin + "/17/mcp"])));
            Assert.Null(await FindRefusedAsync(Client(CliId, [Origin, Origin + "/17/mcp", Origin + "/18/mcp"])));
            Assert.Empty(Audits);
        }

        [Theory]
        [InlineData(Origin + "/017/mcp")]
        [InlineData(Origin + "/17/mcp/")]
        [InlineData(Origin + "/17/other")]
        [InlineData(Origin + "/0/mcp")]
        [InlineData(OtherOrigin)]
        [InlineData(OtherOrigin + "/17/mcp")]
        [InlineData(TellmaIdentityConstants.ControlPlaneAudience)]
        [InlineData(Issuer)]
        public async Task A_client_is_refused_anything_else_and_the_refusal_is_audited(string requested)
        {
            Assert.Equal(requested, await FindRefusedAsync(Client(CliId, [Origin, requested])));

            AuditEventEntry audit = Assert.Single(Audits);
            Assert.Equal(AuditActions.ResourceRefused, audit.Action);
            Assert.Equal(CliId, audit.ClientId);
            Assert.Equal("failure", audit.Outcome);
            using var details = JsonDocument.Parse(audit.DetailsJson!);
            Assert.Equal(requested, details.RootElement.GetProperty("requested").GetString());
        }

        [Fact]
        public async Task A_client_unknown_to_the_store_is_refused_everything()
        {
            Assert.Equal(Origin, await FindRefusedAsync(Client("unknown-client", [Origin])));
            Assert.Equal("unknown-client", Assert.Single(Audits).ClientId);
        }

        [Fact]
        public async Task A_granted_resource_with_a_path_is_granted_exactly_and_anchors_nothing()
        {
            Assert.Null(await FindRefusedAsync(Client(PathGrantId, [Origin + "/17/mcp"])));
            Assert.Equal(Origin + "/18/mcp", await FindRefusedAsync(Client(PathGrantId, [Origin + "/18/mcp"])));
            Assert.Equal(Origin, await FindRefusedAsync(Client(PathGrantId, [Origin])));
        }

        [Fact]
        public async Task A_document_client_is_granted_the_distribution_origin_list_and_its_tenant_resources()
        {
            Assert.Null(await FindRefusedAsync(Client(DocumentClientId, [Origin])));
            Assert.Null(await FindRefusedAsync(Client(DocumentClientId, [OtherOrigin + "/17/mcp"])));

            // An origin outside the list is refused, and so is a list entry that is not an origin.
            Assert.Equal(OffListOrigin, await FindRefusedAsync(Client(DocumentClientId, [OffListOrigin])));
            Assert.Equal(
                OffListOrigin + "/17/mcp",
                await FindRefusedAsync(Client(DocumentClientId, [OffListOrigin + "/17/mcp"])));
            Assert.Equal(
                "https://malformed.example.com/17/mcp",
                await FindRefusedAsync(Client(DocumentClientId, ["https://malformed.example.com/17/mcp"])));
        }

        [Fact]
        public async Task The_control_plane_may_name_a_listed_origin_exactly_with_its_scope_through_client_credentials()
        {
            Assert.Null(await FindRefusedAsync(ControlPlane([Origin])));
            Assert.Null(await FindRefusedAsync(ControlPlane([OtherOrigin])));
            Assert.Empty(Audits);
        }

        [Theory]
        [InlineData(Origin + "/17/mcp")]
        [InlineData(OffListOrigin)]
        [InlineData("https://malformed.example.com/")]
        [InlineData(Issuer + "/17/mcp")]
        public async Task The_control_plane_gets_no_tenant_pattern_and_no_origin_outside_the_list(string requested)
        {
            Assert.Equal(requested, await FindRefusedAsync(ControlPlane([requested])));
            Assert.Equal(ControlPlaneId, Assert.Single(Audits).ClientId);
        }

        [Fact]
        public async Task The_control_plane_names_no_origin_without_its_scope_or_outside_client_credentials()
        {
            // Without the control-plane scope the list is not open to it.
            Assert.Equal(
                Origin,
                await FindRefusedAsync(new ResourceGrantRequest(
                    ControlPlaneId, [Origin], [TellmaIdentityConstants.IdentityScope], GrantTypes.ClientCredentials, null)));

            // Nor through any grant type but client credentials, nor at the authorization endpoint.
            Assert.Equal(
                Origin,
                await FindRefusedAsync(new ResourceGrantRequest(
                    ControlPlaneId, [Origin], [TellmaIdentityConstants.ControlPlaneScope], GrantTypes.TokenExchange, null)));
            Assert.Equal(
                Origin,
                await FindRefusedAsync(new ResourceGrantRequest(
                    ControlPlaneId, [Origin], [TellmaIdentityConstants.ControlPlaneScope], null, null)));
        }

        [Fact]
        public async Task The_control_plane_holds_its_platform_audiences_exactly()
        {
            Assert.Null(await FindRefusedAsync(ControlPlane([TellmaIdentityConstants.ControlPlaneAudience])));
            Assert.Null(await FindRefusedAsync(ControlPlane([Issuer])));
        }

        [Fact]
        public async Task Another_client_never_reaches_the_distribution_origin_list()
        {
            // The CLI holds only the origin it was granted; the list's other entries stay closed to
            // it even when it asks with the control-plane scope's name.
            Assert.Equal(
                OtherOrigin,
                await FindRefusedAsync(new ResourceGrantRequest(
                    CliId, [OtherOrigin], [TellmaIdentityConstants.ControlPlaneScope], GrantTypes.ClientCredentials, null)));
        }

        [Theory]
        [InlineData(OtherOrigin)]
        [InlineData(TellmaIdentityConstants.ControlPlaneAudience)]
        [InlineData(Issuer)]
        public async Task A_control_plane_token_naming_a_distribution_names_nothing_else(string other)
        {
            // One token for a distribution is valid in one place: not at a second distribution,
            // and not at the identity server's own APIs.
            Assert.Equal(other, await FindRefusedAsync(ControlPlane([Origin, other])));

            // In either order; the refusal names a resource the distribution's cannot ride with.
            Assert.NotNull(await FindRefusedAsync(ControlPlane([other, Origin])));
            Assert.Equal(2, Audits.Count);
            Assert.All(Audits, static audit => Assert.Equal(ControlPlaneId, audit.ClientId));
        }

        [Fact]
        public async Task A_control_plane_token_may_name_both_platform_audiences()
        {
            Assert.Null(await FindRefusedAsync(ControlPlane([TellmaIdentityConstants.ControlPlaneAudience, Issuer])));
        }

        [Theory]
        [InlineData(GrantTypes.ClientCredentials)]
        [InlineData(GrantTypes.TokenExchange)]
        public async Task A_machine_token_naming_a_distribution_names_no_other_resource(string grantType)
        {
            // The CLI holds the origin, so each resource is granted on its own; together they
            // would be one token valid at the origin and at a tenant's endpoint.
            Assert.Equal(
                Origin + "/17/mcp",
                await FindRefusedAsync(new ResourceGrantRequest(
                    CliId, [Origin, Origin + "/17/mcp"], [TellmaIdentityConstants.ApiScope], grantType, null)));
            Assert.Null(await FindRefusedAsync(new ResourceGrantRequest(
                CliId, [Origin + "/17/mcp"], [TellmaIdentityConstants.ApiScope], grantType, null)));
        }

        [Theory]
        [InlineData(Origin)]
        [InlineData(Origin + "/17/mcp")]
        public async Task An_interactive_grant_naming_a_distribution_never_also_asks_for_the_management_scope(string resource)
        {
            Assert.Equal(
                resource,
                await FindRefusedAsync(new ResourceGrantRequest(
                    CliId, [resource], [Scopes.OpenId, TellmaIdentityConstants.ApiScope, TellmaIdentityConstants.IdentityScope], null, null)));

            // Without the management scope the same request stands, and so does one naming several
            // of the client's distribution resources: the combination rule binds machine tokens.
            Assert.Null(await FindRefusedAsync(Client(CliId, [resource])));
            Assert.Null(await FindRefusedAsync(Client(CliId, [Origin, Origin + "/17/mcp"])));
        }

        [Fact]
        public async Task A_stored_grant_request_is_never_refused_for_its_combination()
        {
            // The combination was decided when the grant was made; a later request only narrows it.
            Assert.Null(await FindRefusedAsync(new ResourceGrantRequest(
                CliId, [Origin, Origin + "/17/mcp"], [TellmaIdentityConstants.IdentityScope], GrantTypes.RefreshToken, [Origin])));
        }

        [Fact]
        public async Task A_stored_tenant_grant_narrows_to_itself_and_never_widens()
        {
            string[] stored = [Origin + "/17/mcp"];

            Assert.Null(await FindRefusedAsync(Stored(GrantTypes.RefreshToken, stored, [Origin + "/17/mcp"])));
            Assert.Equal(
                Origin + "/18/mcp",
                await FindRefusedAsync(Stored(GrantTypes.RefreshToken, stored, [Origin + "/18/mcp"])));
            Assert.Equal(Origin, await FindRefusedAsync(Stored(GrantTypes.RefreshToken, stored, [Origin])));
        }

        [Theory]
        [InlineData(GrantTypes.AuthorizationCode)]
        [InlineData(GrantTypes.DeviceCode)]
        [InlineData(GrantTypes.RefreshToken)]
        public async Task A_stored_origin_grant_may_narrow_to_one_tenant(string grantType)
        {
            string[] stored = [Origin, Issuer];

            Assert.Null(await FindRefusedAsync(Stored(grantType, stored, [Origin + "/17/mcp"])));
            Assert.Null(await FindRefusedAsync(Stored(grantType, stored, [Origin])));
            Assert.Equal(OtherOrigin, await FindRefusedAsync(Stored(grantType, stored, [OtherOrigin])));
            Assert.Equal(OtherOrigin + "/17/mcp", await FindRefusedAsync(Stored(grantType, stored, [OtherOrigin + "/17/mcp"])));
        }

        [Fact]
        public async Task A_stored_grant_is_evaluated_against_itself_alone_never_the_clients_grants()
        {
            // The CLI holds the origin, but the grant it presents was for one tenant only.
            Assert.Equal(
                Origin,
                await FindRefusedAsync(Stored(GrantTypes.RefreshToken, [Origin + "/17/mcp"], [Origin])));

            // A stored grant with no resources narrows to nothing.
            Assert.Equal(Origin, await FindRefusedAsync(Stored(GrantTypes.RefreshToken, [], [Origin])));
        }

        [Fact]
        public async Task An_oversized_refused_resource_is_quoted_cut_short()
        {
            string requested = Origin + "/" + new string('a', 2000);

            Assert.Equal(requested, await FindRefusedAsync(Client(CliId, [requested])));

            using var details = JsonDocument.Parse(Assert.Single(Audits).DetailsJson!);
            Assert.Equal(
                ResourceGrantValidator.MaxQuotedResourceLength,
                details.RootElement.GetProperty("requested").GetString()!.Length);
            Assert.True(details.RootElement.GetProperty("truncated").GetBoolean());
        }

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await SeedAsync();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
        }

        /// <summary>A request against the client's own grants, at the authorization endpoint.</summary>
        private static ResourceGrantRequest Client(string clientId, string[] resources)
        {
            return new ResourceGrantRequest(clientId, resources, [Scopes.OpenId, TellmaIdentityConstants.ApiScope], null, null);
        }

        /// <summary>A control-plane client-credentials request with its scope.</summary>
        private static ResourceGrantRequest ControlPlane(string[] resources)
        {
            return new ResourceGrantRequest(
                ControlPlaneId, resources, [TellmaIdentityConstants.ControlPlaneScope], GrantTypes.ClientCredentials, null);
        }

        /// <summary>A CLI token request presenting a stored grant.</summary>
        private static ResourceGrantRequest Stored(string grantType, string[] stored, string[] resources)
        {
            return new ResourceGrantRequest(CliId, resources, [], grantType, stored);
        }

        /// <summary>Builds the services the validator runs on.</summary>
        private static ServiceProvider BuildServices()
        {
            ServiceCollection services = new();
            services.AddInMemoryOpenIddict();
            services.AddSingleton<RecordingAuditLogger>();
            services.AddSingleton<IAuditLogger>(static provider => provider.GetRequiredService<RecordingAuditLogger>());
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddSingleton<IResourceGrantEvaluator, ResourceGrantEvaluator>();
            services.AddScoped<ResourceGrantValidator>();
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        /// <summary>Runs the validator in a scope of its own.</summary>
        private async Task<string?> FindRefusedAsync(ResourceGrantRequest request)
        {
            using IServiceScope scope = _services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ResourceGrantValidator>()
                .FindRefusedAsync(request, TestContext.Current.CancellationToken);
        }

        /// <summary>
        ///     Seeds the distribution-origin list (two origins, plus a malformed entry that must never
        ///     count) and one client of each kind the decision tells apart.
        /// </summary>
        private async Task SeedAsync()
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            TellmaIdentityOptions options = new() { Issuer = new Uri(Issuer) };
            using IServiceScope scope = _services.CreateScope();
            IOpenIddictScopeManager scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
            IOpenIddictApplicationManager applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

            OpenIddictScopeDescriptor api = new() { Name = TellmaIdentityConstants.ApiScope };
            api.Resources.UnionWith([Origin, OtherOrigin, "https://malformed.example.com/"]);
            await scopeManager.CreateAsync(api, cancellationToken);

            await applicationManager.CreateAsync(
                ClientDescriptorFactory.SeededClient(
                    options,
                    new TellmaIdentitySeedClientOptions
                    {
                        ClientId = CliId,
                        Kind = TellmaIdentitySeedClientKind.Cli,
                        RedirectUris = { "http://127.0.0.1/callback" },
                        Resources = { Origin },
                    }),
                cancellationToken);
            await applicationManager.CreateAsync(
                ClientDescriptorFactory.SeededClient(
                    options,
                    new TellmaIdentitySeedClientOptions
                    {
                        ClientId = ControlPlaneId,
                        Kind = TellmaIdentitySeedClientKind.ControlPlane,
                        ClientSecret = "control-plane-secret-0123456789abcdef",
                    }),
                cancellationToken);
            await applicationManager.CreateAsync(
                ClientDescriptorFactory.SeededClient(
                    options,
                    new TellmaIdentitySeedClientOptions
                    {
                        ClientId = PathGrantId,
                        Kind = TellmaIdentitySeedClientKind.Native,
                        RedirectUris = { "http://127.0.0.1/callback" },
                        Resources = { Origin + "/17/mcp" },
                    }),
                cancellationToken);

            // A document client stores no resource permission; the marker alone opens the list.
            OpenIddictApplicationDescriptor document = new()
            {
                ClientId = DocumentClientId,
                ClientType = ClientTypes.Public,
                ApplicationType = ApplicationTypes.Native,
                ConsentType = ConsentTypes.Explicit,
                RedirectUris = { new Uri("http://127.0.0.1/callback") },
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.ResponseTypes.Code,
                    Permissions.Prefixes.Scope + TellmaIdentityConstants.ApiScope,
                },
            };
            TellmaClientProperties.Set(document.Properties, TellmaClientProperties.DocumentClient, "true");
            await applicationManager.CreateAsync(document, cancellationToken);
        }

        /// <summary>Records audit entries in memory.</summary>
        private sealed class RecordingAuditLogger : IAuditLogger
        {
            /// <summary>Every recorded entry.</summary>
            public List<AuditEventEntry> Entries { get; } = [];

            /// <inheritdoc />
            public Task LogAsync(AuditEventEntry entry, CancellationToken cancellationToken = default)
            {
                Entries.Add(entry);
                return Task.CompletedTask;
            }
        }
    }
}
