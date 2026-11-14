// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using OpenIddict.Validation.AspNetCore;
using System.Security.Claims;
using Tellma.Identity.Services.AuthenticationPolicy;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Authorization policy names and their requirements for the management APIs. Each policy
    ///     requires its scope, its own audience and a machine principal. A token is a credential at
    ///     one of the identity server's APIs only when it was minted for that API, so a token minted
    ///     for a distribution — which sits in that distribution's host and logs — is refused here
    ///     even when its scopes would match. And both APIs are machine-to-machine surfaces, so a
    ///     token that speaks for a person is refused whatever it carries.
    /// </summary>
    public static class ApiPolicies
    {
        /// <summary>
        ///     The distribution-facing management surface (bulk invite, service accounts): the
        ///     management scope, the management audience among the token's audiences, and a
        ///     machine principal.
        /// </summary>
        public const string IdentityScope = "TellmaIdentityScope";

        /// <summary>
        ///     The operator/control-plane surface (user admin, temporary access passes): the
        ///     control-plane scope, the control-plane audience among the token's audiences, and a
        ///     machine principal.
        /// </summary>
        public const string ControlPlaneScope = "TellmaControlPlaneScope";

        /// <summary>Registers the management-API authorization policies.</summary>
        /// <param name="options">The authorization options.</param>
        public static void Configure(AuthorizationOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            options.AddPolicy(IdentityScope, policy =>
            {
                policy.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(static context =>
                    HasScope(context.User, TellmaIdentityConstants.IdentityScope)
                    && HasAudience(context.User, TellmaIdentityConstants.IdentityAudience)
                    && IsService(context.User));
            });

            options.AddPolicy(ControlPlaneScope, policy =>
            {
                policy.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(static context => HasControlPlaneAuthority(context.User));
            });
        }

        /// <summary>
        ///     Whether a principal holds a scope. OpenIddict emits the granted scopes as a single
        ///     space-delimited <c>scope</c> claim, so membership is checked by splitting it.
        /// </summary>
        /// <param name="principal">The authenticated principal.</param>
        /// <param name="scope">The scope to look for.</param>
        /// <returns>Whether the scope was granted.</returns>
        public static bool HasScope(ClaimsPrincipal principal, string scope)
        {
            ArgumentNullException.ThrowIfNull(principal);

            return principal.FindAll(Claims.Scope)
                .SelectMany(static claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Contains(scope, StringComparer.Ordinal);
        }

        /// <summary>
        ///     Whether a principal's token was minted for an audience. A JWT's <c>aud</c> holds one
        ///     value or an array; either way the validated principal carries one <c>aud</c> claim per
        ///     audience, compared ordinally and never split or normalized.
        /// </summary>
        /// <param name="principal">The authenticated principal.</param>
        /// <param name="audience">The audience to look for.</param>
        /// <returns>Whether the audience is among the token's audiences.</returns>
        public static bool HasAudience(ClaimsPrincipal principal, string audience)
        {
            ArgumentNullException.ThrowIfNull(principal);

            return !string.IsNullOrEmpty(audience)
                && principal.FindAll(Claims.Audience).Any(claim => string.Equals(claim.Value, audience, StringComparison.Ordinal));
        }

        /// <summary>
        ///     Whether a principal is a machine: its token says so in the kind claim the server
        ///     writes from the grant type at issuance. A token that speaks for a person — one that
        ///     descends from an interactive sign-in — never is, and neither is a token without the
        ///     claim.
        /// </summary>
        /// <param name="principal">The authenticated principal.</param>
        /// <returns>Whether the principal is a machine.</returns>
        public static bool IsService(ClaimsPrincipal principal)
        {
            ArgumentNullException.ThrowIfNull(principal);

            // Exactly one kind claim, holding the machine value: a principal that somehow carried
            // two would be ambiguous, and an ambiguous principal is refused.
            Claim[] kinds = [.. principal.FindAll(TellmaClaims.Kind)];
            return kinds is [{ Value: TellmaKinds.Service }];
        }

        /// <summary>
        ///     Whether a principal speaks for the control plane at the identity server: it holds the
        ///     control-plane scope, its token was minted for the control-plane audience, and it is
        ///     a machine. A control-plane token minted for a distribution carries only that
        ///     distribution's origin, so it never has this authority here.
        /// </summary>
        /// <param name="principal">The authenticated principal.</param>
        /// <returns>Whether the principal has control-plane authority at the identity server.</returns>
        public static bool HasControlPlaneAuthority(ClaimsPrincipal principal)
        {
            ArgumentNullException.ThrowIfNull(principal);

            return HasScope(principal, TellmaIdentityConstants.ControlPlaneScope)
                && HasAudience(principal, TellmaIdentityConstants.ControlPlaneAudience)
                && IsService(principal);
        }
    }
}
