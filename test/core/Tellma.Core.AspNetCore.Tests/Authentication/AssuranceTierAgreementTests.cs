// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Access;
using Tellma.Core.Tenancy;
using Tellma.Identity.Services.AuthenticationPolicy;

namespace Tellma.Core.AspNetCore.Tests.Authentication
{
    /// <summary>
    ///     The host and the platform's identity server rank assurance alike: the class a step-up asks
    ///     for is one the identity server can satisfy, a session the identity server deems to satisfy a
    ///     class passes the host's bar of that class, and a list demands the same tier on both sides.
    ///     A disagreement would send a user who just stepped up straight back to the identity server.
    /// </summary>
    public sealed class AssuranceTierAgreementTests
    {
        /// <summary>The identity server's policy engine.</summary>
        private readonly AuthenticationPolicyService _engine = new();

        /// <summary>The tiers a session can hold.</summary>
        /// <returns>The tiers.</returns>
        public static TheoryData<string> HeldTiers()
        {
            return [AcrTiers.Aal1, AcrTiers.Aal2, AcrTiers.Aal3];
        }

        /// <summary>The classes a bar or a request can name, each paired with a tier a session holds.</summary>
        /// <returns>The pairs.</returns>
        public static TheoryData<string, string> HeldAndRequired()
        {
            string[] required = [AcrTiers.Aal1, AcrTiers.Aal2, AcrTiers.Aal3, AcrTiers.PhishingResistant, AcrTiers.PhishingResistantHardware];
            TheoryData<string, string> pairs = [];
            foreach (string held in new[] { AcrTiers.Aal1, AcrTiers.Aal2, AcrTiers.Aal3 })
            {
                foreach (string acr in required)
                {
                    pairs.Add(held, acr);
                }
            }

            return pairs;
        }

        [Fact]
        public void The_default_step_up_class_is_one_the_identity_server_can_satisfy()
        {
            PolicyEvaluation evaluation = _engine.Evaluate([StepUpBar.DefaultAcr], null, null, null, forceInteraction: false, nowUnixSeconds: 1_000);

            Assert.NotEqual(PolicyOutcome.Unsatisfiable, evaluation.Outcome);
            Assert.Equal(AcrTiers.Aal2, evaluation.RequiredTier);
        }

        [Theory]
        [MemberData(nameof(HeldAndRequired))]
        public void A_session_passes_the_hosts_bar_exactly_when_the_identity_server_deems_it_satisfied(string held, string required)
        {
            AssuranceResult session = new(held, ["hwk", "user"], ["passkey"], AuthTime: 900, PasskeyAuthTime: 900);

            bool engine = _engine.Evaluate([required], null, null, session, forceInteraction: false, nowUnixSeconds: 1_000).Outcome == PolicyOutcome.Satisfied;

            Assert.Equal(engine, AssuranceTiers.Satisfies(held, required));
        }

        [Theory]
        [InlineData("urn:tellma:acr:aal1 urn:tellma:acr:aal3")]
        [InlineData("urn:tellma:acr:aal3 urn:tellma:acr:aal1")]
        [InlineData("urn:tellma:acr:aal2 phrh")]
        [InlineData("phr urn:tellma:acr:aal1")]
        [InlineData("urn:tellma:acr:aal1")]
        public void A_list_demands_the_same_tier_of_both(string list)
        {
            PolicyEvaluation evaluation = _engine.Evaluate(list.Split(' '), null, null, null, forceInteraction: false, nowUnixSeconds: 1_000);

            Assert.Equal(AssuranceTiers.Rank(evaluation.RequiredTier), AssuranceTiers.RankList(list));
        }

        [Theory]
        [MemberData(nameof(HeldTiers))]
        public void Every_tier_the_identity_server_issues_is_ranked_by_the_host(string held)
        {
            Assert.NotNull(AssuranceTiers.Rank(held));
        }
    }
}
