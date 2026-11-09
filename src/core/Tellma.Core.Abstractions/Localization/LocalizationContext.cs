// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Calendars;

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>
    ///     The outcome of negotiating one reader's locale: the culture their text is looked up and
    ///     formatted in, the calendar and zone their dates render in, and which of the tenant's
    ///     content languages names records for them. The request context copies it; code rendering
    ///     text for someone other than the caller negotiates one for that person.
    /// </summary>
    /// <param name="Culture">
    ///     The culture: the negotiated language and region (<c>ar-SA</c>), formatting numbers with
    ///     Latin digits and with no bidirectional marks in its patterns.
    /// </param>
    /// <param name="Calendar">The calendar dates render in.</param>
    /// <param name="TimeZone">
    ///     The display zone instants render in. It only formats; the tenant's own zone decides what
    ///     "today" is.
    /// </param>
    /// <param name="ContentLanguageIndex">
    ///     The position (1 to 3) of the tenant content language that matches the culture's language,
    ///     else 1: which of a record's <c>Name</c>, <c>Name2</c> and <c>Name3</c> names it for this
    ///     reader.
    /// </param>
    public sealed record LocalizationContext(
        CultureInfo Culture, ICalendarSystem Calendar, TimeZoneInfo TimeZone, int ContentLanguageIndex);
}
