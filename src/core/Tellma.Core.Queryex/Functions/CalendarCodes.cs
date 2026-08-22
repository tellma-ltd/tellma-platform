// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex.Functions
{
    /// <summary>
    ///     The calendar a calendar operation counts in.
    /// </summary>
    /// <remarks>
    ///     A compile-time selector, not a value: it chooses which arithmetic is emitted, and the
    ///     author's text never reaches the backend. This version implements the Gregorian calendar
    ///     only; the other two names are reserved so that the language surface is already known and
    ///     will not have to change when they arrive.
    /// </remarks>
    internal static class CalendarCodes
    {
        /// <summary>The Gregorian calendar, and the default.</summary>
        internal const string Gregorian = "gc";

        /// <summary>
        ///     Every code this version accepts. The reserved names — <c>uq</c> for Umm al-Qura and
        ///     <c>et</c> for the Ethiopian calendar — are deliberately absent, so writing one today
        ///     is rejected the same way any unknown code is, rather than compiling to the wrong
        ///     arithmetic.
        /// </summary>
        internal static string[] Accepted => [Gregorian];
    }
}
