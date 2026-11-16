// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;

namespace Tellma.Core.Settings
{
    // The four sections are spec 0013 §1.2's client metadata, all in the Core chunk, which is served
    // anonymously: nothing here is a tenant's, and a secret key has no default to show.
    /// <summary>
    ///     Writes the client metadata the settings editor and the locale pickers render from: the
    ///     offered languages with their formats, the calendar codes, the setting keys and the
    ///     settings tabs.
    /// </summary>
    /// <param name="languages">The language catalogue.</param>
    /// <param name="calendars">The calendar registry.</param>
    /// <param name="keys">The setting keys.</param>
    internal sealed class SettingsClientAssetContributor(ILanguageCatalog languages, ICalendarRegistry calendars, ISettingKeyRegistry keys)
        : IClientAssetContributor
    {
        /// <summary>The section of the offered languages.</summary>
        public const string LanguagesSection = "languages";

        /// <summary>The section of the calendar codes.</summary>
        public const string CalendarsSection = "calendars";

        /// <summary>The section of the setting keys.</summary>
        public const string SettingKeysSection = "settingKeys";

        /// <summary>The section of the settings tabs.</summary>
        public const string SettingCategoriesSection = "settingCategories";

        /// <inheritdoc />
        public void Contribute(ClientAssetContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- The offered languages in declaration order, each with its formats ---
            List<ClientLanguage> offered = [.. languages.Offered.Select(Describe)];
            context.Section(ClientMetadataChunk.Core, LanguagesSection, writer => JsonSerializer.Serialize(writer, offered, TellmaJson.Options));

            // --- The calendar codes, the platform's first ---
            IReadOnlyList<string> codes = calendars.Codes;
            context.Section(ClientMetadataChunk.Core, CalendarsSection, writer => JsonSerializer.Serialize(writer, codes, TellmaJson.Options));

            // --- The keys in registry order, then the tabs in tab order ---
            IReadOnlyList<SettingKeyDescriptor> descriptors = keys.Describe();
            context.Section(ClientMetadataChunk.Core, SettingKeysSection, writer => JsonSerializer.Serialize(writer, descriptors, TellmaJson.Options));
            IReadOnlyList<SettingCategoryDescriptor> categories = keys.Categories;
            context.Section(ClientMetadataChunk.Core, SettingCategoriesSection, writer => JsonSerializer.Serialize(writer, categories, TellmaJson.Options));
        }

        /// <summary>A language as the client renders it.</summary>
        /// <param name="language">The language.</param>
        /// <returns>The entry.</returns>
        private ClientLanguage Describe(LanguageInfo language)
        {
            LanguageFormats formats = languages.Formats(language.Code);
            return new ClientLanguage(
                language.Code,
                language.NativeName,
                language.IsRightToLeft ? "rtl" : "ltr",
                formats.DefaultRegion,
                [.. formats.Regions.Order(StringComparer.Ordinal)],
                formats.NativeDigits);
        }
    }

    /// <summary>An offered language in the client metadata.</summary>
    /// <param name="Code">The language's code.</param>
    /// <param name="NativeName">The language's name in itself.</param>
    /// <param name="Dir">The writing direction, <c>ltr</c> or <c>rtl</c>.</param>
    /// <param name="Region">The distribution's default region for the language.</param>
    /// <param name="Regions">The regions the language is offered with, in ordinal order.</param>
    /// <param name="NativeDigits">The language's native digit system (<c>arab</c>); null, written as such, when its digits are Latin.</param>
    internal sealed record ClientLanguage(
        string Code,
        string NativeName,
        string Dir,
        string Region,
        IReadOnlyList<string> Regions,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? NativeDigits);
}
