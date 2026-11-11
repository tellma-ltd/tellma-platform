// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Tellma.Core.Resources;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>
    ///     The boot page's strings in Core's resources: every one in English, Arabic and Amharic, the
    ///     heading naming the display name by its one argument, and no other string taking any.
    /// </summary>
    public partial class BootStringsTests
    {
        /// <summary>The boot page's string keys, which the build copies into the boot script.</summary>
        private static readonly string[] BootKeys =
        [
            "Boot_Title",
            "Boot_Unsupported",
            "Boot_Cookies",
            "Boot_SignInFailed",
            "Boot_LoadFailed",
            "Boot_TryAgain",
        ];

        [Theory]
        [InlineData("")]
        [InlineData("ar")]
        [InlineData("am")]
        public void Each_language_carries_every_boot_string(string language)
        {
            Dictionary<string, string> strings = Entries(CultureInfo.GetCultureInfo(language));

            foreach (string key in BootKeys)
            {
                Assert.True(strings.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value), $"{key} is missing from '{language}'.");
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("ar")]
        [InlineData("am")]
        public void Only_the_title_takes_an_argument_and_it_is_the_display_name(string language)
        {
            Dictionary<string, string> strings = Entries(CultureInfo.GetCultureInfo(language));

            Assert.Equal(["{displayName}"], Arguments().Matches(strings["Boot_Title"]).Select(static m => m.Value));
            foreach (string key in BootKeys.Where(static k => k != "Boot_Title"))
            {
                Assert.DoesNotContain('{', strings[key]);
            }
        }

        [Theory]
        [InlineData("ar")]
        [InlineData("am")]
        public void A_satellite_translates_every_boot_string(string language)
        {
            Dictionary<string, string> neutral = Entries(CultureInfo.InvariantCulture);
            Dictionary<string, string> satellite = Entries(CultureInfo.GetCultureInfo(language));

            foreach (string key in BootKeys)
            {
                Assert.NotEqual(neutral[key], satellite[key]);
            }
        }

        /// <summary>A message argument: a brace-delimited name.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"\{[^{}]*\}")]
        private static partial Regex Arguments();

        /// <summary>The entries one culture's own resource set declares, without its parents'.</summary>
        /// <param name="culture">The culture; the invariant culture for the neutral set.</param>
        /// <returns>The entries.</returns>
        private static Dictionary<string, string> Entries(CultureInfo culture)
        {
            ResourceSet? set = Strings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
            Assert.NotNull(set);
            return set.Cast<DictionaryEntry>().ToDictionary(static e => (string)e.Key, static e => (string)e.Value!);
        }
    }
}
