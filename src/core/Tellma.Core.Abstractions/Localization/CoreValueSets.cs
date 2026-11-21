// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>The names of the value sets the platform registers in every composition.</summary>
    public static class CoreValueSets
    {
        /// <summary>
        ///     Every language the platform can describe, offered or not, each labelled with its own
        ///     name (<c>ar</c> is <c>العربية</c> in every interface language).
        /// </summary>
        public const string Language = "core.Language";

        /// <summary>The composed calendar systems' codes, each labelled by the key <c>Calendar_</c> and the code.</summary>
        public const string Calendar = "core.Calendar";

        /// <summary>
        ///     The time-zone ids the query language compiles, each labelled with itself. Its aliases
        ///     are the older ids a browser reports for some of them.
        /// </summary>
        public const string TimeZone = "core.TimeZone";

        /// <summary>The regions of every language the platform can describe, each labelled by the key <c>Region_</c> and the code.</summary>
        public const string Region = "core.Region";
    }
}
