// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using OpenIddict.Abstractions;
using System.Collections.Immutable;

namespace Tellma.Identity.Services
{
    /// <summary>
    ///     The audiences of a machine token — one issued by client credentials or token exchange.
    /// </summary>
    /// <param name="Audiences">The token's audiences, when they could be derived.</param>
    /// <param name="Error">Why they could not, when they could not.</param>
    public sealed record MachineAudienceResult(IReadOnlyList<string> Audiences, string? Error);

    /// <summary>
    ///     Derives a machine token's audiences. An explicit <c>resource</c> is the token's whole
    ///     audience: the requested scopes' platform audiences are not added, so a token minted for
    ///     a distribution is never also a credential at the identity server's own APIs. Without a
    ///     <c>resource</c>, the audiences are the requested scopes' platform audiences. The
    ///     distribution API scope has no audience of its own a machine caller could mean — its
    ///     resources are every distribution's origin — so it always needs an explicit
    ///     <c>resource</c>.
    /// </summary>
    public static class MachineTokenAudiences
    {
        /// <summary>The error description for a distribution API scope requested without a resource.</summary>
        public const string ResourceRequiredDescription =
            "The tellma_api scope requires an explicit 'resource' parameter naming the target API.";

        /// <summary>Derives the audiences of a machine token.</summary>
        /// <param name="scopes">The token's scopes.</param>
        /// <param name="resources">
        ///     The request's <c>resource</c> parameters, already validated against the client's
        ///     grants.
        /// </param>
        /// <param name="scopeManager">Reads the platform scopes' audiences.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The audiences, or the <c>invalid_target</c> description when none can be derived.</returns>
        public static async Task<MachineAudienceResult> ResolveAsync(
            IReadOnlyCollection<string> scopes,
            IReadOnlyCollection<string> resources,
            IOpenIddictScopeManager scopeManager,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(scopes);
            ArgumentNullException.ThrowIfNull(resources);
            ArgumentNullException.ThrowIfNull(scopeManager);

            // An explicit resource is the whole audience.
            if (resources.Count > 0)
            {
                return new MachineAudienceResult([.. resources.Distinct(StringComparer.Ordinal)], null);
            }

            if (scopes.Contains(TellmaIdentityConstants.ApiScope, StringComparer.Ordinal))
            {
                return new MachineAudienceResult([], ResourceRequiredDescription);
            }

            // Without one, the platform audiences of the requested scopes: the identity server's
            // own origin for the management scope, the control-plane audience for the operator
            // scope.
            List<string> audiences = [];
            ImmutableArray<string> names = [.. scopes];
            await foreach (string resource in scopeManager.ListResourcesAsync(names, cancellationToken))
            {
                if (!audiences.Contains(resource, StringComparer.Ordinal))
                {
                    audiences.Add(resource);
                }
            }

            return new MachineAudienceResult(audiences, null);
        }
    }
}
