// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex.Time;

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     The time-zone ids the engine compiles, for a host's list of zones.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The same table the zone argument of <c>local</c> is resolved against, so a zone a user
    ///         picks from a list built here always compiles and no second list can drift from it.
    ///     </para>
    ///     <para>
    ///         A host offers <see cref="Ids" /> as the choices, and maps a reported id through
    ///         <see cref="Aliases" /> before comparing or storing it: a browser reports the older
    ///         spelling of some renamed zones, and the alias is what lets that spelling find the
    ///         current id a stored setting holds.
    ///     </para>
    /// </remarks>
    public static class QueryexTimeZones
    {
        /// <summary>
        ///     The current IANA time-zone ids, in ordinal order.
        /// </summary>
        /// <remarks>
        ///     An id is current when the time-zone database lists it as a location in its country
        ///     table, or when it is <c>UTC</c>. Each location therefore appears once, under the id a
        ///     user would recognise, and each compiles.
        /// </remarks>
        public static IReadOnlyList<string> Ids { get; } = CldrTimeZones.Ids;

        /// <summary>
        ///     The older ids a supported browser reports in place of a current one, each mapped to
        ///     the current id it stands for. Keys match ignoring case.
        /// </summary>
        /// <remarks>
        ///     Every value is one of <see cref="Ids" />, and no key is. An alias compiles too,
        ///     resolving to its current id's zone, but a list of choices never shows one.
        /// </remarks>
        public static IReadOnlyDictionary<string, string> Aliases { get; } = CldrTimeZones.Aliases;
    }
}
