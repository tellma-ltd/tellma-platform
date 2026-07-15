// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using OpenIddict.Validation.AspNetCore;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>Authorization policy names and their scope requirements for the management APIs.</summary>
    public static class ApiPolicies
    {
        /// <summary>The distribution-facing management surface (bulk invite, service accounts).</summary>
        public const string IdentityScope = "TellmaIdentityScope";

        /// <summary>The operator/control-plane surface (user admin, temporary access passes, audit).</summary>
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
                policy.RequireAssertion(context => HasScope(context, TellmaIdentityConstants.IdentityScope));
            });

            options.AddPolicy(ControlPlaneScope, policy =>
            {
                policy.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => HasScope(context, TellmaIdentityConstants.ControlPlaneScope));
            });
        }

        /// <summary>
        ///     Whether the principal holds a scope. OpenIddict emits the granted scopes as a single
        ///     space-delimited <c>scope</c> claim, so membership is checked by splitting it.
        /// </summary>
        private static bool HasScope(AuthorizationHandlerContext context, string scope)
        {
            return context.User.FindAll("scope")
                .SelectMany(static claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Contains(scope, StringComparer.Ordinal);
        }
    }
}
