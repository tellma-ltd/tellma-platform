// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Services.AuthenticationPolicy
{
    /// <summary>The assurance a completed authentication event carries.</summary>
    /// <param name="Acr">The tier reached (<see cref="AcrTiers" />).</param>
    /// <param name="Amr">The RFC 8176 method references describing the event.</param>
    /// <param name="Methods">The concrete methods used, in the allow-list vocabulary.</param>
    /// <param name="AuthTime">When the interactive event happened (unix seconds).</param>
    public sealed record AssuranceResult(string Acr, IReadOnlyList<string> Amr, IReadOnlyList<string> Methods, long AuthTime);

    /// <summary>How an authorization request's authentication requirements were evaluated.</summary>
    public enum PolicyOutcome
    {
        /// <summary>The current session satisfies the requested assurance and freshness.</summary>
        Satisfied = 0,

        /// <summary>
        ///     Interactive (re-)authentication is required: no session, insufficient assurance,
        ///     stale <c>auth_time</c>, an explicit <c>prompt=login</c>, or methods outside the
        ///     allow-list.
        /// </summary>
        InteractionRequired = 1,

        /// <summary>
        ///     No allowed method can reach the requested tier; the protocol answer is
        ///     <c>unmet_authentication_requirements</c>.
        /// </summary>
        Unsatisfiable = 2,
    }

    /// <summary>The result of evaluating a request's authentication requirements.</summary>
    /// <param name="Outcome">The evaluation outcome.</param>
    /// <param name="Assurance">The current session's assurance, when one exists and satisfies the request.</param>
    /// <param name="RequiredTier">The tier interaction must reach, when interaction is required.</param>
    /// <param name="OfferableMethods">
    ///     The methods the login UI may offer: the allow-list filtered down to methods able to
    ///     reach <paramref name="RequiredTier" />.
    /// </param>
    public sealed record PolicyEvaluation(
        PolicyOutcome Outcome,
        AssuranceResult? Assurance = null,
        string? RequiredTier = null,
        IReadOnlyList<string>? OfferableMethods = null);
}
