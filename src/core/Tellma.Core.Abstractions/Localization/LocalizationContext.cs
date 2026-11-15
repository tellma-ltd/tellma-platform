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
    ///     formatted in, the calendar and zone their dates render in, the tenant's content languages,
    ///     and which of them names records for them. The request context copies it; code rendering
    ///     text for someone other than the caller negotiates one for that person.
    /// </summary>
    /// <remarks>
    ///     The content languages are the tenant's, not the reader's: they label the columns of a
    ///     multilingual group (<c>Name (E)</c>, <c>Name (ع)</c>) in whatever language the reader
    ///     reads, so a label rendered on this flow names each column by the language it holds.
    /// </remarks>
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
    /// <param name="ContentLanguages">
    ///     The tenant's content languages, one to three in position order; empty outside a tenant.
    /// </param>
    public sealed record LocalizationContext(
        CultureInfo Culture,
        ICalendarSystem Calendar,
        TimeZoneInfo TimeZone,
        int ContentLanguageIndex,
        IReadOnlyList<LanguageInfo> ContentLanguages);
}
