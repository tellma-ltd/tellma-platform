// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Checks
{
    /// <summary>
    ///     <c>core.calendars</c>: every composed calendar system has a well-formed code, no two share
    ///     one, and the platform's three calendars are composed under the codes the query language
    ///     reserves for them.
    /// </summary>
    /// <remarks>
    ///     Reads the composition alone. The calendar registry keeps the first system of a code and
    ///     drops the rest, so that this check, and not a failing constructor, is where a clash
    ///     surfaces.
    /// </remarks>
    internal sealed class CalendarsCheck : IStartupCheck
    {
        /// <summary>
        ///     The codes the platform's own calendars carry. They are the strings the query language
        ///     reserves for its calendar argument, so one code names one calendar in a tenant's
        ///     settings, in a request header and in a query.
        /// </summary>
        private static readonly string[] Required = [CalendarCodes.Gregorian, CalendarCodes.UmAlQura, CalendarCodes.Ethiopian];

        /// <inheritdoc />
        public string Name => CoreCheckNames.Calendars;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <summary>Whether a code is one to sixteen lower-case ASCII letters and digits, starting with a letter.</summary>
        /// <param name="code">The code.</param>
        /// <returns>True when it is well formed.</returns>
        public static bool IsWellFormed(string? code)
        {
            if (string.IsNullOrEmpty(code) || code.Length > CalendarCodes.MaxLength || !char.IsAsciiLetterLower(code[0]))
            {
                return false;
            }

            foreach (char c in code)
            {
                if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);

            List<CompositionProblem> problems = [];
            List<ICalendarSystem> systems = [.. services.GetServices<ICalendarSystem>()];

            // --- The form of each code ---
            foreach (ICalendarSystem system in systems)
            {
                if (!IsWellFormed(system.Code))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The calendar system {TypeDisplay.Qualified(system.GetType())} has the code '{system.Code}', which is not 1 to {CalendarCodes.MaxLength} lower-case ASCII letters and digits starting with a letter.",
                        "Give the calendar system a short lower-case code, such as 'gc' or 'persian'."));
                }
            }

            // --- One system per code, without regard to case ---
            foreach (IGrouping<string, ICalendarSystem> clash in systems
                .Where(static system => !string.IsNullOrEmpty(system.Code))
                .GroupBy(static system => system.Code, StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Count() > 1))
            {
                string types = string.Join(" and ", clash.Select(static system => TypeDisplay.Qualified(system.GetType())));
                problems.Add(new CompositionProblem(
                    Name,
                    $"The calendar systems {types} share the code '{clash.Key}'.",
                    "Give each calendar system a code of its own, or contribute only one of them."));
            }

            // --- The platform's three ---
            foreach (string code in Required)
            {
                if (!systems.Exists(system => string.Equals(system.Code, code, StringComparison.Ordinal)))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"No composed calendar system has the code '{code}', one of the three the platform and its query language rely on.",
                        "Leave the platform's calendar registrations in place: do not remove or replace the ICalendarSystem services Tellma registers."));
                }
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }
    }
}
