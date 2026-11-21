// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Globalization;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     Generates the pinned culture data from this host's ICU: every pair of the language
    ///     catalogue read as its Latin-digit Gregorian culture, cleaned, with the platform's
    ///     corrections laid over. The generator tool runs it on the one ICU release the platform
    ///     pins; a suite on a host with that release holds the checked-in data to it.
    /// </summary>
    internal static class CultureDataSource
    {
        /// <summary>
        ///     Where the platform departs from the pinned CLDR release, by pair and field.
        /// </summary>
        /// <remarks>
        ///     <list type="bullet">
        ///         <item>
        ///             CLDR 48 gives Oromo and Tigrinya the month-first numeric date <c>M/d/yy</c>, a
        ///             regression: every earlier release, and Amharic beside them, writes the day
        ///             first, as Ethiopia, Eritrea and Kenya do. A month-first date would be read as
        ///             another day.
        ///         </item>
        ///         <item>
        ///             CLDR keeps the Arabic percent sign (U+066A) for Saudi Arabia and Somalia even
        ///             with Latin digits. Server text writes numbers that stay numbers when pasted,
        ///             so those two take the percent sign every other Arabic region has.
        ///         </item>
        ///     </list>
        /// </remarks>
        private static readonly FrozenDictionary<string, FrozenDictionary<string, string>> Corrections =
            new Dictionary<string, FrozenDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["om-ET"] = Correction("shortDate", "dd/MM/yyyy"),
                ["om-KE"] = Correction("shortDate", "dd/MM/yyyy"),
                ["ti-ER"] = Correction("shortDate", "dd/MM/yyyy"),
                ["ti-ET"] = Correction("shortDate", "dd/MM/yyyy"),
                ["ar-SA"] = Correction("percentSymbol", "%"),
                ["ar-SO"] = Correction("percentSymbol", "%"),
            }.ToFrozenDictionary(StringComparer.Ordinal);

        /// <summary>Every pair of the language catalogue, in catalogue order.</summary>
        /// <returns>The pairs (<c>ar-SA</c>).</returns>
        public static IEnumerable<string> CataloguePairs()
        {
            return LanguageCatalogData.Languages.SelectMany(
                static language => language.Formats.Regions.Select(region => language.Info.Code + "-" + region));
        }

        /// <summary>Generates the data's text form from this host.</summary>
        /// <param name="source">What this host's data is, for a reader: the CLDR and ICU releases and the image.</param>
        /// <returns>The text.</returns>
        /// <exception cref="InvalidOperationException">The host cannot put a catalogue pair on the Gregorian calendar.</exception>
        /// <exception cref="CultureNotFoundException">The host lacks a catalogue pair.</exception>
        public static string Generate(string source)
        {
            List<KeyValuePair<string, CultureSnapshot>> cultures = [];
            foreach (string pair in CataloguePairs())
            {
                CultureInfo culture = NegotiatedCultures.CreateSource(pair);
                if (culture.DateTimeFormat.Calendar is not GregorianCalendar)
                {
                    throw new InvalidOperationException($"This host offers no Gregorian calendar for the culture '{pair}'.");
                }

                var snapshot = CultureSnapshot.Read(culture);
                if (Corrections.TryGetValue(pair, out FrozenDictionary<string, string>? corrections))
                {
                    snapshot = snapshot.With(corrections);
                }

                cultures.Add(new KeyValuePair<string, CultureSnapshot>(pair, snapshot));
            }

            return CultureDataFile.Write(source, CultureDataFile.HostFingerprint(), cultures);
        }

        /// <summary>One corrected field.</summary>
        /// <param name="field">The field's name.</param>
        /// <param name="value">The corrected value's text form.</param>
        /// <returns>The correction.</returns>
        private static FrozenDictionary<string, string> Correction(string field, string value)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal) { [field] = value }.ToFrozenDictionary(StringComparer.Ordinal);
        }
    }
}
