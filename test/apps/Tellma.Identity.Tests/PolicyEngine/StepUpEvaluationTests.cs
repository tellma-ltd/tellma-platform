// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Identity.Services.AuthenticationPolicy;

namespace Tellma.Identity.Tests.PolicyEngine
{
    /// <summary>Requested-assurance and freshness evaluation (step-up decisions).</summary>
    public sealed class StepUpEvaluationTests
    {
        private readonly AuthenticationPolicyService _policy = new();

        [Fact]
        public void No_session_requires_interaction()
        {
            PolicyEvaluation evaluation = _policy.Evaluate([], null, null, current: null, forceInteraction: false, nowUnixSeconds: 100);
            Assert.Equal(PolicyOutcome.InteractionRequired, evaluation.Outcome);
        }

        [Fact]
        public void A_satisfying_session_is_accepted()
        {
            AssuranceResult current = _policy.DeriveAssurance([AuthenticationMethods.Passkey], passkeyIsDeviceBound: false, authTime: 100);

            PolicyEvaluation evaluation = _policy.Evaluate(
                [AcrTiers.Aal2], null, null, current, forceInteraction: false, nowUnixSeconds: 150);

            Assert.Equal(PolicyOutcome.Satisfied, evaluation.Outcome);
        }

        [Fact]
        public void A_higher_tier_than_the_session_reached_requires_step_up()
        {
            AssuranceResult current = _policy.DeriveAssurance([AuthenticationMethods.EmailCode], passkeyIsDeviceBound: false, authTime: 100);

            PolicyEvaluation evaluation = _policy.Evaluate(
                [AcrTiers.Aal2], null, null, current, forceInteraction: false, nowUnixSeconds: 150);

            Assert.Equal(PolicyOutcome.InteractionRequired, evaluation.Outcome);
        }

        [Fact]
        public void Phr_maps_to_the_passkey_tier()
        {
            AssuranceResult syncedPasskey = _policy.DeriveAssurance([AuthenticationMethods.Passkey], passkeyIsDeviceBound: false, authTime: 100);

            PolicyEvaluation evaluation = _policy.Evaluate(
                [AcrTiers.PhishingResistant], null, null, syncedPasskey, forceInteraction: false, nowUnixSeconds: 150);

            Assert.Equal(PolicyOutcome.Satisfied, evaluation.Outcome);
        }

        [Fact]
        public void Phrh_requires_a_device_bound_passkey()
        {
            AssuranceResult syncedPasskey = _policy.DeriveAssurance([AuthenticationMethods.Passkey], passkeyIsDeviceBound: false, authTime: 100);

            PolicyEvaluation evaluation = _policy.Evaluate(
                [AcrTiers.PhishingResistantHardware], null, null, syncedPasskey, forceInteraction: false, nowUnixSeconds: 150);

            Assert.Equal(PolicyOutcome.InteractionRequired, evaluation.Outcome);
        }

        [Fact]
        public void An_expired_auth_time_against_max_age_requires_reauthentication()
        {
            AssuranceResult current = _policy.DeriveAssurance([AuthenticationMethods.Passkey], passkeyIsDeviceBound: false, authTime: 100);

            PolicyEvaluation evaluation = _policy.Evaluate(
                [], maxAge: TimeSpan.FromSeconds(30), allowedMethods: null, current, forceInteraction: false, nowUnixSeconds: 200);

            Assert.Equal(PolicyOutcome.InteractionRequired, evaluation.Outcome);
        }

        [Fact]
        public void A_weaker_later_factor_does_not_refresh_a_passkey_tier_against_max_age()
        {
            // The session asserted a device-bound passkey at 100 and later added an email code at
            // 1000, which refreshed the session-wide auth_time. A sensitive operation asking for
            // aal3 within the last minute must not be satisfied by that email code: the tier rests
            // on the passkey, so its freshness does too.
            AssuranceResult current = _policy.DeriveAssurance(
                [AuthenticationMethods.Passkey, AuthenticationMethods.EmailCode],
                passkeyIsDeviceBound: true,
                authTime: 1000,
                passkeyAuthTime: 100);

            PolicyEvaluation stale = _policy.Evaluate(
                [AcrTiers.Aal3], maxAge: TimeSpan.FromSeconds(60), allowedMethods: null, current,
                forceInteraction: false, nowUnixSeconds: 1010);
            Assert.Equal(PolicyOutcome.InteractionRequired, stale.Outcome);

            // The same session still satisfies an aal1 request, which any interactive event
            // legitimately refreshes.
            PolicyEvaluation baseline = _policy.Evaluate(
                [AcrTiers.Aal1], maxAge: TimeSpan.FromSeconds(60), allowedMethods: null, current,
                forceInteraction: false, nowUnixSeconds: 1010);
            Assert.Equal(PolicyOutcome.Satisfied, baseline.Outcome);
        }

        [Fact]
        public void A_fresh_passkey_assertion_satisfies_max_age_at_its_own_tier()
        {
            AssuranceResult current = _policy.DeriveAssurance(
                [AuthenticationMethods.EmailCode, AuthenticationMethods.Passkey],
                passkeyIsDeviceBound: true,
                authTime: 1000,
                passkeyAuthTime: 1000);

            PolicyEvaluation evaluation = _policy.Evaluate(
                [AcrTiers.Aal3], maxAge: TimeSpan.FromSeconds(60), allowedMethods: null, current,
                forceInteraction: false, nowUnixSeconds: 1010);

            Assert.Equal(PolicyOutcome.Satisfied, evaluation.Outcome);
        }

        [Fact]
        public void The_tier_freshness_claim_dates_the_tier_not_the_latest_event()
        {
            // What a relying party sees has to answer the question it asks. A step-up decision
            // keyed on acr=aal3 needs to know when the hardware key was presented — 100 — not when
            // the user last did anything, which the email code moved to 1000.
            AssuranceResult passkeyThenEmailCode = _policy.DeriveAssurance(
                [AuthenticationMethods.Passkey, AuthenticationMethods.EmailCode],
                passkeyIsDeviceBound: true,
                authTime: 1000,
                passkeyAuthTime: 100);

            Assert.Equal(AcrTiers.Aal3, passkeyThenEmailCode.Acr);
            Assert.Equal(100, _policy.TierAuthTime(passkeyThenEmailCode));

            // An aal1 session is carried by whatever it last did, so its own auth_time applies.
            AssuranceResult emailCodeOnly = _policy.DeriveAssurance(
                [AuthenticationMethods.EmailCode], passkeyIsDeviceBound: false, authTime: 1000);

            Assert.Equal(AcrTiers.Aal1, emailCodeOnly.Acr);
            Assert.Equal(1000, _policy.TierAuthTime(emailCodeOnly));
        }

        [Fact]
        public void A_weaker_interaction_cannot_discharge_a_demand_made_of_the_passkey()
        {
            // The request redirected to login at 1000. The user came back having signed in with an
            // email code (auth_time 1005) while the passkey assertion still dates from 100. That
            // interaction discharges nothing for aal3: the demand was made of the passkey, so only
            // a passkey assertion answers it — otherwise the weakest available factor could launder
            // the freshness of the strongest.
            AssuranceResult afterEmailCode = _policy.DeriveAssurance(
                [AuthenticationMethods.Passkey, AuthenticationMethods.EmailCode],
                passkeyIsDeviceBound: true,
                authTime: 1005,
                passkeyAuthTime: 100);

            PolicyEvaluation notDischarged = _policy.Evaluate(
                [AcrTiers.Aal3], maxAge: TimeSpan.FromSeconds(60), allowedMethods: null, afterEmailCode,
                forceInteraction: false, nowUnixSeconds: 1010, interactionSince: 1000);
            Assert.Equal(PolicyOutcome.InteractionRequired, notDischarged.Outcome);

            // The same request after an actual passkey assertion is satisfied, however short the
            // bound was — the user did exactly what was asked, and re-asking would loop.
            AssuranceResult afterPasskey = _policy.DeriveAssurance(
                [AuthenticationMethods.Passkey, AuthenticationMethods.EmailCode],
                passkeyIsDeviceBound: true,
                authTime: 1005,
                passkeyAuthTime: 1005);

            PolicyEvaluation discharged = _policy.Evaluate(
                [AcrTiers.Aal3], maxAge: TimeSpan.FromSeconds(1), allowedMethods: null, afterPasskey,
                forceInteraction: true, nowUnixSeconds: 1010, interactionSince: 1000);
            Assert.Equal(PolicyOutcome.Satisfied, discharged.Outcome);
        }

        [Fact]
        public void A_session_older_than_the_redirect_does_not_discharge_the_demand()
        {
            // A marker left by an abandoned attempt — or planted by someone who navigated the
            // browser to the same URL — carries an instant the session predates, so it makes the
            // demand stricter rather than satisfying it.
            AssuranceResult stale = _policy.DeriveAssurance(
                [AuthenticationMethods.Passkey], passkeyIsDeviceBound: true, authTime: 500);

            PolicyEvaluation evaluation = _policy.Evaluate(
                [], maxAge: null, allowedMethods: null, stale,
                forceInteraction: true, nowUnixSeconds: 1010, interactionSince: 1000);

            Assert.Equal(PolicyOutcome.InteractionRequired, evaluation.Outcome);
        }

        [Fact]
        public void Prompt_login_forces_interaction_even_when_satisfied()
        {
            AssuranceResult current = _policy.DeriveAssurance([AuthenticationMethods.Passkey], passkeyIsDeviceBound: false, authTime: 100);

            PolicyEvaluation evaluation = _policy.Evaluate(
                [], null, null, current, forceInteraction: true, nowUnixSeconds: 150);

            Assert.Equal(PolicyOutcome.InteractionRequired, evaluation.Outcome);
        }
    }
}
