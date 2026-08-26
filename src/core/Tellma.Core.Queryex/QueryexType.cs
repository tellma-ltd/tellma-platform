// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     The static type of an expression.
    /// </summary>
    /// <remarks>
    ///     Members carry a <c>Qx</c> prefix throughout because several of the names the language
    ///     uses are also platform type names, which the repository's analyzers reject; prefixing
    ///     every member rather than only the colliding ones keeps the set readable as one.
    /// </remarks>
    public enum QueryexType
    {
        /// <summary>
        ///     True or false. The one boolean type: storable, selectable, and comparable. Whether a
        ///     given occurrence is realised in SQL as a predicate or as a value is decided during
        ///     emission and is invisible in the language.
        /// </summary>
        QxBool,

        /// <summary>Exact decimal, at most 38 significant digits. Never binary floating point.</summary>
        QxNumeric,

        /// <summary>Unicode text.</summary>
        QxString,

        /// <summary>A globally unique identifier.</summary>
        QxGuid,

        /// <summary>A calendar date.</summary>
        QxDate,

        /// <summary>A date and time without an offset.</summary>
        QxDateTime,

        /// <summary>An instant with a UTC offset.</summary>
        QxDateTimeOffset,

        /// <summary>A node in a hierarchy. Has no literal form and no parameter form.</summary>
        QxHierarchyId,

        /// <summary>A spatial value. Has no literal form and no parameter form.</summary>
        QxGeography,
    }
}
