// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Abstractions.Calendars;

namespace Tellma.Core.Calendars
{
    /// <summary>
    ///     The calendar systems of the composition, by code, in composition order. The platform
    ///     registers it as a singleton over every <see cref="ICalendarSystem" /> its features
    ///     contributed.
    /// </summary>
    /// <remarks>
    ///     When two systems share a code (compared without regard to case), the first composed wins
    ///     and the other is unreachable here; the startup gate reports the clash, so it never reaches
    ///     a running host. A system with a null or empty code is skipped for the same reason.
    /// </remarks>
    public sealed class CalendarRegistry : ICalendarRegistry
    {
        /// <summary>The systems by code, without regard to case.</summary>
        private readonly FrozenDictionary<string, ICalendarSystem> _byCode;

        /// <summary>Creates the registry.</summary>
        /// <param name="systems">The composed systems, in composition order.</param>
        /// <exception cref="ArgumentNullException"><paramref name="systems" /> is null.</exception>
        public CalendarRegistry(IEnumerable<ICalendarSystem> systems)
        {
            ArgumentNullException.ThrowIfNull(systems);

            // --- First system per code wins; order is composition order ---
            Dictionary<string, ICalendarSystem> byCode = new(StringComparer.OrdinalIgnoreCase);
            List<string> codes = [];
            foreach (ICalendarSystem system in systems)
            {
                if (system is null || string.IsNullOrEmpty(system.Code))
                {
                    continue;
                }

                if (byCode.TryAdd(system.Code, system))
                {
                    codes.Add(system.Code);
                }
            }

            _byCode = byCode.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
            Codes = codes.AsReadOnly();
        }

        /// <inheritdoc />
        public IReadOnlyList<string> Codes { get; }

        /// <inheritdoc />
        public ICalendarSystem this[string code]
        {
            get
            {
                ArgumentNullException.ThrowIfNull(code);
                return _byCode.TryGetValue(code, out ICalendarSystem? system)
                    ? system
                    : throw new KeyNotFoundException($"No calendar system with the code '{code}' is composed.");
            }
        }

        /// <inheritdoc />
        public ICalendarSystem? TryGet(string code)
        {
            return code is not null && _byCode.TryGetValue(code, out ICalendarSystem? system) ? system : null;
        }
    }
}
