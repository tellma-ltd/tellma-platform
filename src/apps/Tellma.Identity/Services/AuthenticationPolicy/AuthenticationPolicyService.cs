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
        public AssuranceResult DeriveAssurance(IReadOnlyCollection<string> methods, bool passkeyIsDeviceBound, long authTime)
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

            return new AssuranceResult(acr, amr, [.. methods], authTime);
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

            long authTime = long.TryParse(
                principal.FindFirst(Claims.AuthenticationTime)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
                ? parsed
                : 0;

            return DeriveAssurance(methods, deviceBound, authTime);
        }

        /// <inheritdoc />
        public PolicyEvaluation Evaluate(
            IReadOnlyCollection<string> acrValues,
            TimeSpan? maxAge,
            IReadOnlyList<string>? allowedMethods,
            AssuranceResult? current,
            bool forceInteraction,
            long nowUnixSeconds)
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

            // The methods the login UI may offer: those able to participate in a composition of
            // allowed methods that reaches the tier. An empty set means no composition can.
            List<string> offerable = OfferableMethods(allowedMethods ?? AuthenticationMethods.All, requiredRank);
            if (offerable.Count == 0)
            {
                return new PolicyEvaluation(PolicyOutcome.Unsatisfiable, RequiredTier: requiredTier);
            }

            if (current is null || forceInteraction)
            {
                return new PolicyEvaluation(PolicyOutcome.InteractionRequired, RequiredTier: requiredTier, OfferableMethods: offerable);
            }

            // The methods actually used must all be inside the allow-list; a method a tenant
            // disabled stops working at the next authorization or refresh.
            if (allowedMethods is not null
                && current.Methods.Any(method => !allowedMethods.Contains(method, StringComparer.Ordinal)))
            {
                return new PolicyEvaluation(PolicyOutcome.InteractionRequired, RequiredTier: requiredTier, OfferableMethods: offerable);
            }

            // Assurance tier reached must satisfy the requested tier.
            if (TierRanks[current.Acr] < requiredRank)
            {
                return new PolicyEvaluation(PolicyOutcome.InteractionRequired, RequiredTier: requiredTier, OfferableMethods: offerable);
            }

            // Freshness: max_age bounds the age of the last interactive event.
            return maxAge is { } bound && (nowUnixSeconds - current.AuthTime) > (long)bound.TotalSeconds
                ? new PolicyEvaluation(PolicyOutcome.InteractionRequired, RequiredTier: requiredTier, OfferableMethods: offerable)
                : new PolicyEvaluation(PolicyOutcome.Satisfied, Assurance: current);
        }

        /// <summary>
        ///     The methods worth offering for a required tier — mirroring how
        ///     <see cref="DeriveAssurance" /> composes: ranks are not per-method. Any method
        ///     reaches aal1; aal2 needs a passkey OR password plus an OTP factor (a lone password,
        ///     TOTP, or email code derives only aal1); aal3 needs a device-bound passkey, so only
        ///     the passkey method is offered — whether the presented authenticator is device-bound
        ///     is knowable only once the user presents it.
        /// </summary>
        private static List<string> OfferableMethods(IReadOnlyList<string> allowed, int requiredRank)
        {
            if (requiredRank <= 1)
            {
                return [.. allowed];
            }

            bool passkey = allowed.Contains(AuthenticationMethods.Passkey, StringComparer.Ordinal);
            if (requiredRank >= 3)
            {
                return passkey ? [AuthenticationMethods.Passkey] : [];
            }

            List<string> offerable = [];
            if (passkey)
            {
                offerable.Add(AuthenticationMethods.Passkey);
            }

            List<string> otpFactors =
                [.. allowed.Where(static m => m is AuthenticationMethods.Totp or AuthenticationMethods.EmailCode)];
            if (allowed.Contains(AuthenticationMethods.Password, StringComparer.Ordinal) && otpFactors.Count > 0)
            {
                offerable.Add(AuthenticationMethods.Password);
                offerable.AddRange(otpFactors);
            }

            return offerable;
        }
    }
}
