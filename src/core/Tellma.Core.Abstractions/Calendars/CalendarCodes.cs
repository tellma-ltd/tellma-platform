// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Calendars
{
    /// <summary>
    ///     The codes of the three calendar systems the platform ships. They are the same strings the
    ///     query language reserves for its calendar argument, so a code stored in a tenant's settings,
    ///     sent in a request header or written in a query names one calendar everywhere.
    /// </summary>
    /// <remarks>
    ///     A code is lower-case ASCII of at most 16 characters and is compared without regard to
    ///     case. A distribution that registers a further calendar system gives it a code of the same
    ///     form.
    /// </remarks>
    public static class CalendarCodes
    {
        /// <summary>The Gregorian calendar.</summary>
        public const string Gregorian = "gc";

        /// <summary>The Umm al-Qura calendar, the tabulated Islamic calendar of Saudi Arabia.</summary>
        public const string UmAlQura = "uq";

        /// <summary>The Ethiopian calendar: thirteen months, a single era.</summary>
        public const string Ethiopian = "et";

        /// <summary>The longest code a calendar system may have.</summary>
        public const int MaxLength = 16;
    }
}
