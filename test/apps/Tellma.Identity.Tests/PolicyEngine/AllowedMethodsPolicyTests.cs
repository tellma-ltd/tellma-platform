// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Identity.Services.AuthenticationPolicy;

namespace Tellma.Identity.Tests.PolicyEngine
{
    /// <summary>Parsing and enforcement of the tenant method allow-list.</summary>
    public sealed class AllowedMethodsPolicyTests
    {
        private readonly AuthenticationPolicyService _policy = new();

        [Fact]
        public void Empty_allow_list_permits_every_method()
        {
            Assert.True(_policy.TryParseAllowedMethods(null, out IReadOnlyList<string>? parsed));
            Assert.Null(parsed);
        }

        [Fact]
        public void Known_methods_parse_in_order_without_duplicates()
        {
            Assert.True(_policy.TryParseAllowedMethods("passkey email_code passkey", out IReadOnlyList<string>? parsed));
            Assert.Equal(["passkey", "email_code"], parsed);
        }

        [Fact]
        public void An_unknown_method_is_a_protocol_error()
        {
            Assert.False(_policy.TryParseAllowedMethods("passkey sms", out IReadOnlyList<string>? parsed));
            Assert.Null(parsed);
        }

        [Fact]
        public void A_disabled_method_used_by_the_session_forces_reauthentication()
        {
            // The session authenticated with a passkey, but the tenant now allows only email code.
            AssuranceResult current = _policy.DeriveAssurance([AuthenticationMethods.Passkey], passkeyIsDeviceBound: false, authTime: 100);

            PolicyEvaluation evaluation = _policy.Evaluate(
                acrValues: [],
                maxAge: null,
                allowedMethods: [AuthenticationMethods.EmailCode],
                current: current,
                forceInteraction: false,
                nowUnixSeconds: 200);

            Assert.Equal(PolicyOutcome.InteractionRequired, evaluation.Outcome);
            Assert.Equal([AuthenticationMethods.EmailCode], evaluation.OfferableMethods);
        }

        [Fact]
        public void A_disallowed_method_stops_counting_but_an_allowed_factor_keeps_the_session()
        {
            // The session signed in with an email code and later stepped up with a device-bound
            // passkey (step-up merges methods). The tenant allows only the passkey: the email
            // code's contribution is filtered out — not treated as a session taint, which could
            // never be cured interactively — and the surviving passkey still satisfies aal3.
            AssuranceResult current = _policy.DeriveAssurance(
                [AuthenticationMethods.EmailCode, AuthenticationMethods.Passkey], passkeyIsDeviceBound: true, authTime: 100);

            PolicyEvaluation evaluation = _policy.Evaluate(
                acrValues: [AcrTiers.Aal3],
                maxAge: null,
                allowedMethods: [AuthenticationMethods.Passkey],
                current: current,
                forceInteraction: false,
                nowUnixSeconds: 200);

            Assert.Equal(PolicyOutcome.Satisfied, evaluation.Outcome);

            // And the assurance the grant will carry is the filtered one — the same filter the
            // evaluation reached its decision through, applied where the claims are assembled.
            AssuranceResult effective = _policy.FilterAssurance(current, [AuthenticationMethods.Passkey])!;
            Assert.Equal([AuthenticationMethods.Passkey], effective.Methods);
            Assert.Equal(AcrTiers.Aal3, effective.Acr);
        }

        [Fact]
        public void Filtering_a_disallowed_passkey_drops_its_tier_contribution()
        {
            // The device-bound passkey is what carried aal3; with the passkey disallowed, the
            // surviving email code re-derives to aal1 — filtering never inflates assurance.
            AssuranceResult current = _policy.DeriveAssurance(
                [AuthenticationMethods.Passkey, AuthenticationMethods.EmailCode], passkeyIsDeviceBound: true, authTime: 100);

            AssuranceResult? filtered = _policy.FilterAssurance(current, [AuthenticationMethods.EmailCode]);

            Assert.NotNull(filtered);
            Assert.Equal([AuthenticationMethods.EmailCode], filtered.Methods);
            Assert.Equal(AcrTiers.Aal1, filtered.Acr);
        }

        [Fact]
        public void Filtering_is_the_identity_when_the_list_permits_everything()
        {
            AssuranceResult current = _policy.DeriveAssurance(
                [AuthenticationMethods.Passkey], passkeyIsDeviceBound: true, authTime: 100);

            Assert.Same(current, _policy.FilterAssurance(current, null));
            Assert.Same(current, _policy.FilterAssurance(current, [AuthenticationMethods.Passkey]));
        }

        [Fact]
        public void Filtering_away_every_used_method_yields_null()
        {
            AssuranceResult current = _policy.DeriveAssurance(
                [AuthenticationMethods.EmailCode], passkeyIsDeviceBound: false, authTime: 100);

            Assert.Null(_policy.FilterAssurance(current, [AuthenticationMethods.Passkey]));
        }

        [Fact]
        public void A_tier_no_allowed_method_can_reach_is_unsatisfiable()
        {
            // aal3 requested but only email code allowed (aal1 on its own).
            PolicyEvaluation evaluation = _policy.Evaluate(
                acrValues: [AcrTiers.Aal3],
                maxAge: null,
                allowedMethods: [AuthenticationMethods.EmailCode],
                current: null,
                forceInteraction: false,
                nowUnixSeconds: 200);

            Assert.Equal(PolicyOutcome.Unsatisfiable, evaluation.Outcome);
        }

        [Theory]
        [InlineData(AuthenticationMethods.EmailCode)]
        [InlineData(AuthenticationMethods.Password)]
        [InlineData(AuthenticationMethods.Totp)]
        public void A_lone_factor_that_only_composes_to_aal1_cannot_satisfy_aal2(string method)
        {
            // Ranks compose: a lone password, TOTP, or email code derives aal1, so offering it
            // for an aal2 request would loop the user forever instead of failing cleanly.
            PolicyEvaluation evaluation = _policy.Evaluate(
                acrValues: [AcrTiers.Aal2],
                maxAge: null,
                allowedMethods: [method],
                current: null,
                forceInteraction: false,
                nowUnixSeconds: 200);

            Assert.Equal(PolicyOutcome.Unsatisfiable, evaluation.Outcome);
        }

        [Fact]
        public void Otp_factors_without_a_password_cannot_satisfy_aal2()
        {
            PolicyEvaluation evaluation = _policy.Evaluate(
                acrValues: [AcrTiers.Aal2],
                maxAge: null,
                allowedMethods: [AuthenticationMethods.Totp, AuthenticationMethods.EmailCode],
                current: null,
                forceInteraction: false,
                nowUnixSeconds: 200);

            Assert.Equal(PolicyOutcome.Unsatisfiable, evaluation.Outcome);
        }

        [Fact]
        public void Password_compositions_are_not_offered_while_password_sign_in_is_deferred()
        {
            // Password + TOTP derives aal2, but password sign-in is deferred: offering the
            // composition's halves would send the user through sign-ins that succeed yet can
            // never raise the tier — an infinite login loop. Without a passkey in the allow-list
            // the request is unsatisfiable up front.
            PolicyEvaluation evaluation = _policy.Evaluate(
                acrValues: [AcrTiers.Aal2],
                maxAge: null,
                allowedMethods: [AuthenticationMethods.Password, AuthenticationMethods.Totp],
                current: null,
                forceInteraction: false,
                nowUnixSeconds: 200);

            Assert.Equal(PolicyOutcome.Unsatisfiable, evaluation.Outcome);
        }

        [Fact]
        public void A_session_already_holding_password_and_otp_evidence_still_satisfies_aal2()
        {
            // Satisfaction is checked before offerability: a session that already reached the
            // tier is honored even when no offerable method could reach it interactively today.
            AssuranceResult current = _policy.DeriveAssurance(
                [AuthenticationMethods.Password, AuthenticationMethods.Totp], passkeyIsDeviceBound: false, authTime: 100);

            PolicyEvaluation satisfied = _policy.Evaluate(
                acrValues: [AcrTiers.Aal2],
                maxAge: null,
                allowedMethods: [AuthenticationMethods.Password, AuthenticationMethods.Totp],
                current: current,
                forceInteraction: false,
                nowUnixSeconds: 200);

            Assert.Equal(PolicyOutcome.Satisfied, satisfied.Outcome);
        }

        [Fact]
        public void Aal3_offers_only_the_passkey_method()
        {
            PolicyEvaluation evaluation = _policy.Evaluate(
                acrValues: [AcrTiers.Aal3],
                maxAge: null,
                allowedMethods: null,
                current: null,
                forceInteraction: false,
                nowUnixSeconds: 200);

            Assert.Equal(PolicyOutcome.InteractionRequired, evaluation.Outcome);
            Assert.Equal([AuthenticationMethods.Passkey], evaluation.OfferableMethods);
        }

        [Fact]
        public void An_unknown_acr_value_is_unsatisfiable()
        {
            PolicyEvaluation evaluation = _policy.Evaluate(
                acrValues: ["urn:unknown:tier"],
                maxAge: null,
                allowedMethods: null,
                current: null,
                forceInteraction: false,
                nowUnixSeconds: 200);

            Assert.Equal(PolicyOutcome.Unsatisfiable, evaluation.Outcome);
        }
    }
}
