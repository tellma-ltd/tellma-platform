// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Settings
{
    /// <summary>
    ///     The codes a settings save refuses a patch with, each at the patch member it names or at
    ///     <c>Entries</c> and the key. A key's own check raises codes of its feature's family besides.
    /// </summary>
    public static class SettingsValidationCodes
    {
        /// <summary>A field names no typed member and no entry. Argument: <c>field</c>.</summary>
        public const string FieldUnknown = "Settings.FieldUnknown";

        /// <summary>A language the distribution does not offer. Argument: <c>language</c>.</summary>
        public const string LanguageNotOffered = "Settings.LanguageNotOffered";

        /// <summary>Two positions hold one language. Argument: <c>language</c>.</summary>
        public const string LanguagesNotDistinct = "Settings.LanguagesNotDistinct";

        /// <summary>A tertiary language while the secondary one is empty.</summary>
        public const string TertiaryRequiresSecondary = "Settings.TertiaryRequiresSecondary";

        /// <summary>A calendar no composed feature contributes. Argument: <c>calendar</c>.</summary>
        public const string CalendarNotRegistered = "Settings.CalendarNotRegistered";

        /// <summary>The secondary calendar is the primary one.</summary>
        public const string CalendarsNotDistinct = "Settings.CalendarsNotDistinct";

        /// <summary>A zone outside the time-zone value set; an alias of a zone is outside it too. Argument: <c>timeZone</c>.</summary>
        public const string TimeZoneUnknown = "Settings.TimeZoneUnknown";

        /// <summary>A region no offered language offers. Argument: <c>region</c>.</summary>
        public const string RegionNotOffered = "Settings.RegionNotOffered";

        /// <summary>An entry key no composed feature declares. Argument: <c>key</c>.</summary>
        public const string KeyUndeclared = "Settings.KeyUndeclared";

        /// <summary>An entry's value does not read as its key's type. Argument: <c>key</c>.</summary>
        public const string EntryTypeMismatch = "Settings.EntryTypeMismatch";
    }
}
