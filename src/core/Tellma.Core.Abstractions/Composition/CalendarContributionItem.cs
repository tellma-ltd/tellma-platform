// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     A calendar system a feature contributes: a concrete class implementing
    ///     <see cref="Calendars.ICalendarSystem" /> with a public parameterless constructor. The
    ///     platform creates one instance, a singleton, and composes it into the calendar registry
    ///     after the platform's own three systems.
    /// </summary>
    /// <param name="CalendarType">The calendar system class.</param>
    public sealed record CalendarContributionItem(Type CalendarType) : FeatureContributionItem;
}
