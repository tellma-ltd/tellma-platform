// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Identity.Services.AuthenticationPolicy;

namespace Tellma.Identity.Tests.PolicyEngine
{
    /// <summary>
    ///     The resources a permanent authorization covers: a grant is covered only when every
    ///     resource it names was recorded, compared ordinally, so remembered consent for one place
    ///     never answers for another, and a malformed record covers nothing.
    /// </summary>
    public sealed class AuthorizationResourcesTests
    {
        private const string Origin = "https://acme.example.com";
        private const string Tenant17 = Origin + "/17/mcp";
        private const string Tenant18 = Origin + "/18/mcp";

        [Fact]
        public void A_recorded_set_covers_its_own_resources_and_any_subset()
        {
            Dictionary<string, JsonElement> properties = Recorded(Tenant17, Origin);

            Assert.True(AuthorizationResources.Covers(properties, [Tenant17]));
            Assert.True(AuthorizationResources.Covers(properties, [Origin, Tenant17]));
            Assert.True(AuthorizationResources.Covers(properties, []));
        }

        [Theory]
        [InlineData(Tenant18)]
        [InlineData("https://globex.example.com")]
        [InlineData("https://ACME.example.com/17/mcp")]
        [InlineData(Tenant17 + "/")]
        public void A_recorded_set_covers_no_other_place(string resource)
        {
            Assert.False(AuthorizationResources.Covers(Recorded(Tenant17), [resource]));
            Assert.False(AuthorizationResources.Covers(Recorded(Tenant17), [Tenant17, resource]));
        }

        [Fact]
        public void An_authorization_without_a_record_covers_only_a_grant_that_names_nothing()
        {
            Dictionary<string, JsonElement> properties = [];

            Assert.True(AuthorizationResources.Covers(properties, []));
            Assert.False(AuthorizationResources.Covers(properties, [Origin]));
        }

        [Fact]
        public void A_malformed_record_covers_nothing_it_does_not_spell_out()
        {
            Dictionary<string, JsonElement> notAnArray = new()
            {
                [AuthorizationResources.Property] = JsonSerializer.SerializeToElement(Origin),
            };
            Dictionary<string, JsonElement> mixed = new()
            {
                [AuthorizationResources.Property] = JsonSerializer.SerializeToElement<object?[]>([Tenant17, 17, null, ""]),
            };

            Assert.False(AuthorizationResources.Covers(notAnArray, [Origin]));
            Assert.Equal([Tenant17], AuthorizationResources.Read(mixed));
        }

        [Fact]
        public void The_record_is_written_distinct_and_in_ordinal_order()
        {
            Dictionary<string, JsonElement> properties = [];
            AuthorizationResources.Write(properties, [Tenant18, Origin, Tenant18, Tenant17]);

            Assert.Equal(
                JsonSerializer.Serialize<string[]>([Origin, Tenant17, Tenant18]),
                properties[AuthorizationResources.Property].GetRawText());
        }

        /// <summary>A properties bag recording the given resources.</summary>
        private static Dictionary<string, JsonElement> Recorded(params string[] resources)
        {
            Dictionary<string, JsonElement> properties = [];
            AuthorizationResources.Write(properties, resources);
            return properties;
        }
    }
}
