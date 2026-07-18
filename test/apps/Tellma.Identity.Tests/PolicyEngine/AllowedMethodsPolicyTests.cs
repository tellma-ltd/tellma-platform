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
        public void Password_with_an_otp_factor_composes_to_aal2()
        {
            PolicyEvaluation required = _policy.Evaluate(
                acrValues: [AcrTiers.Aal2],
                maxAge: null,
                allowedMethods: [AuthenticationMethods.Password, AuthenticationMethods.Totp],
                current: null,
                forceInteraction: false,
                nowUnixSeconds: 200);

            Assert.Equal(PolicyOutcome.InteractionRequired, required.Outcome);
            Assert.Equal([AuthenticationMethods.Password, AuthenticationMethods.Totp], required.OfferableMethods);

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
