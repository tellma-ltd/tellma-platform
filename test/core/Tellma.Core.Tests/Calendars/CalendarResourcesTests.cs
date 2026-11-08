// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections;
using System.Globalization;
using System.Resources;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Resources;

namespace Tellma.Core.Tests.Calendars
{
    /// <summary>
    ///     Core's month names: every month of both resource-named calendars in the neutral English
    ///     resources, the Arabic and Amharic satellites declaring only keys the neutral set has, and
    ///     the resources reachable by the address packages use.
    /// </summary>
    public class CalendarResourcesTests
    {
        /// <summary>Every month-name key: Umm al-Qura's twelve and the Ethiopian thirteen.</summary>
        private static readonly string[] MonthKeys =
        [
            .. Enumerable.Range(1, 12).Select(static m => $"Calendar_uq_Month{m}"),
            .. Enumerable.Range(1, 13).Select(static m => $"Calendar_et_Month{m}"),
        ];

        [Fact]
        public void The_neutral_resources_name_every_month()
        {
            Dictionary<string, string> neutral = Entries(CultureInfo.InvariantCulture);

            foreach (string key in MonthKeys)
            {
                Assert.True(neutral.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value), $"{key} is missing.");
            }
        }

        [Theory]
        [InlineData("ar")]
        [InlineData("am")]
        public void A_satellite_names_every_month_and_declares_no_key_the_neutral_set_lacks(string language)
        {
            Dictionary<string, string> neutral = Entries(CultureInfo.InvariantCulture);
            Dictionary<string, string> satellite = Entries(CultureInfo.GetCultureInfo(language));

            Assert.Empty(satellite.Keys.Except(neutral.Keys));
            foreach (string key in MonthKeys)
            {
                Assert.True(satellite.ContainsKey(key), $"{key} is missing from {language}.");
                Assert.NotEqual(neutral[key], satellite[key]);
            }
        }

        [Fact]
        public void The_resources_are_at_the_address_packages_use()
        {
            Assert.Equal(CoreStrings.BaseName, Strings.ResourceManager.BaseName);
            Assert.Equal(CoreStrings.Assembly, typeof(Strings).Assembly.GetName().Name);

            ResourceManager byAddress = new(CoreStrings.BaseName, typeof(Strings).Assembly);
            Assert.Equal("Pagumen", byAddress.GetString("Calendar_et_Month13", CultureInfo.InvariantCulture));
        }

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
