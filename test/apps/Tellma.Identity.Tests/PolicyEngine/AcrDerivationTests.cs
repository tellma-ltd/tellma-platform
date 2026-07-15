// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Identity.Services.AuthenticationPolicy;

namespace Tellma.Identity.Tests.PolicyEngine
{
    /// <summary>
    ///     The <c>acr</c>/<c>amr</c> derivation matrix from the actual authentication event.
    /// </summary>
    public sealed class AcrDerivationTests
    {
        private readonly AuthenticationPolicyService _policy = new();

        [Fact]
        public void Device_bound_passkey_is_aal3_with_hardware_key_method()
        {
            AssuranceResult result = _policy.DeriveAssurance([AuthenticationMethods.Passkey], passkeyIsDeviceBound: true, authTime: 100);

            Assert.Equal(AcrTiers.Aal3, result.Acr);
            Assert.Equal(["hwk", "user"], result.Amr);
        }

        [Fact]
        public void Synced_passkey_is_aal2_with_software_key_method()
        {
            AssuranceResult result = _policy.DeriveAssurance([AuthenticationMethods.Passkey], passkeyIsDeviceBound: false, authTime: 100);

            Assert.Equal(AcrTiers.Aal2, result.Acr);
            Assert.Equal(["swk", "user"], result.Amr);
        }

        [Fact]
        public void Password_plus_totp_is_aal2_multifactor()
        {
            AssuranceResult result = _policy.DeriveAssurance(
                [AuthenticationMethods.Password, AuthenticationMethods.Totp], passkeyIsDeviceBound: false, authTime: 100);

            Assert.Equal(AcrTiers.Aal2, result.Acr);
            Assert.Equal(["pwd", "otp", "mfa"], result.Amr);
        }

        [Fact]
        public void Email_code_alone_is_aal1()
        {
            AssuranceResult result = _policy.DeriveAssurance([AuthenticationMethods.EmailCode], passkeyIsDeviceBound: false, authTime: 100);

            Assert.Equal(AcrTiers.Aal1, result.Acr);
            Assert.Equal(["otp"], result.Amr);
        }

        [Fact]
        public void Password_alone_is_aal1_not_multifactor()
        {
            AssuranceResult result = _policy.DeriveAssurance([AuthenticationMethods.Password], passkeyIsDeviceBound: false, authTime: 100);

            Assert.Equal(AcrTiers.Aal1, result.Acr);
            Assert.Equal(["pwd"], result.Amr);
        }

        [Fact]
        public void Social_login_alone_is_aal1()
        {
            AssuranceResult result = _policy.DeriveAssurance([AuthenticationMethods.Google], passkeyIsDeviceBound: false, authTime: 100);

            Assert.Equal(AcrTiers.Aal1, result.Acr);
        }
    }
}
