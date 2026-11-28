// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Composition;
using Tellma.Core.Localization;
using Tellma.Core.Settings;

namespace Tellma.Core.Tests.Settings
{
    /// <summary>
    ///     The client metadata the settings editor and the locale pickers render from: the offered
    ///     languages with their formats, the calendar codes, the setting keys and the settings tabs,
    ///     all in the core chunk.
    /// </summary>
    public sealed class SettingsClientAssetContributorTests : IAsyncDisposable
    {
        private readonly SettingsTestHost host = new();

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return host.DisposeAsync();
        }

        [Fact]
        public void The_four_sections_are_written_to_the_core_chunk()
        {
            ClientAssetContext context = Contribute();

            Assert.Equal(["languages", "calendars", "settingKeys", "settingCategories"], context.Sections.Select(static section => section.Name));
            Assert.All(context.Sections, static section => Assert.Equal(ClientMetadataChunk.Core, section.Chunk));
        }

        [Fact]
        public void Each_offered_language_carries_its_direction_regions_and_native_digits()
        {
            using JsonDocument languages = Section(Contribute(), "languages");
            JsonElement[] offered = [.. languages.RootElement.EnumerateArray()];

            Assert.Equal(["en", "ar"], offered.Select(static language => language.GetProperty("code").GetString()));
            Assert.Equal(["ltr", "rtl"], offered.Select(static language => language.GetProperty("dir").GetString()));

            // Latin digits are written as null, never left out; the regions are in ordinal order.
            Assert.Equal(JsonValueKind.Null, offered[0].GetProperty("nativeDigits").ValueKind);
            Assert.Equal("arab", offered[1].GetProperty("nativeDigits").GetString());
            string?[] regions = [.. offered[1].GetProperty("regions").EnumerateArray().Select(static region => region.GetString())];
            Assert.Equal(regions.Order(StringComparer.Ordinal), regions);
            Assert.Contains(offered[1].GetProperty("region").GetString(), regions);
        }

        [Fact]
        public void The_calendars_are_the_registrys_codes_the_platforms_first()
        {
            using JsonDocument calendars = Section(Contribute(), "calendars");

            Assert.Equal(["gc", "uq", "et"], calendars.RootElement.EnumerateArray().Select(static code => code.GetString()));
        }

        [Fact]
        public void Each_key_is_described_with_its_shape_its_default_and_its_label()
        {
            using JsonDocument keys = Section(Contribute(), "settingKeys");
            var byName = keys.RootElement.EnumerateArray().ToDictionary(static key => key.GetProperty("name").GetString()!);

            Assert.Equal(["acme.posting.autoNumber", "acme.posting.width", "acme.period", "acme.codes", "acme.erp.apiKey"], byName.Keys);

            // A flag of a derived group, client-visible.
            JsonElement autoNumber = byName["acme.posting.autoNumber"];
            Assert.Equal("acme", autoNumber.GetProperty("category").GetString());
            Assert.Equal("posting", autoNumber.GetProperty("group").GetString());
            Assert.Equal("Client", autoNumber.GetProperty("visibility").GetString());
            Assert.Equal("Boolean", autoNumber.GetProperty("valueType").GetString());
            Assert.False(autoNumber.GetProperty("isNullable").GetBoolean());
            Assert.False(autoNumber.GetProperty("isArray").GetBoolean());
            Assert.True(autoNumber.GetProperty("defaultJson").GetBoolean());
            Assert.Equal("Setting_acme_posting_autoNumber", autoNumber.GetProperty("labelKey").GetString());
            Assert.False(autoNumber.TryGetProperty("descriptionKey", out _));

            // An enum names its named enum, by the key's schema, and its members in declaration order; a two-segment key has no group.
            JsonElement period = byName["acme.period"];
            Assert.Equal("Enum", period.GetProperty("valueType").GetString());
            Assert.Equal("acme.Cadence", period.GetProperty("enumType").GetString());
            Assert.Equal(["Monthly", "Quarterly"], period.GetProperty("enumValues").EnumerateArray().Select(static value => value.GetString()));
            Assert.False(period.TryGetProperty("group", out _));

            // An array, and a secret whose default is never shown.
            Assert.True(byName["acme.codes"].GetProperty("isArray").GetBoolean());
            Assert.Equal("Int32", byName["acme.codes"].GetProperty("valueType").GetString());
            Assert.Equal("Secret", byName["acme.erp.apiKey"].GetProperty("visibility").GetString());
            Assert.True(byName["acme.erp.apiKey"].GetProperty("isNullable").GetBoolean());
            Assert.Equal(JsonValueKind.Null, byName["acme.erp.apiKey"].GetProperty("defaultJson").ValueKind);
        }

        [Fact]
        public void The_tabs_are_general_then_each_category()
        {
            using JsonDocument categories = Section(Contribute(), "settingCategories");

            Assert.Equal(
                /*lang=json,strict*/ """[{"name":"general","resource":"core.Settings.General","order":0,"labelKey":"SettingCategory_general"},{"name":"acme","resource":"core.Settings.acme","order":1,"labelKey":"SettingCategory_acme"}]""",
                categories.RootElement.GetRawText());
        }

        [Fact]
        public void A_key_names_its_description_only_when_a_composed_resource_declares_it()
        {
            SettingKeyRegistry registry = new(
                host.Services.GetRequiredService<TellmaCompositionRecord>(),
                new DeclaredKeys("Setting_acme_posting_width_Description"),
                host.Services.GetRequiredService<IEntityMetadataProvider>());

            IReadOnlyList<SettingKeyDescriptor> keys = registry.Describe();

            Assert.Equal("Setting_acme_posting_width_Description", keys.Single(static key => key.Name == "acme.posting.width").DescriptionKey);
            Assert.All(keys.Where(static key => key.Name != "acme.posting.width"), static key => Assert.Null(key.DescriptionKey));
        }

        /// <summary>Runs the settings contributor alone.</summary>
        /// <returns>The context it wrote to.</returns>
        private ClientAssetContext Contribute()
        {
            ClientAssetContext context = new(host.Services);
            host.Services.GetServices<IClientAssetContributor>().OfType<SettingsClientAssetContributor>().Single().Contribute(context);
            return context;
        }

        /// <summary>One section's JSON.</summary>
        /// <param name="context">The context.</param>
        /// <param name="name">The section.</param>
        /// <returns>The parsed JSON.</returns>
        private static JsonDocument Section(ClientAssetContext context, string name)
        {
            return JsonDocument.Parse(context.Sections.Single(section => section.Name == name).Json);
        }

        /// <summary>Neutral resources that declare the given keys and no other.</summary>
        /// <param name="keys">The declared keys.</param>
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
