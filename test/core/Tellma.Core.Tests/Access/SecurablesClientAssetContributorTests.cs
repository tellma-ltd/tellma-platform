// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Access;
using Tellma.Core.Localization;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The securables section of the client metadata: one entry per registered securable, with
    ///     its owner and the keys of its labels resolved through the neutral shared resources, and the
    ///     humanized fallbacks of the keys none declares.
    /// </summary>
    public class SecurablesClientAssetContributorTests
    {
        [Fact]
        public async Task The_section_lists_every_securable_with_its_owner_and_label_keys()
        {
            await using ServiceProvider provider = TestComposition.Compose(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution
                    .Securables(static securables => securables
                        .Add("acme.CreditExposureReport", "Read", null)
                        .Add(CoreResources.Role, AccessActions.Activate, CoreResources.Role)
                        .Add(CoreResources.SettingsGeneral, AccessActions.Save, null)
                        .Add("core.Settings.gl", AccessActions.Read, null)))))
                .BuildServiceProvider(validateScopes: true);

            // The securables contributor alone: the core chunk also carries the other sections.
            ClientAssetContext context = new(provider);
            provider.GetServices<IClientAssetContributor>().OfType<SecurablesClientAssetContributor>().Single().Contribute(context);

            // Core declares Action_Activate and SettingCategory_general; no other key of these chains.
            string expected = /*lang=json,strict*/ """
                [
                {"resource":"acme.CreditExposureReport","action":"Read","isSensitive":false,"feature":"fixture","resourceLabelKey":"Securable_acme_CreditExposureReport","resourceLabelFallback":"Credit exposure report","actionLabelKey":"Acme_CreditExposureReport_Action_Read","actionLabelFallback":"Read","featureLabelKey":"Feature_fixture","featureLabelFallback":"Fixture"},
                {"resource":"core.Role","action":"Activate","filterRoot":"core.Role","isSensitive":true,"feature":"fixture","resourceLabelKey":"Core_Role_Plural","resourceLabelFallback":"Role","actionLabelKey":"Action_Activate","featureLabelKey":"Feature_fixture","featureLabelFallback":"Fixture"},
                {"resource":"core.Settings.General","action":"Save","isSensitive":true,"feature":"fixture","resourceLabelKey":"SettingCategory_general","actionLabelKey":"Core_Settings_General_Action_Save","actionLabelFallback":"Save","featureLabelKey":"Feature_fixture","featureLabelFallback":"Fixture"},
                {"resource":"core.Settings.gl","action":"Read","isSensitive":false,"feature":"fixture","resourceLabelKey":"SettingCategory_gl","resourceLabelFallback":"Gl","actionLabelKey":"Core_Settings_gl_Action_Read","actionLabelFallback":"Read","featureLabelKey":"Feature_fixture","featureLabelFallback":"Fixture"},
                {"resource":"core.User","action":"Read","filterRoot":"core.User","isSensitive":false,"feature":"core","resourceLabelKey":"Core_User_Plural","resourceLabelFallback":"User","actionLabelKey":"Core_User_Action_Read","actionLabelFallback":"Read","featureLabelKey":"Feature_core","featureLabelFallback":"Core"},
                {"resource":"core.UserStamp","action":"Read","filterRoot":"core.UserStamp","isSensitive":false,"feature":"core","owner":{"resource":"core.User","navigation":"User"},"resourceLabelKey":"Securable_core_UserStamp","resourceLabelFallback":"User stamp","actionLabelKey":"Core_UserStamp_Action_Read","actionLabelFallback":"Read","featureLabelKey":"Feature_core","featureLabelFallback":"Core"}
                ]
                """;

            ClientMetadataSection section = Assert.Single(context.Sections);
            Assert.Equal((ClientMetadataChunk.Core, "securables"), (section.Chunk, section.Name));
            Assert.Equal(string.Concat(expected.Split('\n').Select(static line => line.Trim())), Encoding.UTF8.GetString(section.Json.Span));
        }

        [Fact]
        public void The_first_declared_key_of_a_chain_wins_with_no_fallback()
        {
            SecurablesClientAssetContributor contributor = new(
                SecurableRegistryTests.Compose(),
                new EmptyMetadata(),
                new DeclaredKeys("Gl_Center_Action_Read", "Action_Read", "Securable_gl_Center", "Feature_gl"));

            ClientSecurable securable = contributor.Describe(new SecurableDescriptor("gl.Center", "Read", "gl.Center", false, "gl", null));

            Assert.Equal(
                new ClientSecurable("gl.Center", "Read", "gl.Center", false, "gl", null, "Securable_gl_Center", null, "Gl_Center_Action_Read", null, "Feature_gl", null),
                securable);
        }

        [Theory]
        [InlineData("CreditExposureReport", "Credit exposure report")]
        [InlineData("autoNumberOnPost", "Auto number on post")]
        [InlineData("acme.sales", "Acme sales")]
        [InlineData("GLAccount", "GL account")]
        [InlineData("Q1Report", "Q1 report")]
        [InlineData("general", "General")]
        [InlineData("core-reports_v2", "Core reports v2")]
        [InlineData("Read", "Read")]
        [InlineData("...", "...")]
        public void A_name_humanizes_into_words(string name, string expected)
        {
            Assert.Equal(expected, NameHumanizer.Humanize(name));
        }

        [Theory]
        [InlineData("gl.Center", "Gl_Center")]
        [InlineData("core.Settings.General", "Core_Settings_General")]
        [InlineData("Acme.Report", "Acme_Report")]
        public void A_key_prefix_capitalizes_the_schema_and_underscores_the_dots(string resource, string expected)
        {
            Assert.Equal(expected, SecurablesClientAssetContributor.KeyPrefix(resource));
        }

        [Fact]
        public async Task The_neutral_keys_are_those_of_the_composed_features_assemblies()
        {
            await using ServiceProvider provider = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);
            INeutralStringKeys keys = provider.GetRequiredService<INeutralStringKeys>();

            Assert.True(keys.Declares("Action_Activate"));
            Assert.True(keys.Declares("SettingCategory_general"));
            Assert.False(keys.Declares("action_activate"));
            Assert.False(keys.Declares("Securable_core_Nothing"));
        }

        /// <summary>A model with no entities.</summary>
        private sealed class EmptyMetadata : IEntityMetadataProvider
        {
            /// <inheritdoc />
            public IReadOnlyList<EntityMetadata> All => [];

            /// <inheritdoc />
            public string StorageFingerprint => string.Empty;

            /// <inheritdoc />
            public EntityMetadata Get(Type entityType)
            {
                throw new ArgumentException("The model has no entities.", nameof(entityType));
            }
        }

        /// <summary>Neutral keys a test declares.</summary>
        /// <param name="keys">The keys.</param>
        private sealed class DeclaredKeys(params string[] keys) : INeutralStringKeys
        {
            /// <inheritdoc />
            public bool Declares(string key)
            {
                return keys.Contains(key, StringComparer.Ordinal);
            }
        }
    }
}
