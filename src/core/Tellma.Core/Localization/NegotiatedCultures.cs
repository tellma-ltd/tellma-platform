// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Globalization;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     The cultures server text is formatted in: one read-only culture per language and region,
    ///     built once and shared.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A negotiated culture is named <c>&lt;language&gt;-&lt;region&gt;</c> (<c>ar-SA</c>),
    ///         so resources resolve through <c>ar-SA</c>, <c>ar</c> and the neutral strings. Its
    ///         formats are those of <c>&lt;language&gt;-&lt;region&gt;-u-nu-latn</c> under the
    ///         Gregorian calendar, cleaned:
    ///     </para>
    ///     <list type="bullet">
    ///         <item>Latin digits and Latin-digit separators, so a number prints as
    ///         <c>1,234,567.89</c> in Arabic too.</item>
    ///         <item>No bidirectional controls in any pattern or symbol, so a date or an amount
    ///         pasted from an email stays a date or a number.</item>
    ///         <item>The Gregorian calendar, so an incidental <c>ToString()</c> prints Gregorian;
    ///         other calendars render only through the platform's calendar systems.</item>
    ///     </list>
    ///     <para>
    ///         For every pair of the language catalogue the formats come from the pinned culture
    ///         data, so server text reads the same on every host whatever its ICU release. A pair
    ///         of a language a distribution added has no pinned data and takes the host's.
    ///     </para>
    ///     <para>
    ///         An instance is kept for the life of the process: the calendar systems remember what
    ///         they read from a culture by instance.
    ///     </para>
    /// </remarks>
    internal static class NegotiatedCultures
    {
        /// <summary>The Unicode extension that selects Latin digits.</summary>
        private const string LatinDigits = "-u-nu-latn";

        private static readonly string[] Digits = ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9"];

        /// <summary>The cultures built so far, by pair, without regard to case.</summary>
        private static readonly ConcurrentDictionary<string, CultureInfo> Cache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The culture of a language and region.</summary>
        /// <param name="language">The language code, as the catalogue spells it.</param>
        /// <param name="region">The region code, as the catalogue spells it.</param>
        /// <returns>The culture: read-only, and the same instance on every call.</returns>
        public static CultureInfo Get(string language, string region)
        {
            return Cache.GetOrAdd(language + "-" + region, static pair => Create(pair));
        }

        /// <summary>
        ///     The host's culture of a pair with Latin digits, on the Gregorian calendar: what the
        ///     pinned data is generated from, and what a pair without pinned data takes its formats
        ///     from.
        /// </summary>
        /// <param name="pair">The language and region (<c>ar-SA</c>).</param>
        /// <returns>A new culture, not read-only.</returns>
        /// <exception cref="CultureNotFoundException">The host has no such culture.</exception>
        public static CultureInfo CreateSource(string pair)
        {
            CultureInfo culture = new(pair + LatinDigits, useUserOverride: false);
            ForceGregorian(culture);
            return culture;
        }

        /// <summary>Builds the culture of a pair.</summary>
        /// <param name="pair">The language and region.</param>
        /// <returns>The read-only culture.</returns>
        private static CultureInfo Create(string pair)
        {
            // --- The culture, named by the pair ---
            CultureInfo culture = New(pair);
            ForceGregorian(culture);

            // --- Its formats: pinned for a catalogue pair, the host's otherwise ---
            if (!CultureDataFile.Pinned.Cultures.TryGetValue(pair, out CultureSnapshot? snapshot))
            {
                snapshot = ReadHost(pair, culture);
            }

            snapshot.Apply(culture);

            // The Latin-digit culture's own answers, which the base culture of the pair does not give.
            culture.NumberFormat.NativeDigits = Digits;
            culture.NumberFormat.DigitSubstitution = DigitShapes.None;

            return CultureInfo.ReadOnly(culture);
        }

        /// <summary>The host's formats of a pair without pinned data, cleaned.</summary>
        /// <param name="pair">The language and region.</param>
        /// <param name="fallback">The culture to read when the host has no Latin-digit culture of the pair.</param>
        /// <returns>The snapshot.</returns>
        private static CultureSnapshot ReadHost(string pair, CultureInfo fallback)
        {
            try
            {
                return CultureSnapshot.Read(CreateSource(pair));
            }
            catch (CultureNotFoundException)
            {
                return CultureSnapshot.Read(fallback);
            }
        }

        /// <summary>A new culture of a pair; of its language alone, or the invariant culture, when the host knows neither.</summary>
        /// <param name="pair">The language and region.</param>
        /// <returns>A new culture, not read-only.</returns>
        private static CultureInfo New(string pair)
        {
            // Negotiation never throws, and a language a distribution added may be one the host's
            // ICU does not know under predefined-cultures-only mode.
            try
            {
                return new CultureInfo(pair, useUserOverride: false);
            }
            catch (CultureNotFoundException)
            {
            }

            try
            {
                return new CultureInfo(pair[..pair.IndexOf('-', StringComparison.Ordinal)], useUserOverride: false);
            }
            catch (CultureNotFoundException)
            {
                return (CultureInfo)CultureInfo.InvariantCulture.Clone();
            }
        }

        /// <summary>Puts a culture on its Gregorian calendar, where it offers one.</summary>
        /// <param name="culture">The culture, not read-only.</param>
        private static void ForceGregorian(CultureInfo culture)
        {
            if (culture.DateTimeFormat.Calendar is GregorianCalendar)
            {
                return;
            }

            // ar-SA's own calendar is Umm al-Qura before ICU 78; the localized Gregorian calendar is
            // the one whose names and patterns are the language's.
            Calendar? gregorian = culture.OptionalCalendars.OfType<GregorianCalendar>()
                .OrderBy(static calendar => calendar.CalendarType == GregorianCalendarTypes.Localized ? 0 : 1)
                .FirstOrDefault();
            if (gregorian is not null)
            {
                culture.DateTimeFormat.Calendar = gregorian;
            }
        }
    }
}
