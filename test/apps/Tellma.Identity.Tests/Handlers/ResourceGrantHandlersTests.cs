// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using System.Collections.Immutable;
using System.Security.Claims;
using Tellma.Identity.Handlers;
using Tellma.Identity.Options;
using Tellma.Identity.Services;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.Provisioning;
using Tellma.Identity.Tests.Provisioning;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Tellma.Identity.Tests.Handlers
{
    /// <summary>
    ///     The resource grant pipeline handlers: a token request against a stored grant is checked
    ///     against that grant alone and refused with <c>invalid_target</c>; the access token, and
    ///     only the access token, narrows to the requested resources; a device request's resources
    ///     ride its codes; and every handler sits where OpenIddict's own permission check would.
    /// </summary>
    public sealed class ResourceGrantHandlersTests : IAsyncLifetime
    {
        private const string Origin = "https://acme.example.com";
        private const string ClientId = "tellma-cli";

        private readonly ServiceProvider _services = BuildServices();

        [Fact]
        public async Task A_refresh_naming_a_resource_outside_its_grant_is_refused_with_invalid_target()
        {
            ValidateTokenRequestContext context = TokenRequest(GrantTypes.RefreshToken, Origin + "/18/mcp");
            context.RefreshTokenPrincipal = GrantPrincipal(Origin + "/17/mcp");

            await HandleAsync(context);

            Assert.True(context.IsRejected);
            Assert.Equal(Errors.InvalidTarget, context.Error);
            Assert.Equal(ResourceGrantValidator.RefusedDescription, context.ErrorDescription);
        }

        [Theory]
        [InlineData(GrantTypes.AuthorizationCode)]
        [InlineData(GrantTypes.DeviceCode)]
        [InlineData(GrantTypes.RefreshToken)]
        public async Task A_stored_grant_request_is_checked_against_its_own_grant(string grantType)
        {
            // The client holds the origin; its stored grant holds one tenant. Narrowing to that
            // tenant passes, the origin itself does not.
            ValidateTokenRequestContext narrowing = TokenRequest(grantType, Origin + "/17/mcp");
            SetStoredPrincipal(narrowing, grantType, GrantPrincipal(Origin + "/17/mcp"));
            await HandleAsync(narrowing);
            Assert.False(narrowing.IsRejected);

            ValidateTokenRequestContext widening = TokenRequest(grantType, Origin);
            SetStoredPrincipal(widening, grantType, GrantPrincipal(Origin + "/17/mcp"));
            await HandleAsync(widening);
            Assert.True(widening.IsRejected);
            Assert.Equal(Errors.InvalidTarget, widening.Error);
        }

        [Fact]
        public async Task A_stored_grant_request_without_its_stored_principal_is_refused()
        {
            // Never fall back to the client's own grants, which would let a request widen.
            ValidateTokenRequestContext context = TokenRequest(GrantTypes.RefreshToken, Origin);

            await HandleAsync(context);

            Assert.True(context.IsRejected);
            Assert.Equal(Errors.InvalidTarget, context.Error);
        }

        [Fact]
        public async Task A_client_credentials_request_is_checked_against_the_clients_grants()
        {
            ValidateTokenRequestContext granted = TokenRequest(GrantTypes.ClientCredentials, Origin + "/17/mcp");
            await HandleAsync(granted);
            Assert.False(granted.IsRejected);

            ValidateTokenRequestContext refused = TokenRequest(GrantTypes.ClientCredentials, "https://globex.example.com");
            await HandleAsync(refused);
            Assert.True(refused.IsRejected);
            Assert.Equal(Errors.InvalidTarget, refused.Error);
        }

        [Fact]
        public async Task A_token_request_naming_no_resource_passes()
        {
            ValidateTokenRequestContext context = TokenRequest(GrantTypes.RefreshToken);

            await HandleAsync(context);

            Assert.False(context.IsRejected);
        }

        [Theory]
        [InlineData(GrantTypes.AuthorizationCode)]
        [InlineData(GrantTypes.DeviceCode)]
        [InlineData(GrantTypes.RefreshToken)]
        public async Task The_access_token_alone_narrows_to_the_requested_resources(string grantType)
        {
            ProcessSignInContext context = SignIn(OpenIddictServerEndpointType.Token, grantType, Origin + "/17/mcp");

            await new NarrowAccessTokenAudienceHandler().HandleAsync(context);

            Assert.Equal([Origin + "/17/mcp"], context.AccessTokenPrincipal!.GetAudiences());

            // The refresh token keeps the grant's resources, so a later refresh is evaluated
            // against the original grant.
            Assert.Equal([Origin, "https://identity.tellma.com"], context.RefreshTokenPrincipal!.GetResources());
        }

        [Fact]
        public async Task An_access_token_keeps_its_audiences_when_the_request_names_no_resource()
        {
            ProcessSignInContext context = SignIn(OpenIddictServerEndpointType.Token, GrantTypes.RefreshToken);

            await new NarrowAccessTokenAudienceHandler().HandleAsync(context);

            Assert.Equal([Origin, "https://identity.tellma.com"], context.AccessTokenPrincipal!.GetAudiences());
        }

        [Theory]
        [InlineData(GrantTypes.ClientCredentials)]
        [InlineData(GrantTypes.TokenExchange)]
        public async Task Machine_grants_are_not_narrowed_by_the_handler(string grantType)
        {
            ProcessSignInContext context = SignIn(OpenIddictServerEndpointType.Token, grantType, Origin + "/17/mcp");

            await new NarrowAccessTokenAudienceHandler().HandleAsync(context);

            Assert.Equal([Origin, "https://identity.tellma.com"], context.AccessTokenPrincipal!.GetAudiences());
        }

        [Fact]
        public void The_narrowing_runs_after_every_principal_is_prepared_and_before_any_token_is_generated()
        {
            int order = NarrowAccessTokenAudienceHandler.Descriptor.Order;

            Assert.True(order > OpenIddictServerHandlers.PrepareAccessTokenPrincipal.Descriptor.Order);
            Assert.True(order > OpenIddictServerHandlers.PrepareRefreshTokenPrincipal.Descriptor.Order);
            Assert.True(order > OpenIddictServerHandlers.PrepareUserCodePrincipal.Descriptor.Order);
            Assert.True(order < OpenIddictServerHandlers.GenerateAccessToken.Descriptor.Order);
            Assert.True(order < OpenIddictServerHandlers.GenerateRefreshToken.Descriptor.Order);
        }

        [Fact]
        public void Every_check_runs_after_the_built_in_permission_checks_it_replaces()
        {
            Assert.True(ValidateAuthorizationResourcesHandler.Descriptor.Order
                > OpenIddictServerHandlers.Authentication.ValidateClientRedirectUri.Descriptor.Order);
            Assert.True(ValidateAuthorizationResourcesHandler.Descriptor.Order
                > OpenIddictServerHandlers.Authentication.ValidateScopePermissions.Descriptor.Order);
            Assert.True(ValidatePushedAuthorizationResourcesHandler.Descriptor.Order
                > OpenIddictServerHandlers.Authentication.ValidatePushedScopePermissions.Descriptor.Order);
            Assert.True(ValidateDeviceAuthorizationResourcesHandler.Descriptor.Order
                > OpenIddictServerHandlers.Device.ValidateDeviceAuthentication.Descriptor.Order);
            Assert.True(ValidateTokenResourcesHandler.Descriptor.Order
                > OpenIddictServerHandlers.Exchange.ValidateAuthentication.Descriptor.Order);
            Assert.True(ValidateTokenResourcesHandler.Descriptor.Order
                > OpenIddictServerHandlers.Exchange.ValidateScopePermissions.Descriptor.Order);
        }

        [Fact]
        public async Task A_device_request_carries_its_scopes_and_resources_into_its_codes()
        {
            HandleDeviceAuthorizationRequestContext context = new(Transaction(
                OpenIddictServerEndpointType.DeviceAuthorization,
                new OpenIddictRequest { ClientId = ClientId, Scope = "openid tellma_api", Resources = AsParameter(Origin + "/17/mcp") }));

            await new AttachDeviceAuthorizationResourcesHandler().HandleAsync(context);

            Assert.NotNull(context.Principal);
            Assert.False(context.Principal.Identity!.IsAuthenticated);
            Assert.Equal([Scopes.OpenId, TellmaIdentityConstants.ApiScope], context.Principal.GetScopes());
            Assert.Equal([Origin + "/17/mcp"], context.Principal.GetResources());
        }

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            // A CLI granted one origin, as onboarding grants it.
            using IServiceScope scope = _services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>().CreateAsync(
                ClientDescriptorFactory.SeededClient(
                    new TellmaIdentityOptions { Issuer = new Uri("https://identity.tellma.com") },
                    new TellmaIdentitySeedClientOptions
                    {
                        ClientId = ClientId,
                        Kind = TellmaIdentitySeedClientKind.Cli,
                        RedirectUris = { "http://127.0.0.1/callback" },
                        Resources = { Origin },
                    }),
                TestContext.Current.CancellationToken);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
        }

        /// <summary>Builds the services the token check runs on.</summary>
        private static ServiceProvider BuildServices()
        {
            ServiceCollection services = new();
            services.AddInMemoryOpenIddict();
            services.AddSingleton<IAuditLogger, NullAuditLogger>();
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddSingleton<IResourceGrantEvaluator, ResourceGrantEvaluator>();
            services.AddScoped<ResourceGrantValidator>();
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        /// <summary>A transaction at an endpoint, carrying a request.</summary>
        private static OpenIddictServerTransaction Transaction(OpenIddictServerEndpointType endpoint, OpenIddictRequest request)
        {
            return new OpenIddictServerTransaction
            {
                EndpointType = endpoint,
                Logger = NullLogger.Instance,
                Options = new OpenIddictServerOptions(),
                Request = request,
            };
        }

        /// <summary>A token request from the CLI, naming the given resources.</summary>
        private static ValidateTokenRequestContext TokenRequest(string grantType, params string[] resources)
        {
            return new ValidateTokenRequestContext(Transaction(
                OpenIddictServerEndpointType.Token,
                new OpenIddictRequest { ClientId = ClientId, GrantType = grantType, Resources = AsParameter(resources) }));
        }

        /// <summary>The <c>resource</c> parameter's value: absent when no resource is named.</summary>
        private static ImmutableArray<string?>? AsParameter(params string[] resources)
        {
            return resources.Length == 0 ? null : ImmutableArray.Create<string?>(resources);
        }

        /// <summary>Attaches a stored principal where the grant type's validation would.</summary>
        private static void SetStoredPrincipal(ValidateTokenRequestContext context, string grantType, ClaimsPrincipal principal)
        {
            switch (grantType)
            {
                case GrantTypes.AuthorizationCode:
                    context.AuthorizationCodePrincipal = principal;
                    break;
                case GrantTypes.DeviceCode:
                    context.DeviceCodePrincipal = principal;
                    break;
                default:
                    context.RefreshTokenPrincipal = principal;
                    break;
            }
        }

        /// <summary>A stored grant's principal holding the given resources.</summary>
        private static ClaimsPrincipal GrantPrincipal(params string[] resources)
        {
            ClaimsIdentity identity = new("test");
            identity.SetClaim(Claims.Subject, "subject");
            identity.SetResources(resources);
            return new ClaimsPrincipal(identity);
        }

        /// <summary>
        ///     A sign-in as OpenIddict hands it over after preparing each principal: the access
        ///     token's audiences and the refresh token's resources are the grant's.
        /// </summary>
        private static ProcessSignInContext SignIn(
            OpenIddictServerEndpointType endpoint, string grantType, params string[] requested)
        {
            string[] grant = [Origin, "https://identity.tellma.com"];
            ProcessSignInContext context = new(Transaction(
                endpoint,
                new OpenIddictRequest { ClientId = ClientId, GrantType = grantType, Resources = AsParameter(requested) }))
            {
                Principal = GrantPrincipal(grant),
                GenerateAccessToken = true,
                GenerateRefreshToken = true,
                RefreshTokenPrincipal = GrantPrincipal(grant),
            };

            ClaimsIdentity access = new("test");
            access.SetAudiences(grant);
            context.AccessTokenPrincipal = new ClaimsPrincipal(access);
            return context;
        }

        /// <summary>Runs the token check in a scope of its own.</summary>
        private async Task HandleAsync(ValidateTokenRequestContext context)
        {
            using IServiceScope scope = _services.CreateScope();
            await new ValidateTokenResourcesHandler(scope.ServiceProvider.GetRequiredService<ResourceGrantValidator>())
                .HandleAsync(context);
        }

        /// <summary>Discards audit entries.</summary>
        private sealed class NullAuditLogger : IAuditLogger
        {
            /// <inheritdoc />
            public Task LogAsync(AuditEventEntry entry, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }
        }
    }
}
