// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Localization;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>The key grammar: entity prefixes, schema prefixes, action segments and keys built from dotted keys.</summary>
    public class StringKeyConventionsTests
    {
        [Theory]
        [InlineData("gl.Center", "Gl_Center")]
        [InlineData("core.Settings.General", "Core_Settings_General")]
        [InlineData("Acme.Report", "Acme_Report")]
        public void A_key_prefix_capitalizes_the_schema_and_underscores_the_dots(string resource, string expected)
        {
            Assert.Equal(expected, StringKeyConventions.KeyPrefix(resource));
        }

        [Theory]
        [InlineData("gl.Center", "Gl")]
        [InlineData("fixture.WidgetPart", "Fixture")]
        [InlineData("plain", "Plain")]
        public void A_schema_prefix_is_the_capitalized_first_segment(string name, string expected)
        {
            Assert.Equal(expected, StringKeyConventions.SchemaPrefix(name));
        }

        [Theory]
        [InlineData("take-over", "TakeOver")]
        [InlineData("preferences/set", "PreferencesSet")]
        [InlineData("Activate", "Activate")]
        [InlineData("sendNow", "SendNow")]
        [InlineData("reset_password", "ResetPassword")]
        public void An_action_segment_is_the_name_pascal_cased_without_separators(string action, string expected)
        {
            Assert.Equal(expected, StringKeyConventions.ActionSegment(action));
        }

        [Fact]
        public void A_key_built_from_a_dotted_key_underscores_the_dots_and_keeps_the_hyphens()
        {
            Assert.Equal(
                "Notification_core_job_pending-confirmation",
                StringKeyConventions.FromDottedKey("Notification_", "core.job.pending-confirmation"));
        }
    }
}
