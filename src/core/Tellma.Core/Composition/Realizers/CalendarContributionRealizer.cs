// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Realizers
{
    /// <summary>
    ///     Registers <see cref="CalendarContributionItem" />s: each calendar system class once, as a
    ///     singleton <see cref="ICalendarSystem" />, in realisation order, which is the order the
    ///     calendar registry lists their codes in. The same class contributed twice is registered
    ///     once, for the feature realised first.
    /// </summary>
    internal sealed class CalendarContributionRealizer : IContributionRealizer<CalendarContributionItem>
    {
        /// <inheritdoc />
        public void Realize(CalendarContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            Type? calendarType = item.CalendarType;
            if (calendarType is null
                || !calendarType.IsClass
                || calendarType.IsAbstract
                || calendarType.ContainsGenericParameters
                || !typeof(ICalendarSystem).IsAssignableFrom(calendarType)
                || calendarType.GetConstructor(Type.EmptyTypes) is null)
            {
                string described = calendarType is null ? "no calendar type" : TypeDisplay.Qualified(calendarType);
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' contributes the calendar system {described}, which is not a concrete, closed class implementing {nameof(ICalendarSystem)} with a public parameterless constructor.",
                    "Contribute a calendar system as a concrete class implementing ICalendarSystem with a public parameterless constructor: contribution.Calendar<TCalendar>()."));
                return;
            }

            if (!context.Record.Calendars.Exists(existing => existing.Type == calendarType))
            {
                context.Record.Calendars.Add(new ContributedType(calendarType, owner));
                context.Services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(ICalendarSystem), calendarType));
            }
        }
    }
}
