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
        ///     self-asserted backup-eligibility flag.
        /// </param>
        /// <param name="authTime">When the most recent interactive event happened (unix seconds).</param>
        /// <param name="passkeyAuthTime">
        ///     When the most recent passkey assertion happened (unix seconds); defaults to
        ///     <paramref name="authTime" />, i.e. the passkey evidence is the event being derived.
        /// </param>
        /// <returns>The derived assurance.</returns>
        AssuranceResult DeriveAssurance(
            IReadOnlyCollection<string> methods, bool passkeyIsDeviceBound, long authTime, long? passkeyAuthTime = null);

        /// <summary>Reads the assurance recorded in an authenticated SSO-cookie principal.</summary>
        /// <param name="principal">The cookie principal.</param>
        /// <returns>The assurance, or null when the principal carries no method evidence.</returns>
        AssuranceResult? ReadAssurance(ClaimsPrincipal principal);

        /// <summary>
        ///     When the tier an assurance asserts was established — the value belonging in the
        ///     <c>auth_time</c> claim beside its <c>acr</c>. A session accumulates events, but a
        ///     token claiming <c>aal3</c> is claiming a device-bound passkey was presented, and
        ///     the time that matters is when <em>that</em> happened: relying parties pair
        ///     <c>acr</c> with <c>auth_time</c> to decide whether a sensitive operation needs a
        ///     fresh step-up, so dating the tier from a later, weaker factor would let an email
        ///     code renew a hardware key's recency at the resource server even though the
        ///     authority itself refuses to.
        /// </summary>
        /// <param name="assurance">The assurance being emitted.</param>
        /// <returns>Unix seconds; 0 when the tier rests on evidence the session never recorded.</returns>
        long TierAuthTime(AssuranceResult assurance);

        /// <summary>
        ///     Filters an assurance down to the methods an allow-list permits, re-deriving the
        ///     tier from the surviving factors. A disallowed method stops counting toward this
        ///     request's assurance without poisoning the shared session, which other distributions
        ///     with different allow-lists may still be using — and which step-up sign-ins merge
        ///     into, so a poisoned session could never be cured interactively.
        /// </summary>
        /// <param name="assurance">The session's full assurance.</param>
        /// <param name="allowedMethods">The parsed allow-list; null when everything is allowed.</param>
        /// <returns>
        ///     The assurance the allow-list supports (the same instance when nothing is filtered),
        ///     or null when no used method remains allowed.
        /// </returns>
        AssuranceResult? FilterAssurance(AssuranceResult assurance, IReadOnlyList<string>? allowedMethods);

        /// <summary>Evaluates a request's authentication requirements against the current session.</summary>
        /// <param name="acrValues">The requested <c>acr_values</c> (tiers or EAP synonyms).</param>
        /// <param name="maxAge">The requested freshness bound, when any.</param>
        /// <param name="allowedMethods">The parsed allow-list; null when everything is allowed.</param>
        /// <param name="current">The current session's assurance; null when no session exists.</param>
        /// <param name="forceInteraction">An explicit <c>prompt=login</c> style demand.</param>
        /// <param name="nowUnixSeconds">The current time, for <c>auth_time</c> freshness.</param>
        /// <param name="interactionSince">
        ///     When this request already sent the user to authenticate, if it has. Evidence dated
        ///     at or after that instant is the interaction this request asked for, and discharges
        ///     both <paramref name="forceInteraction" /> and <paramref name="maxAge" /> — otherwise
        ///     a bound shorter than the redirect round trip could never be met and the browser
        ///     would bounce forever. The evidence that counts is the evidence carrying the
        ///     requested tier, so re-authenticating with a weaker factor does not discharge a
        ///     demand made of a stronger one.
        /// </param>
        /// <returns>The evaluation.</returns>
        PolicyEvaluation Evaluate(
            IReadOnlyCollection<string> acrValues,
            TimeSpan? maxAge,
            IReadOnlyList<string>? allowedMethods,
            AssuranceResult? current,
            bool forceInteraction,
            long nowUnixSeconds,
            long? interactionSince = null);
    }
}
