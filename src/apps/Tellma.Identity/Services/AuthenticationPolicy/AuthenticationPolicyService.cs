// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Services.AuthenticationPolicy
{
    /// <summary>
    ///     Pure policy logic: no I/O, no HTTP context — fully unit-testable. Enforcement lives at
    ///     the authority (here), not at resource servers, because RFC 8176 <c>amr</c> values are
    ///     too coarse to carry the allow-list's granularity.
    /// </summary>
    public sealed class AuthenticationPolicyService : IAuthenticationPolicyService
    {
        /// <summary>Tier ranks for comparisons; higher satisfies lower.</summary>
        private static readonly Dictionary<string, int> TierRanks = new(StringComparer.Ordinal)
        {
            [AcrTiers.Aal1] = 1,
            [AcrTiers.Aal2] = 2,
            [AcrTiers.Aal3] = 3,
            // EAP ACR synonyms accepted as inputs.
            [AcrTiers.PhishingResistant] = 2,
            [AcrTiers.PhishingResistantHardware] = 3,
        };

        /// <inheritdoc />
        public bool TryParseAllowedMethods(string? value, out IReadOnlyList<string>? allowedMethods)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                allowedMethods = null;
                return true;
            }

            string[] tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            List<string> parsed = [];
            foreach (string token in tokens)
            {
                if (!AuthenticationMethods.All.Contains(token, StringComparer.Ordinal))
                {
                    allowedMethods = null;
                    return false;
                }

                if (!parsed.Contains(token, StringComparer.Ordinal))
                {
                    parsed.Add(token);
                }
            }

            allowedMethods = parsed;
            return true;
        }

        /// <inheritdoc />
        public AssuranceResult DeriveAssurance(
            IReadOnlyCollection<string> methods, bool passkeyIsDeviceBound, long authTime, long? passkeyAuthTime = null)
        {
            ArgumentNullException.ThrowIfNull(methods);

            List<string> amr = [];
            string acr = AcrTiers.Aal1;

            bool passkey = methods.Contains(AuthenticationMethods.Passkey, StringComparer.Ordinal);
            bool password = methods.Contains(AuthenticationMethods.Password, StringComparer.Ordinal);
            bool otp = methods.Contains(AuthenticationMethods.Totp, StringComparer.Ordinal)
                || methods.Contains(AuthenticationMethods.EmailCode, StringComparer.Ordinal);

            if (passkey)
            {
                // A passkey is a single multi-factor, phishing-resistant authenticator. The
                // device-bound (non-synced) signal raises it to the aal3 tier; a fully
                // substantiated NIST AAL3 additionally needs attestation, which is deferred.
                amr.Add(passkeyIsDeviceBound ? AuthenticationMethodReferences.HardwareKey : AuthenticationMethodReferences.SoftwareKey);
                amr.Add(AuthenticationMethodReferences.UserPresence);
                acr = passkeyIsDeviceBound ? AcrTiers.Aal3 : AcrTiers.Aal2;
            }

            // Non-passkey factors are recorded even when a passkey was also used, so amr stays a
            // complete audit record of every method the session actually exercised.
            if (password)
            {
                amr.Add(AuthenticationMethodReferences.Password);
            }

            if (otp)
            {
                amr.Add(AuthenticationMethodReferences.OneTimePassword);
            }

            if (password && otp)
            {
                // Two distinct factors: knowledge plus possession.
                amr.Add(AuthenticationMethodReferences.MultiFactor);
                if (TierRanks[acr] < TierRanks[AcrTiers.Aal2])
                {
                    acr = AcrTiers.Aal2;
                }
            }

            // Passkey evidence carries its own timestamp: absent an explicit one, the assertion is
            // as fresh as the event being derived; without a passkey there is nothing to date.
            return new AssuranceResult(acr, amr, [.. methods], authTime, passkey ? passkeyAuthTime ?? authTime : 0);
        }

        /// <inheritdoc />
        public AssuranceResult? ReadAssurance(ClaimsPrincipal principal)
        {
            ArgumentNullException.ThrowIfNull(principal);

            string[] methods = [.. principal.FindAll(TellmaClaims.Methods).Select(static claim => claim.Value)];
            if (methods.Length == 0)
            {
                return null;
            }

            bool deviceBound = string.Equals(
                principal.FindFirst(SignInClaims.PasskeyDeviceBound)?.Value, "true", StringComparison.OrdinalIgnoreCase);

            long authTime = ReadUnixSeconds(principal, Claims.AuthenticationTime);

            // A session predating the passkey timestamp has none, which reads as infinitely stale
            // and so fails closed on any max_age asked of a passkey-borne tier: the next passkey
            // assertion stamps it. Falling back to auth_time instead would let the session's
            // weakest later factor answer for the passkey indefinitely.
            return DeriveAssurance(
                methods, deviceBound, authTime, ReadUnixSeconds(principal, SignInClaims.PasskeyAuthTime));
        }

        /// <summary>Reads a unix-seconds claim, or 0 when absent or unparseable.</summary>
        private static long ReadUnixSeconds(ClaimsPrincipal principal, string claimType)
        {
            return long.TryParse(
                principal.FindFirst(claimType)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
                ? parsed
                : 0;
        }

        /// <inheritdoc />
        public long TierAuthTime(AssuranceResult assurance)
        {
            ArgumentNullException.ThrowIfNull(assurance);

            return FreshnessAnchor(assurance, TierRanks[assurance.Acr]);
        }

        /// <inheritdoc />
        public AssuranceResult? FilterAssurance(AssuranceResult assurance, IReadOnlyList<string>? allowedMethods)
        {
            ArgumentNullException.ThrowIfNull(assurance);

            if (allowedMethods is null)
            {
                return assurance;
            }

            string[] survivors =
                [.. assurance.Methods.Where(method => allowedMethods.Contains(method, StringComparer.Ordinal))];
            if (survivors.Length == 0)
            {
                return null;
            }

            // Only a device-bound passkey derives the aal3 tier, so the device-bound signal is
            // recoverable from the tier being filtered.
            return survivors.Length == assurance.Methods.Count
                ? assurance
                : DeriveAssurance(
                    survivors, assurance.Acr == AcrTiers.Aal3, assurance.AuthTime, assurance.PasskeyAuthTime);
        }

        /// <inheritdoc />
        public PolicyEvaluation Evaluate(
            IReadOnlyCollection<string> acrValues,
            TimeSpan? maxAge,
            IReadOnlyList<string>? allowedMethods,
            AssuranceResult? current,
            bool forceInteraction,
            long nowUnixSeconds,
            long? interactionSince = null)
        {
            ArgumentNullException.ThrowIfNull(acrValues);

            // The highest requested tier wins; unknown acr values are treated as unsatisfiable
            // rather than silently ignored.
            int requiredRank = 1;
            foreach (string value in acrValues)
            {
                if (!TierRanks.TryGetValue(value, out int rank))
                {
                    return new PolicyEvaluation(PolicyOutcome.Unsatisfiable);
                }

                requiredRank = Math.Max(requiredRank, rank);
            }

            string requiredTier = requiredRank switch
            {
                3 => AcrTiers.Aal3,
                2 => AcrTiers.Aal2,
                _ => AcrTiers.Aal1,
            };

            // A satisfying session needs no interaction, so it is checked before offerability:
            // a session that already reached the tier must never be refused just because no
            // offerable method could reach it interactively today. A disallowed method stops
            // counting toward this request's assurance (it is filtered, never a taint): step-up
            // sign-ins merge the session's accumulated methods, so a session poisoned by a
            // since-disallowed method could never be cured interactively — the user would loop
            // through the login page forever.
            if (current is null
                || FilterAssurance(current, allowedMethods) is not { } effective
                || TierRanks[effective.Acr] < requiredRank)
            {
                return RequireInteraction(allowedMethods, requiredRank, requiredTier);
            }

            // The evidence carrying the requested tier — the only evidence whose age can answer a
            // question asked about that tier.
            long anchor = FreshnessAnchor(effective, requiredRank);

            // Did this request's own redirect produce that evidence? If so it discharges both the
            // forced-interaction demand and the max_age bound: the user just did what was asked,
            // and re-asking would loop. Measuring the discharge against the tier's anchor rather
            // than the session's last event is what stops a weaker factor from laundering it — an
            // email code cannot answer a demand made of a hardware key.
            bool dischargedByThisRequest = interactionSince is { } since && anchor >= since;

            // Either demand outstanding — a forced event not yet produced, or evidence older than
            // the bound — sends the user to authenticate.
            bool outstanding = !dischargedByThisRequest
                && (forceInteraction
                    || (maxAge is { } bound && (nowUnixSeconds - anchor) > (long)bound.TotalSeconds));

            return outstanding
                ? RequireInteraction(allowedMethods, requiredRank, requiredTier)
                : new PolicyEvaluation(PolicyOutcome.Satisfied);
        }

        /// <summary>
        ///     The timestamp a <c>max_age</c> bound is measured against: the age of the evidence
        ///     that actually carries the requested tier. The base tier is carried by any
        ///     interactive event, so the session-wide <c>auth_time</c> dates it. Every tier above
        ///     it is dated from the evidence that reaches it, and the session times exactly one
        ///     such piece of evidence — the passkey assertion — so an email code cannot renew the
        ///     recency of a hardware-key proof the user never repeated.
        ///     <para>
        ///         The password + one-time-code composition also derives the second tier, and the
        ///         session records no timestamp for it. Crediting <c>auth_time</c> there would
        ///         reintroduce exactly the laundering this exists to stop — a later, weaker factor
        ///         refreshing a proof that was not repeated — so it is left undated and reads as
        ///         infinitely stale, which fails closed. That composition is unreachable while
        ///         password sign-in is deferred; timing it is part of shipping password sign-in,
        ///         alongside restoring it to <see cref="OfferableMethods" />.
        ///     </para>
        /// </summary>
        /// <param name="assurance">The assurance whose evidence is being dated.</param>
        /// <param name="requiredRank">The rank of the tier the question is asked about.</param>
        /// <returns>Unix seconds; 0 when the tier rests on evidence the session never timed.</returns>
        private static long FreshnessAnchor(AssuranceResult assurance, int requiredRank)
        {
            return requiredRank <= 1 ? assurance.AuthTime : assurance.PasskeyAuthTime;
        }

        /// <summary>
        ///     Interaction is needed: offers the methods able to reach the tier through a
        ///     completable composition, or — when none can — reports the request unsatisfiable
        ///     (the protocol answer, never a login redirect that could only loop).
        /// </summary>
        private static PolicyEvaluation RequireInteraction(
            IReadOnlyList<string>? allowedMethods, int requiredRank, string requiredTier)
        {
            List<string> offerable = OfferableMethods(allowedMethods ?? AuthenticationMethods.All, requiredRank);
            return offerable.Count == 0
                ? new PolicyEvaluation(PolicyOutcome.Unsatisfiable, RequiredTier: requiredTier)
                : new PolicyEvaluation(PolicyOutcome.InteractionRequired, RequiredTier: requiredTier, OfferableMethods: offerable);
        }

        /// <summary>
        ///     The methods worth offering for a required tier — mirroring how
        ///     <see cref="DeriveAssurance" /> composes: ranks are not per-method. Any method
        ///     reaches aal1. aal2 and above are offered only the passkey (device-bound for aal3 —
        ///     knowable only once the user presents it): the password + OTP composition also
        ///     derives aal2, but password sign-in is deferred, so offering its halves would send
        ///     the user through sign-ins that succeed yet can never raise the tier — an infinite
        ///     login loop. Restore the composition here when password sign-in ships.
        /// </summary>
        private static List<string> OfferableMethods(IReadOnlyList<string> allowed, int requiredRank)
        {
            return requiredRank <= 1
                ? [.. allowed]
                : allowed.Contains(AuthenticationMethods.Passkey, StringComparer.Ordinal)
                    ? [AuthenticationMethods.Passkey]
                    : [];
        }
    }
}
