// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Tellma.Connector.AcsEmail.Adapter.Tests.Infrastructure;

namespace Tellma.Connector.AcsEmail.Adapter.Tests.Sending
{
    /// <summary>Covers the configuration this transport refuses to start on.</summary>
    public class AcsEmailOptionsValidatorTests
    {
        [Fact]
        public void Accepts_the_harness_configuration()
        {
            // The harness calls its configuration valid, so the two have to agree: a default that
            // the validator would reject would make every sending test run an impossible setup.
            ValidateOptionsResult result = Validate(AcsSenderHarness.DefaultOptions());

            Assert.True(result.Succeeded, result.FailureMessage);
        }

        [Fact]
        public void Rejects_a_configured_sender_display_name()
        {
            AcsEmailOptions options = AcsSenderHarness.DefaultOptions();
            options.From.DisplayName = "Tellma";

            // ACS refuses a senderAddress carrying a display name, and takes the name from the
            // domain's MailFrom address instead, so the setting can only ever be a silent no-op.
            ValidateOptionsResult result = Validate(options);

            Assert.True(result.Failed);
            Assert.Contains("MailFrom", Assert.Single(result.Failures), StringComparison.Ordinal);
        }

        private static ValidateOptionsResult Validate(AcsEmailOptions options)
        {
            return new AcsEmailOptionsValidator().Validate(Options.DefaultName, options);
        }
    }
}
