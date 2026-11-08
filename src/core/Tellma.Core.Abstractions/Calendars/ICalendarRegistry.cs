// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Calendars
{
    /// <summary>
    ///     The calendar systems composed into the distribution, by code: the platform's three
    ///     (<see cref="CalendarCodes" />) first, then any a distribution or pack added, in
    ///     composition order.
    /// </summary>
    /// <remarks>Codes are compared ordinally, without regard to case.</remarks>
    public interface ICalendarRegistry
    {
        /// <summary>The codes of every composed system, in composition order.</summary>
        IReadOnlyList<string> Codes { get; }

        /// <summary>The system with a code.</summary>
        /// <param name="code">The code.</param>
        /// <returns>The system.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="code" /> is null.</exception>
        /// <exception cref="KeyNotFoundException">No composed system has the code.</exception>
        ICalendarSystem this[string code] { get; }

        /// <summary>The system with a code, if one is composed.</summary>
        /// <param name="code">The code.</param>
        /// <returns>The system, or null when none has the code.</returns>
        ICalendarSystem? TryGet(string code);
    }
}
