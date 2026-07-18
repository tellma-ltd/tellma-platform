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
            Assert.Same(current, evaluation.Assurance);
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
        public void Prompt_login_forces_interaction_even_when_satisfied()
        {
            AssuranceResult current = _policy.DeriveAssurance([AuthenticationMethods.Passkey], passkeyIsDeviceBound: false, authTime: 100);

            PolicyEvaluation evaluation = _policy.Evaluate(
                [], null, null, current, forceInteraction: true, nowUnixSeconds: 150);

            Assert.Equal(PolicyOutcome.InteractionRequired, evaluation.Outcome);
        }
    }
}
