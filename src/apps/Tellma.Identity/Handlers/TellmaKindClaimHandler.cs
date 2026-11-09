// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using OpenIddict.Abstractions;
using OpenIddict.Server;
using System.Security.Claims;
using Tellma.Identity.Services.AuthenticationPolicy;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Tellma.Identity.Handlers
{
    /// <summary>
    ///     Writes <see cref="TellmaClaims.Kind" /> into every access token the server signs, from
    ///     the token request's grant type, and removes it from every other token. It runs inside
    ///     the sign-in pipeline after OpenIddict has filtered each token's principal and before
    ///     any token is generated, so no other code path decides the value: a claim of that type
    ///     already on the principal — from the store, a controller or a previous token — never
    ///     reaches a token, and nothing a client sends is read.
    /// </summary>
    public sealed class TellmaKindClaimHandler : IOpenIddictServerHandler<ProcessSignInContext>
    {
        /// <summary>
        ///     The handler registration: after <c>PrepareUserCodePrincipal</c>, the last of the
        ///     handlers that build a token's principal, and before <c>GenerateAccessToken</c>, the
        ///     first that serializes one.
        /// </summary>
        public static OpenIddictServerHandlerDescriptor Descriptor { get; }
            = OpenIddictServerHandlerDescriptor.CreateBuilder<ProcessSignInContext>()
                .UseSingletonHandler<TellmaKindClaimHandler>()
                .SetOrder(OpenIddictServerHandlers.PrepareUserCodePrincipal.Descriptor.Order + 500)
                .Build();

        /// <inheritdoc />
        public ValueTask HandleAsync(ProcessSignInContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // Strip the claim from every principal this sign-in prepared. Codes and refresh
            // tokens keep every claim of the principal they were built from, so without this a
            // value written anywhere upstream would be persisted and could resurface later.
            foreach (ClaimsPrincipal? principal in (ClaimsPrincipal?[])
                [
                    context.AccessTokenPrincipal,
                    context.AuthorizationCodePrincipal,
                    context.DeviceCodePrincipal,
                    context.IdentityTokenPrincipal,
                    context.IssuedTokenPrincipal,
                    context.RefreshTokenPrincipal,
                    context.RequestTokenPrincipal,
                    context.UserCodePrincipal,
                ])
            {
                principal?.RemoveClaims(TellmaClaims.Kind);
            }

            // The access tokens this sign-in will sign: the ordinary one, and the token a token
            // exchange issues, which is an access token whenever that is the requested type.
            bool signsAccessToken = context.GenerateAccessToken && context.AccessTokenPrincipal is not null;
            bool signsIssuedAccessToken = context.GenerateIssuedToken
                && context.IssuedTokenPrincipal is not null
                && string.Equals(context.IssuedTokenType, TokenTypeIdentifiers.AccessToken, StringComparison.Ordinal);
            if (!signsAccessToken && !signsIssuedAccessToken)
            {
                return ValueTask.CompletedTask;
            }

            // Access tokens are issued at the token endpoint alone (the implicit flow is not
            // enabled). Anywhere else, or for a grant type with no known kind, the token is
            // refused rather than signed without the claim every resource server relies on.
            string? kind = context.EndpointType == OpenIddictServerEndpointType.Token
                ? TellmaKinds.ForGrantType(context.Request?.GrantType)
                : null;
            if (kind is null)
            {
                context.Reject(
                    error: Errors.ServerError,
                    description: "The kind of principal the token speaks for cannot be determined.");
                return ValueTask.CompletedTask;
            }

            if (signsAccessToken)
            {
                context.AccessTokenPrincipal!.SetClaim(TellmaClaims.Kind, kind);
            }

            if (signsIssuedAccessToken)
            {
                context.IssuedTokenPrincipal!.SetClaim(TellmaClaims.Kind, kind);
            }

            return ValueTask.CompletedTask;
        }
    }
}
