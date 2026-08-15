// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Hosting;

namespace Tellma.Core.Abstractions.Tests.Hosting
{
    /// <summary>
    ///     The deployment id ends up on wires and in file names, so both its composition and its
    ///     refusal to compose something unusable are pinned.
    /// </summary>
    public class DeploymentIdentityTests
    {
        [Theory]
        [InlineData("etpharma", "Production", "etpharma")]
        [InlineData("etpharma", "production", "etpharma")]
        [InlineData("etpharma", "Staging", "etpharma-staging")]
        [InlineData("identity", "Development", "identity-development")]
        public void Qualifies_the_id_by_environment_outside_production(
            string application, string environmentName, string expected)
        {
            Assert.Equal(expected, new DeploymentIdentity(application, environmentName).DeploymentId);
        }

        [Fact]
        public void Produces_a_colon_free_id_so_the_wire_envelope_stays_unambiguous()
        {
            DeploymentIdentity identity = new("etpharma", "Staging");

            Assert.DoesNotContain(":", identity.DeploymentId, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("EtPharma")]
        [InlineData("et pharma")]
        [InlineData("et_pharma")]
        [InlineData("")]
        public void Refuses_an_application_name_that_is_not_lowercase_kebab_case(string application)
        {
            Assert.Throws<ArgumentException>(() => new DeploymentIdentity(application, "Production"));
        }

        [Fact]
        public void Refuses_an_over_long_application_name()
        {
            string tooLong = new('a', DeploymentIdentity.MaxApplicationLength + 1);

            Assert.Throws<ArgumentException>(() => new DeploymentIdentity(tooLong, "Production"));
        }

        [Theory]
        [InlineData("QA 2")]
        [InlineData("Prod_West")]
        public void Refuses_an_environment_name_that_would_corrupt_the_id(string environmentName)
        {
            // Sanitizing instead would let two environments collapse onto one id, which is precisely
            // what keeps their wire artifacts apart.
            Assert.Throws<ArgumentException>(() => new DeploymentIdentity("etpharma", environmentName));
        }

        [Fact]
        public void Refuses_a_blank_environment_name()
        {
            Assert.Throws<ArgumentException>(() => new DeploymentIdentity("etpharma", "  "));
        }
    }
}
