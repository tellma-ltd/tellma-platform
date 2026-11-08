// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Core.Tests.Calendars
{
    /// <summary>
    ///     en-GB, ar-SA and am-ET clones whose Gregorian date formats are fixed to the values .NET 10
    ///     reads from ICU 74 (Ubuntu 24.04), so the golden strings do not move with the ICU release of
    ///     the host running the tests: Windows' ICU differs (en-GB's full date has a comma there, and
    ///     am-ET's Gregorian month names are spelled differently).
    /// </summary>
    /// <remarks>
    ///     The pinned values keep the raw data's quirks (Arabic right-to-left marks in the patterns
    ///     and the separator, the era the Arabic patterns end with), which the calendar systems must
    ///     clean. Each clone's calendar is Gregorian, as a negotiated request culture's is.
    /// </remarks>
    internal static class PinnedCultures
    {
        /// <summary>English (United Kingdom).</summary>
        public static CultureInfo EnGb { get; } = Pin(
            "en-GB",
            shortDate: "dd/MM/yyyy",
            longDate: "dddd d MMMM yyyy",
            dateSeparator: "/",
            shortTime: "HH:mm",
            am: "am",
            pm: "pm",
            months: ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"],
            abbreviatedMonths: ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sept", "Oct", "Nov", "Dec"],
            days: ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"]);

        /// <summary>Arabic (Saudi Arabia), switched to its Gregorian calendar.</summary>
        public static CultureInfo ArSa { get; } = Pin(
            "ar-SA",
            shortDate: "d‏/M‏/yyyy g",
            longDate: "dddd، d MMMM yyyy g",
            dateSeparator: "‏/",
            shortTime: "h:mm tt",
            am: "ص",
            pm: "م",
            months: ["يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"],
            abbreviatedMonths: ["يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"],
            days: ["الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت"]);

        /// <summary>Amharic (Ethiopia).</summary>
        public static CultureInfo AmEt { get; } = Pin(
            "am-ET",
            shortDate: "dd/MM/yyyy",
            longDate: "yyyy MMMM d, dddd",
            dateSeparator: "/",
            shortTime: "h:mm tt",
            am: "ጥዋት",
            pm: "ከሰዓት",
            months: ["ጃንዋሪ", "ፌብሩዋሪ", "ማርች", "ኤፕሪል", "ሜይ", "ጁን", "ጁላይ", "ኦገስት", "ሴፕቴምበር", "ኦክቶበር", "ኖቬምበር", "ዲሴምበር"],
            abbreviatedMonths: ["ጃን", "ፌብ", "ማርች", "ኤፕሪ", "ሜይ", "ጁን", "ጁላይ", "ኦገስ", "ሴፕቴ", "ኦክቶ", "ኖቬም", "ዲሴም"],
            days: ["እሑድ", "ሰኞ", "ማክሰኞ", "ረቡዕ", "ሐሙስ", "ዓርብ", "ቅዳሜ"]);

        /// <summary>Clones a culture with its Gregorian date formats fixed.</summary>
        /// <param name="name">The culture.</param>
        /// <param name="shortDate">The short date pattern.</param>
        /// <param name="longDate">The long date pattern.</param>
        /// <param name="dateSeparator">The date separator.</param>
        /// <param name="shortTime">The short time pattern.</param>
        /// <param name="am">The morning designator.</param>
        /// <param name="pm">The afternoon designator.</param>
        /// <param name="months">The twelve month names.</param>
        /// <param name="abbreviatedMonths">The twelve abbreviated month names.</param>
        /// <param name="days">The seven weekday names, Sunday first.</param>
        /// <returns>The pinned, read-only clone.</returns>
        private static CultureInfo Pin(
            string name,
            string shortDate,
            string longDate,
            string dateSeparator,
            string shortTime,
            string am,
            string pm,
            string[] months,
            string[] abbreviatedMonths,
            string[] days)
        {
            var culture = (CultureInfo)CultureInfo.GetCultureInfo(name).Clone();
            DateTimeFormatInfo format = culture.DateTimeFormat;

            // Switching the calendar reloads the culture's data, so it goes first.
            format.Calendar = culture.OptionalCalendars.OfType<GregorianCalendar>().First();
            format.ShortDatePattern = shortDate;
            format.LongDatePattern = longDate;
            format.DateSeparator = dateSeparator;
            format.ShortTimePattern = shortTime;
            format.AMDesignator = am;
            format.PMDesignator = pm;
            format.MonthNames = [.. months, string.Empty];
            format.MonthGenitiveNames = [.. months, string.Empty];
            format.AbbreviatedMonthNames = [.. abbreviatedMonths, string.Empty];
            format.AbbreviatedMonthGenitiveNames = [.. abbreviatedMonths, string.Empty];
            format.DayNames = days;
            return CultureInfo.ReadOnly(culture);
        }
    }
}
