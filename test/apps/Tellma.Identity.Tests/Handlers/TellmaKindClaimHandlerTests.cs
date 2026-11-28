// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging.Abstractions;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using System.Security.Claims;
using Tellma.Identity.Handlers;
using Tellma.Identity.Services.AuthenticationPolicy;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Tellma.Identity.Tests.Handlers
{
    /// <summary>
    ///     The <c>tellma_kind</c> claim: set in every access token from the grant type at issuance,
    ///     never carried over from the principal, and never in any other token.
    /// </summary>
    public sealed class TellmaKindClaimHandlerTests
    {
        /// <summary>The grant types the token endpoint accepts, with the kind each issues.</summary>
        public static TheoryData<string, string> GrantKinds { get; } = new()
        {
            { GrantTypes.AuthorizationCode, TellmaKinds.Individual },
            { GrantTypes.DeviceCode, TellmaKinds.Individual },
            { GrantTypes.RefreshToken, TellmaKinds.Individual },
            { GrantTypes.ClientCredentials, TellmaKinds.Service },
            { GrantTypes.TokenExchange, TellmaKinds.Service },
        };

        [Theory]
        [MemberData(nameof(GrantKinds))]
        public void Each_grant_type_maps_to_its_kind(string grantType, string kind)
        {
            Assert.Equal(kind, TellmaKinds.ForGrantType(grantType));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(GrantTypes.Password)]
        [InlineData(GrantTypes.Implicit)]
        [InlineData("urn:example:custom")]
        [InlineData("AUTHORIZATION_CODE")]
        public void A_grant_type_with_no_known_kind_maps_to_none(string? grantType)
        {
            Assert.Null(TellmaKinds.ForGrantType(grantType));
        }

        [Theory]
        [MemberData(nameof(GrantKinds))]
        public async Task The_access_token_carries_the_kind_of_its_grant(string grantType, string kind)
        {
            ProcessSignInContext context = TokenSignIn(grantType);

            await new TellmaKindClaimHandler().HandleAsync(context);

            Assert.False(context.IsRejected);
            ClaimsPrincipal principal = grantType == GrantTypes.TokenExchange
                ? context.IssuedTokenPrincipal!
                : context.AccessTokenPrincipal!;
            Assert.Equal(kind, Assert.Single(principal.GetClaims(TellmaClaims.Kind)));
        }

        [Fact]
        public async Task A_kind_already_on_the_principal_is_replaced_never_trusted()
        {
            // A user grant whose principal somehow arrived claiming to be a service: the value the
            // pipeline signs is the grant type's, and it appears exactly once.
            ProcessSignInContext context = TokenSignIn(GrantTypes.AuthorizationCode, spoofedKind: TellmaKinds.Service);

            await new TellmaKindClaimHandler().HandleAsync(context);

            Assert.Equal(TellmaKinds.Individual, Assert.Single(context.AccessTokenPrincipal!.GetClaims(TellmaClaims.Kind)));
        }

        [Fact]
        public async Task No_token_but_the_access_token_carries_the_kind()
        {
            // Every other principal starts out carrying a spoofed value, so this also shows the
            // strip reaches them: the id token, the refresh token and the codes stay clean.
            ProcessSignInContext context = TokenSignIn(GrantTypes.AuthorizationCode, spoofedKind: TellmaKinds.Service);
            context.GenerateIdentityToken = true;
            context.GenerateRefreshToken = true;

            await new TellmaKindClaimHandler().HandleAsync(context);

            foreach (ClaimsPrincipal principal in (ClaimsPrincipal[])
                [
                    context.IdentityTokenPrincipal!,
                    context.RefreshTokenPrincipal!,
                    context.AuthorizationCodePrincipal!,
                    context.DeviceCodePrincipal!,
                    context.RequestTokenPrincipal!,
                    context.UserCodePrincipal!,
                ])
            {
                Assert.Empty(principal.GetClaims(TellmaClaims.Kind));
            }
        }

        [Fact]
        public async Task A_sign_in_without_an_access_token_strips_the_claim_and_adds_none()
        {
            // The authorization endpoint mints a code, not an access token: the code's principal
            // keeps every claim it is built from, so a spoofed value must not ride into it.
            ProcessSignInContext context = Context(OpenIddictServerEndpointType.Authorization, grantType: null);
            context.AuthorizationCodePrincipal = Principal(spoofedKind: TellmaKinds.Service);

            await new TellmaKindClaimHandler().HandleAsync(context);

            Assert.False(context.IsRejected);
            Assert.Empty(context.AuthorizationCodePrincipal.GetClaims(TellmaClaims.Kind));
        }

        [Fact]
        public async Task An_access_token_outside_the_token_endpoint_is_refused()
        {
            // The implicit flow is not enabled; were an access token ever produced at the
            // authorization endpoint, it is refused rather than signed without a kind.
            ProcessSignInContext context = Context(OpenIddictServerEndpointType.Authorization, grantType: null);
            context.GenerateAccessToken = true;
            context.AccessTokenPrincipal = Principal();

            await new TellmaKindClaimHandler().HandleAsync(context);

            Assert.True(context.IsRejected);
            Assert.Equal(Errors.ServerError, context.Error);
            Assert.Empty(context.AccessTokenPrincipal.GetClaims(TellmaClaims.Kind));
        }

        [Fact]
        public async Task An_access_token_for_an_unknown_grant_type_is_refused()
        {
            ProcessSignInContext context = TokenSignIn("urn:example:custom");

            await new TellmaKindClaimHandler().HandleAsync(context);

            Assert.True(context.IsRejected);
            Assert.Equal(Errors.ServerError, context.Error);
        }

        [Fact]
        public async Task An_exchanged_token_of_another_type_carries_no_kind()
        {
            // Only access tokens carry the kind; were another token type ever issued by an
            // exchange, it would not.
            ProcessSignInContext context = TokenSignIn(GrantTypes.TokenExchange);
            context.IssuedTokenType = TokenTypeIdentifiers.IdentityToken;
            context.IssuedTokenPrincipal = Principal(spoofedKind: TellmaKinds.Individual);

            await new TellmaKindClaimHandler().HandleAsync(context);

            Assert.False(context.IsRejected);
            Assert.Empty(context.IssuedTokenPrincipal.GetClaims(TellmaClaims.Kind));
        }

        [Fact]
        public void The_handler_runs_after_every_principal_is_prepared_and_before_any_token_is_generated()
        {
            int order = TellmaKindClaimHandler.Descriptor.Order;

            foreach (OpenIddictServerHandlerDescriptor prepare in (OpenIddictServerHandlerDescriptor[])
                [
                    OpenIddictServerHandlers.PrepareAccessTokenPrincipal.Descriptor,
                    OpenIddictServerHandlers.PrepareAuthorizationCodePrincipal.Descriptor,
                    OpenIddictServerHandlers.PrepareDeviceCodePrincipal.Descriptor,
                    OpenIddictServerHandlers.PrepareIssuedTokenPrincipal.Descriptor,
                    OpenIddictServerHandlers.PrepareRequestTokenPrincipal.Descriptor,
                    OpenIddictServerHandlers.PrepareRefreshTokenPrincipal.Descriptor,
                    OpenIddictServerHandlers.PrepareIdentityTokenPrincipal.Descriptor,
                    OpenIddictServerHandlers.PrepareUserCodePrincipal.Descriptor,
                ])
            {
                Assert.True(order > prepare.Order, $"The handler runs before {prepare.ServiceDescriptor.ServiceType.Name}.");
            }

            foreach (OpenIddictServerHandlerDescriptor generate in (OpenIddictServerHandlerDescriptor[])
                [
                    OpenIddictServerHandlers.GenerateAccessToken.Descriptor,
                    OpenIddictServerHandlers.GenerateAuthorizationCode.Descriptor,
                    OpenIddictServerHandlers.GenerateDeviceCode.Descriptor,
                    OpenIddictServerHandlers.GenerateIssuedToken.Descriptor,
                    OpenIddictServerHandlers.GenerateRequestToken.Descriptor,
                    OpenIddictServerHandlers.GenerateRefreshToken.Descriptor,
                    OpenIddictServerHandlers.GenerateUserCode.Descriptor,
                    OpenIddictServerHandlers.GenerateIdentityToken.Descriptor,
                ])
            {
                Assert.True(order < generate.Order, $"The handler runs after {generate.ServiceDescriptor.ServiceType.Name}.");
            }

            Assert.Equal(typeof(ProcessSignInContext), TellmaKindClaimHandler.Descriptor.ContextType);
        }

        /// <summary>
        ///     A token-endpoint sign-in as OpenIddict hands it over after preparing each principal:
        ///     an access token (or, for token exchange, an issued access token) and the other
        ///     principals, each optionally carrying a spoofed kind.
        /// </summary>
        private static ProcessSignInContext TokenSignIn(string grantType, string? spoofedKind = null)
        {
            ProcessSignInContext context = Context(OpenIddictServerEndpointType.Token, grantType);

            if (grantType == GrantTypes.TokenExchange)
            {
                context.GenerateIssuedToken = true;
                context.IssuedTokenType = TokenTypeIdentifiers.AccessToken;
                context.IssuedTokenPrincipal = Principal(spoofedKind);
            }
            else
            {
                context.GenerateAccessToken = true;
                context.AccessTokenPrincipal = Principal(spoofedKind);
            }

            context.AuthorizationCodePrincipal = Principal(spoofedKind);
            context.DeviceCodePrincipal = Principal(spoofedKind);
            context.IdentityTokenPrincipal = Principal(spoofedKind);
            context.RefreshTokenPrincipal = Principal(spoofedKind);
            context.RequestTokenPrincipal = Principal(spoofedKind);
            context.UserCodePrincipal = Principal(spoofedKind);
            return context;
        }

        /// <summary>A sign-in context at an endpoint, for a grant type.</summary>
        private static ProcessSignInContext Context(OpenIddictServerEndpointType endpoint, string? grantType)
        {
            OpenIddictServerTransaction transaction = new()
            {
                EndpointType = endpoint,
                Logger = NullLogger.Instance,
                Options = new OpenIddictServerOptions(),
                Request = new OpenIddictRequest { GrantType = grantType },
            };

            return new ProcessSignInContext(transaction) { Principal = Principal() };
        }

        /// <summary>A principal with a subject and, optionally, a spoofed kind.</summary>
        private static ClaimsPrincipal Principal(string? spoofedKind = null)
        {
            ClaimsIdentity identity = new("test");
            identity.SetClaim(Claims.Subject, "subject");
            if (spoofedKind is not null)
            {
                identity.SetClaim(TellmaClaims.Kind, spoofedKind);
            }

            return new ClaimsPrincipal(identity);
        }
    }
}
