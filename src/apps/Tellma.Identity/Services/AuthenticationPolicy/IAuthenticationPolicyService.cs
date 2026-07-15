// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Security.Claims;

namespace Tellma.Identity.Services.AuthenticationPolicy
{
    /// <summary>
    ///     The authentication-policy engine: parses and enforces the tenant's method allow-list,
    ///     derives <c>acr</c>/<c>amr</c> from what a session actually used, and decides whether a
    ///     request's assurance and freshness requirements are met.
    /// </summary>
    public interface IAuthenticationPolicyService
    {
        /// <summary>
        ///     Parses the space-delimited <c>tellma_allowed_methods</c> value against the fixed
        ///     vocabulary.
        /// </summary>
        /// <param name="value">The raw parameter value; null or empty means every method is allowed.</param>
        /// <param name="allowedMethods">The parsed allow-list, or null when everything is allowed.</param>
        /// <returns>False when the value contains a token outside the vocabulary.</returns>
        bool TryParseAllowedMethods(string? value, out IReadOnlyList<string>? allowedMethods);

        /// <summary>Derives the assurance a session's recorded evidence supports.</summary>
        /// <param name="methods">The concrete methods used (allow-list vocabulary).</param>
        /// <param name="passkeyIsDeviceBound">
        ///     Whether the passkey used is device-bound (non-synced), per the authenticator's
        ///     self-asserted backup-state flags.
        /// </param>
        /// <param name="authTime">When the interactive event happened (unix seconds).</param>
        /// <returns>The derived assurance.</returns>
        AssuranceResult DeriveAssurance(IReadOnlyCollection<string> methods, bool passkeyIsDeviceBound, long authTime);

        /// <summary>Reads the assurance recorded in an authenticated SSO-cookie principal.</summary>
        /// <param name="principal">The cookie principal.</param>
        /// <returns>The assurance, or null when the principal carries no method evidence.</returns>
        AssuranceResult? ReadAssurance(ClaimsPrincipal principal);

        /// <summary>Evaluates a request's authentication requirements against the current session.</summary>
        /// <param name="acrValues">The requested <c>acr_values</c> (tiers or EAP synonyms).</param>
        /// <param name="maxAge">The requested freshness bound, when any.</param>
        /// <param name="allowedMethods">The parsed allow-list; null when everything is allowed.</param>
        /// <param name="current">The current session's assurance; null when no session exists.</param>
        /// <param name="forceInteraction">An explicit <c>prompt=login</c> style demand.</param>
        /// <param name="nowUnixSeconds">The current time, for <c>auth_time</c> freshness.</param>
        /// <returns>The evaluation.</returns>
        PolicyEvaluation Evaluate(
            IReadOnlyCollection<string> acrValues,
            TimeSpan? maxAge,
            IReadOnlyList<string>? allowedMethods,
            AssuranceResult? current,
            bool forceInteraction,
            long nowUnixSeconds);
    }
}
