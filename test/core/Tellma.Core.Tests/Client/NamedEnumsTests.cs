// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Localization;

namespace Tellma.Core.Tests.Client
{
    /// <summary>
    ///     The C# enums a composition names, over the client entity model's test model: each named once
    ///     by the schema that carries it first, and labelled by every schema that carries it.
    /// </summary>
    public sealed class NamedEnumsTests(ClientEntityModelComposition composition) : IClassFixture<ClientEntityModelComposition>
    {
        [Fact]
        public void Each_enum_is_named_by_its_first_site_and_keyed_by_every_sites_schema()
        {
            NamedEnums enums = composition.Services.GetRequiredService<NamedEnums>();

            Assert.Equal("acme.CustomerType: Acme Gl", Describe(enums.Find(typeof(CustomerType))));
            Assert.Equal("gl.CenterType: Gl", Describe(enums.Find(typeof(CenterType))));
            Assert.Equal("acme.Priority: Acme", Describe(enums.Find(typeof(Priority?))));
            Assert.Null(enums.Find(typeof(DayOfWeek)));
            Assert.Equal([("Retail", "Retail"), ("Wholesale", "wholesale-trade")], enums.Find(typeof(CustomerType))!.Members());
        }

        [Fact]
        public void Discovery_reads_entity_properties_then_setting_keys_then_action_arguments()
        {
            IServiceProvider services = composition.Services;
            IReadOnlyList<NamedEnum> all = NamedEnums.Discover(
                services.GetRequiredService<IEntityMetadataProvider>(),
                services.GetRequiredService<ISettingKeyRegistry>().Keys,
                services.GetRequiredService<IStackRegistry>());
            List<string> names = [.. all.Select(static named => named.Name)];

            Assert.Equal(names.Distinct(StringComparer.Ordinal), names);
            Assert.True(names.IndexOf("acme.CustomerType") < names.IndexOf("gl.CenterType"));
            Assert.True(names.IndexOf("gl.CenterType") < names.IndexOf("acme.Priority"));

            // Without the stacks, an enum only an action carries is not discovered.
            Assert.DoesNotContain(
                NamedEnums.Discover(services.GetRequiredService<IEntityMetadataProvider>(), [], stacks: null),
                static named => named.Type == typeof(Priority));
        }

        [Theory]
        [InlineData("Acme_Customer_Action_SetCreditLimit")]
        [InlineData("Acme_Customer_Action_SetCreditLimit_Assignee")]
        [InlineData("Acme_Customer_Assignee")]
        [InlineData("Assignee")]
        [InlineData("Acme_Customer_Action_SetCreditLimit_Reviewers")]
        [InlineData("Acme_Customer_Action_Statement_Language")]
        [InlineData("Acme_Priority_High")]
        [InlineData("Gl_CustomerType_Retail")]
        [InlineData("Acme_CustomerType_Wholesale")]
        public void The_conventional_keys_cover_action_fields_and_every_schemas_enum_keys(string key)
        {
            Assert.Contains(key, ConventionalStringKeys.Of(composition.Services));
        }

        [Fact]
        public void An_entity_reference_fields_keys_drop_its_id_suffix()
        {
            Assert.DoesNotContain("Acme_Customer_Action_SetCreditLimit_AssigneeId", ConventionalStringKeys.Of(composition.Services));
        }

        /// <summary>A named enum's name and key prefixes.</summary>
        /// <param name="named">The enum.</param>
        /// <returns>Its name, a colon and its prefixes.</returns>
        private static string Describe(NamedEnum? named)
        {
            Assert.NotNull(named);
            return $"{named.Name}: {string.Join(' ', named.KeyPrefixes)}";
        }
    }
}
