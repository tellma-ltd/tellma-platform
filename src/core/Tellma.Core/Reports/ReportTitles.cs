// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Reports;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Localization;

namespace Tellma.Core.Reports
{
    // Spec 0033 §2.1: a report saved with no title takes "{entity} – {kind}", the root's plural label
    // and the kind, in each content slot's own tenant language, so a quick ad-hoc report needs no
    // name. Each language pack orders and separates the two parts its own way; the result is cut to
    // the title's 255 characters. A root the model does not name (the validator refuses it) renders
    // as the name itself, so the default never fails the save that the validator will refuse anyway.
    /// <summary>Renders a stored report's default title in a tenant's content languages.</summary>
    /// <param name="texts">The composed features' shared resources, whose default-title text renders.</param>
    /// <param name="labels">The labels by convention, which render the root's plural label.</param>
    /// <param name="negotiator">The localization negotiator, which builds each slot's localization.</param>
    /// <param name="metadata">The model's entity metadata, which resolves the root's name to its class.</param>
    internal sealed class ReportTitles(ResourceTexts texts, ILabelProvider labels, ILocalizationNegotiator negotiator, IEntityMetadataProvider metadata)
    {
        /// <summary>The resource key of the default title.</summary>
        public const string DefaultTitleKey = "Core_Report_DefaultTitle";

        /// <summary>The most characters a title holds.</summary>
        public const int MaxLength = 255;

        /// <summary>The most content slots a multilingual text group has.</summary>
        public const int MaxSlots = 3;

        /// <summary>The number of content slots a tenant configures: one per tenant language, at most three, and one without settings.</summary>
        /// <param name="settings">The tenant's settings; null outside a tenant's request.</param>
        /// <returns>The number of slots, at least one.</returns>
        public static int Slots(TenantSettings? settings)
        {
            return settings is null ? 1 : Math.Clamp(settings.Languages.Count, 1, MaxSlots);
        }

        /// <summary>The default title of a report over a root and of a kind, in one content slot's tenant language.</summary>
        /// <param name="root">The report's root entity name.</param>
        /// <param name="kind">The report's kind.</param>
        /// <param name="settings">The tenant's settings, whose languages name each slot's language; null for the current culture.</param>
        /// <param name="slot">The zero-based content slot.</param>
        /// <returns>The title, at most 255 characters.</returns>
        public string Default(string? root, ReportKind kind, TenantSettings? settings, int slot)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(slot);

            // --- The slot's language: its tenant language, negotiated as a preference over nothing else ---
            IDisposable? scope = null;
            if (settings is not null && slot < settings.Languages.Count && settings.Languages[slot] is LanguageInfo language)
            {
                LocalizationContext localization = negotiator.Negotiate(new NegotiationInput(
                    AcceptLanguage: null,
                    CalendarHeader: null,
                    TimeZoneHeader: null,
                    PreferredLanguage: language.Code,
                    PreferredCalendar: null,
                    PreferredTimeZone: null,
                    PreferredRegion: null,
                    MessageLanguage: null,
                    MessageCalendar: null,
                    settings));
                scope = LocalizationScope.Enter(localization);
            }

            // --- The text, in that language ---
            using (scope)
            {
                string entity = Entity(root);
                Dictionary<string, object?> arguments = new(StringComparer.Ordinal)
                {
                    ["entity"] = entity,
                    ["kind"] = kind.ToString(),
                };
                string text = texts.Render(DefaultTitleKey, arguments) ?? $"{entity} – {kind}";
                return Cut(text);
            }
        }

        /// <summary>Cuts a text to a title's length, never between the two halves of a surrogate pair.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The text, at most 255 characters.</returns>
        internal static string Cut(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            if (text.Length <= MaxLength)
            {
                return text;
            }

            int length = char.IsHighSurrogate(text[MaxLength - 1]) ? MaxLength - 1 : MaxLength;
            return text[..length];
        }

        /// <summary>The root's plural label, or the root's name when the model does not name it.</summary>
        /// <param name="root">The root's entity name.</param>
        /// <returns>The label.</returns>
        private string Entity(string? root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return string.Empty;
            }

            EntityMetadata? entity = metadata.All.FirstOrDefault(candidate => string.Equals(candidate.Name, root, StringComparison.Ordinal));
            return entity is null ? root : labels.EntityLabel(entity.ClrType, plural: true);
        }
    }
}
