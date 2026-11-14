// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Tellma.Core.AspNetCore.Routing
{
    /// <summary>
    ///     The authorization an endpoint is under, resolved exactly as the authorization middleware
    ///     resolves it: its authorize data, policies and requirements combined, or the fallback policy.
    /// </summary>
    internal static class EndpointPolicies
    {
        /// <summary>The endpoint's effective policy; null when the endpoint is under none at all.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="provider">The policy provider.</param>
        /// <returns>The combined policy.</returns>
        public static async Task<AuthorizationPolicy?> ResolveAsync(Endpoint endpoint, IAuthorizationPolicyProvider provider)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentNullException.ThrowIfNull(provider);

            IReadOnlyList<IAuthorizeData> data = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            IReadOnlyList<AuthorizationPolicy> policies = endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>();
            AuthorizationPolicy? policy = await AuthorizationPolicy.CombineAsync(provider, data, policies).ConfigureAwait(false);

            // Requirements stamped directly on the endpoint join the policy, as the middleware joins them.
            IReadOnlyList<IAuthorizationRequirementData> requirementData = endpoint.Metadata.GetOrderedMetadata<IAuthorizationRequirementData>();
            if (requirementData.Count == 0)
            {
                return policy;
            }

            AuthorizationPolicyBuilder builder = new();
            if (policy is not null)
            {
                _ = builder.Combine(policy);
            }

            foreach (IAuthorizationRequirementData item in requirementData)
            {
                _ = builder.AddRequirements([.. item.GetRequirements()]);
            }

            return builder.Build();
        }

        /// <summary>Whether a policy authenticates the session cookie.</summary>
        /// <param name="policy">The policy, or null.</param>
        /// <returns>True when the policy names the session scheme.</returns>
        public static bool UsesSession(AuthorizationPolicy? policy)
        {
            return policy is not null && policy.AuthenticationSchemes.Contains(TellmaAuthentication.SessionScheme, StringComparer.Ordinal);
        }
    }
}
