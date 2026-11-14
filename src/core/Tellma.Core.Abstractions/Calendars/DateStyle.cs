// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;

namespace Tellma.Core.Abstractions.Calendars
{
    /// <summary>The four renderings of a date that every calendar system offers.</summary>
    [SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "Short and Long are the established names of these date styles in every formatting library; no consumer reads them as the integer types.")]
    public enum DateStyle
    {
        /// <summary>Numeric, in the culture's order of day, month and year and with its separator: <c>15/03/2026</c>.</summary>
        Short,

        /// <summary>The day, the abbreviated month name and the year: <c>15 Mar 2026</c>.</summary>
        Medium,

        /// <summary>The day, the full month name and the year: <c>15 March 2026</c>.</summary>
        Long,

        /// <summary>The long form with the culture's weekday name: <c>Sunday, 15 March 2026</c>.</summary>
        Full,
    }
}
