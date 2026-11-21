// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.AspNetCore.Realtime;

namespace Tellma.Core.AspNetCore.Tests.Realtime
{
    /// <summary>The names the hub computes (its groups, the deployment's application name) and the bounds of its options.</summary>
    public sealed class RealtimeNamingTests
    {
        [Theory]
        [InlineData("acme", "Production", "acme")]
        [InlineData("etpharma", "Staging", "etpharma_staging")]
        [InlineData("acme-erp", "Development", "acme_erp_development")]
        public void The_application_name_is_the_deployment_id_with_underscores(string application, string environment, string expected)
        {
            Assert.Equal(expected, RealtimeApplicationName.From(new DeploymentIdentity(application, environment)));
        }

        [Fact]
        public void A_deployment_id_that_does_not_start_with_a_letter_names_no_application()
        {
            _ = Assert.Throws<ArgumentException>(() => RealtimeApplicationName.From(new DeploymentIdentity("1acme", "Production")));
        }

        [Fact]
        public void A_connection_s_groups_are_its_tenant_s_each_family_apart()
        {
            HubConnectionClaims claims = new(12, 34, "sub-9", "handle-1");

            Assert.Equal(["t12", "t12.u34", "t12.ssub-9", "t12.xhandle-1"], HubGroups.Of(claims));
            Assert.Equal("t12", HubGroups.Tenant(12));
            Assert.Equal("t12.u34", HubGroups.User(12, 34));
        }

        [Fact]
        public void The_same_user_and_session_in_two_tenants_share_no_group()
        {
            string[] first = HubGroups.Of(new HubConnectionClaims(1, 7, "ann", "h"));
            string[] second = HubGroups.Of(new HubConnectionClaims(11, 7, "ann", "h"));

            Assert.Empty(first.Intersect(second, StringComparer.Ordinal));
        }

        [Fact]
        public void The_default_options_are_sound()
        {
            Assert.Empty(Validate(new RealtimeOptions()));
        }

        [Theory]
        [InlineData(0, 150, 1, "KeepAliveInterval")]
        [InlineData(15, 29, 1, "ClientTimeoutInterval")]
        [InlineData(15, 150, 0, "ExpectedInstances")]
        public void Options_out_of_bounds_are_refused(int keepAlive, int clientTimeout, int instances, string member)
        {
            RealtimeOptions options = new()
            {
                KeepAliveInterval = TimeSpan.FromSeconds(keepAlive),
                ClientTimeoutInterval = TimeSpan.FromSeconds(clientTimeout),
                ExpectedInstances = instances,
            };

            Assert.Contains(Validate(options), result => result.MemberNames.Contains(member));
        }

        /// <summary>Validates options as the options check does.</summary>
        /// <param name="options">The options.</param>
        /// <returns>The failures.</returns>
        private static List<ValidationResult> Validate(RealtimeOptions options)
        {
            List<ValidationResult> results = [];
            _ = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
            return results;
        }
    }
}
