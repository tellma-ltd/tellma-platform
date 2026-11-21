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
    ///     <para>
    ///         A compile-time selector, not a value: it chooses which arithmetic is emitted, and the
    ///         author's text never reaches the backend as SQL.
    ///     </para>
    ///     <para>
    ///         Two kinds. An algorithmic calendar is inline date arithmetic and needs nothing from
    ///         the host. A table calendar reads its months from a table the host names through the
    ///         schema, and is accepted only by a schema that names one.
    ///     </para>
    /// </remarks>
    internal static class CalendarCodes
    {
        /// <summary>The Gregorian calendar, and the default.</summary>
        internal const string Gregorian = "gc";

        /// <summary>The Ethiopian calendar, Amete Mihret, computed inline.</summary>
        internal const string Ethiopian = "et";

        /// <summary>The Umm al-Qura calendar, read from the host's month table.</summary>
        internal const string UmAlQura = "uq";

        /// <summary>Every code a calendar argument can name, whatever the schema.</summary>
        /// <remarks>
        ///     A table calendar is in this list too, so that the argument's restriction stays one
        ///     fixed set; whether the schema maps it is checked beside it, and a code the schema
        ///     does not map is refused exactly as an unknown one is.
        /// </remarks>
        internal static string[] Known => [Gregorian, Ethiopian, UmAlQura];

        /// <summary>The calendars whose months come from a host table.</summary>
        internal static string[] Tabular => [UmAlQura];

        /// <summary>Whether a code names a calendar whose months come from a host table.</summary>
        /// <param name="code">The code, as written.</param>
        /// <returns>True for a table calendar.</returns>
        internal static bool IsTabular(string? code)
        {
            return code is not null && string.Equals(code, UmAlQura, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The codes a calendar argument accepts against one schema.</summary>
        /// <param name="schema">The schema, or null where there is none.</param>
        /// <returns>The algorithmic codes, and every table calendar the schema maps.</returns>
        internal static IEnumerable<string> AcceptedBy(QueryexSchema? schema)
        {
            return Known.Where(code => !IsTabular(code) || schema?.CalendarSource(code) is not null);
        }
    }
}
