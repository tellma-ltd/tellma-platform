// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Security.Claims;
using Tellma.Identity.Services.AuthenticationPolicy;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Tests.PolicyEngine
{
    /// <summary>
    ///     How one authentication event merges into a session's evidence — the seam that produces
    ///     the device-bound flag and the passkey timestamp the policy engine then reasons about.
    ///     Tests that hand those values in as literals cannot see a regression here.
    /// </summary>
    public sealed class SessionCompositionTests
    {
        [Fact]
        public void A_synced_passkey_supersedes_an_earlier_device_bound_one()
        {
            // The two-credential setup §8.1 encourages: a hardware key used at 100, then the synced
            // backup at 200. The session must describe what was just presented — carrying the
            // hardware key's classification forward would let the synced credential inherit aal3.
            ClaimsPrincipal session = SessionFrom(
                TellmaSignInService.ComposeSession(
                    null, new SignInEvidence(AuthenticationMethods.Passkey, PasskeyIsDeviceBound: true), authTime: 100));

            TellmaSignInService.SessionState afterSynced = TellmaSignInService.ComposeSession(
                session, new SignInEvidence(AuthenticationMethods.Passkey, PasskeyIsDeviceBound: false), authTime: 200);

            Assert.Equal("false", ClaimValue(afterSynced, SignInClaims.PasskeyDeviceBound));
            Assert.Equal("200", ClaimValue(afterSynced, SignInClaims.PasskeyAuthTime));
        }

        [Fact]
        public void A_non_passkey_event_preserves_the_passkey_evidence_and_its_timestamp()
        {
            // An email-code step-up must neither downgrade the session's passkey classification nor
            // re-date it: the tier still rests on the hardware key asserted at 100, and dating it
            // from the email code is exactly how a weaker factor would launder its freshness.
            ClaimsPrincipal session = SessionFrom(
                TellmaSignInService.ComposeSession(
                    null, new SignInEvidence(AuthenticationMethods.Passkey, PasskeyIsDeviceBound: true), authTime: 100));

            TellmaSignInService.SessionState afterEmailCode = TellmaSignInService.ComposeSession(
                session, new SignInEvidence(AuthenticationMethods.EmailCode), authTime: 1000);

            Assert.Equal("true", ClaimValue(afterEmailCode, SignInClaims.PasskeyDeviceBound));
            Assert.Equal("100", ClaimValue(afterEmailCode, SignInClaims.PasskeyAuthTime));
            Assert.Equal("1000", ClaimValue(afterEmailCode, Claims.AuthenticationTime));

            // Methods accumulate, and the session identifier is preserved (this is a step-up).
            Assert.Equal(
                [AuthenticationMethods.Passkey, AuthenticationMethods.EmailCode],
                afterEmailCode.Claims.Where(c => c.Type == TellmaClaims.Methods).Select(static c => c.Value));
            Assert.False(afterEmailCode.IsNewSession);
        }

        [Fact]
        public void A_session_that_never_asserted_a_passkey_carries_no_timestamp()
        {
            // 0 rather than the event time: freshness for a passkey-borne tier must read this as
            // infinitely stale, not as "just now".
            TellmaSignInService.SessionState session = TellmaSignInService.ComposeSession(
                null, new SignInEvidence(AuthenticationMethods.EmailCode), authTime: 500);

            Assert.Equal("0", ClaimValue(session, SignInClaims.PasskeyAuthTime));
            Assert.Equal("false", ClaimValue(session, SignInClaims.PasskeyDeviceBound));
            Assert.True(session.IsNewSession);
        }

        [Fact]
        public void A_first_sign_in_mints_a_session_and_dates_its_passkey_evidence()
        {
            TellmaSignInService.SessionState session = TellmaSignInService.ComposeSession(
                null, new SignInEvidence(AuthenticationMethods.Passkey, PasskeyIsDeviceBound: true), authTime: 300);

            Assert.True(session.IsNewSession);
            Assert.False(string.IsNullOrEmpty(session.Sid));
            Assert.Equal("300", ClaimValue(session, SignInClaims.PasskeyAuthTime));
            Assert.Equal("300", ClaimValue(session, Claims.AuthenticationTime));
        }

        /// <summary>Turns composed state into the cookie principal a later event would merge into.</summary>
        private static ClaimsPrincipal SessionFrom(TellmaSignInService.SessionState state)
        {
            return new ClaimsPrincipal(new ClaimsIdentity(state.Claims, "Test"));
        }

        /// <summary>Reads a single claim's value from composed state.</summary>
        private static string? ClaimValue(TellmaSignInService.SessionState state, string claimType)
        {
            return state.Claims.SingleOrDefault(claim => claim.Type == claimType)?.Value;
        }
    }
}
