// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Tellma.Identity.Handlers
{
    /// <summary>
    ///     Narrows the access token of a code exchange, device-code redemption or refresh to the
    ///     resources the token request names (RFC 8707). The token request's resources were
    ///     validated against the presented grant's own resources by
    ///     <see cref="ValidateTokenResourcesHandler" />, so the access token can only ever narrow.
    ///     The refresh token keeps the grant's full resources, so a later refresh is evaluated
    ///     against the original grant, never against an earlier narrowing.
    /// </summary>
    public sealed class NarrowAccessTokenAudienceHandler : IOpenIddictServerHandler<ProcessSignInContext>
    {
        /// <summary>
        ///     The handler registration: after OpenIddict has built every token's principal — the
        ///     access token's audiences are then the grant's resources — and before any token is
        ///     serialized.
        /// </summary>
        public static OpenIddictServerHandlerDescriptor Descriptor { get; }
            = OpenIddictServerHandlerDescriptor.CreateBuilder<ProcessSignInContext>()
                .UseSingletonHandler<NarrowAccessTokenAudienceHandler>()
                .SetOrder(OpenIddictServerHandlers.PrepareUserCodePrincipal.Descriptor.Order + 250)
                .Build();

        /// <inheritdoc />
        public ValueTask HandleAsync(ProcessSignInContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // Only the grants that redeem a stored grant narrow here. Client credentials and token
            // exchange take their audiences from the request directly, in the token controller.
            OpenIddictRequest? request = context.Request;
            if (context.EndpointType != OpenIddictServerEndpointType.Token
                || context.AccessTokenPrincipal is null
                || request is null
                || !(request.IsAuthorizationCodeGrantType() || request.IsDeviceCodeGrantType() || request.IsRefreshTokenGrantType()))
            {
                return ValueTask.CompletedTask;
            }

            // A request naming no resource keeps the grant's audiences, which OpenIddict has
            // already copied into the access token.
            string[] requested = [.. request.GetResources()];
            if (requested.Length == 0)
            {
                return ValueTask.CompletedTask;
            }

            context.AccessTokenPrincipal.SetAudiences(requested);
            return ValueTask.CompletedTask;
        }
    }
}
