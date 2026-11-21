// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Collections.ObjectModel;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Queryex;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     The value sets of the composition: the platform's four (<see cref="CoreValueSets" />)
    ///     first, then every set a feature contributed, in composition order. The platform registers
    ///     it as a singleton.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A set is fixed per deployment and the same for every tenant. The language and region
    ///         sets hold every value the platform can describe, not only what the distribution
    ///         offers, so a stored value that is no longer offered still has its label.
    ///     </para>
    ///     <para>
    ///         When two sets share a name, the first composed wins and the other is unreachable
    ///         here; a contributed set without a name or without values is skipped. The startup gate
    ///         reports both, so neither reaches a running host.
    ///     </para>
    /// </remarks>
    public sealed class ValueSetRegistry : IValueSetRegistry
    {
        /// <summary>The sets by name.</summary>
        private readonly FrozenDictionary<string, ValueSetDescriptor> _byName;

        /// <summary>Creates the registry.</summary>
        /// <param name="languages">The language catalogue, whose languages and regions make two of the platform's sets.</param>
        /// <param name="calendars">The calendar registry, whose codes make the calendar set.</param>
        /// <param name="contributed">The sets the features contributed, in composition order.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public ValueSetRegistry(ILanguageCatalog languages, ICalendarRegistry calendars, IEnumerable<ValueSetDescriptor> contributed)
        {
            ArgumentNullException.ThrowIfNull(languages);
            ArgumentNullException.ThrowIfNull(calendars);
            ArgumentNullException.ThrowIfNull(contributed);

            List<ValueSetDescriptor> all = [.. CoreSets(languages, calendars)];

            // --- The contributed sets, each copied so that no later change to it reaches a reader ---
            foreach (ValueSetDescriptor descriptor in contributed)
            {
                if (descriptor is null || string.IsNullOrEmpty(descriptor.Name) || descriptor.Values is null)
                {
                    continue;
                }

                all.Add(new ValueSetDescriptor(
                    descriptor.Name,
                    Array.AsReadOnly([.. descriptor.Values]),
                    descriptor.Aliases is null ? null : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(descriptor.Aliases, StringComparer.Ordinal))));
            }

            // --- First set per name wins; order is composition order ---
            Dictionary<string, ValueSetDescriptor> byName = new(StringComparer.Ordinal);
            List<ValueSetDescriptor> kept = [];
            foreach (ValueSetDescriptor descriptor in all)
            {
                if (byName.TryAdd(descriptor.Name, descriptor))
                {
                    kept.Add(descriptor);
                }
            }

            _byName = byName.ToFrozenDictionary(StringComparer.Ordinal);
            All = kept.AsReadOnly();
        }

        /// <inheritdoc />
        public IReadOnlyList<ValueSetDescriptor> All { get; }

        /// <inheritdoc />
        public ValueSetDescriptor? Find(string name)
        {
            return name is not null && _byName.TryGetValue(name, out ValueSetDescriptor? descriptor) ? descriptor : null;
        }

        /// <summary>The key of a calendar's label.</summary>
        /// <param name="code">The calendar's code.</param>
        /// <returns>The resource key.</returns>
        internal static string CalendarLabelKey(string code)
        {
            return "Calendar_" + code;
        }

        /// <summary>The key of a region's label.</summary>
        /// <param name="code">The region's code.</param>
        /// <returns>The resource key.</returns>
        internal static string RegionLabelKey(string code)
        {
            return "Region_" + code;
        }

        /// <summary>The platform's four sets.</summary>
        /// <param name="languages">The language catalogue.</param>
        /// <param name="calendars">The calendar registry.</param>
        /// <returns>The language, calendar, time-zone and region sets, in that order.</returns>
        internal static ValueSetDescriptor[] CoreSets(ILanguageCatalog languages, ICalendarRegistry calendars)
        {
            // --- Languages: every described language, labelled by its own name in every UI language ---
            ValueSetEntry[] languageValues = [.. languages.All.Select(static language => new ValueSetEntry(language.Code, null, language.NativeName))];

            // --- Calendars: the composed systems, labelled by key ---
            ValueSetEntry[] calendarValues = [.. calendars.Codes.Select(static code => new ValueSetEntry(code, CalendarLabelKey(code), null))];

            // --- Time zones: the query language's own table, so every zone a user can pick compiles ---
            // The id is the label: an offset would change with daylight saving.
            ValueSetEntry[] zoneValues = [.. QueryexTimeZones.Ids.Select(static id => new ValueSetEntry(id, null, id))];

            // --- Regions: the union over every described language, labelled by key ---
            ValueSetEntry[] regionValues =
            [
                .. languages.All
                    .SelectMany(language => languages.Formats(language.Code).Regions)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .Select(static region => new ValueSetEntry(region, RegionLabelKey(region), null)),
            ];

            return
            [
                new ValueSetDescriptor(CoreValueSets.Language, Array.AsReadOnly(languageValues)),
                new ValueSetDescriptor(CoreValueSets.Calendar, Array.AsReadOnly(calendarValues)),
                new ValueSetDescriptor(CoreValueSets.TimeZone, Array.AsReadOnly(zoneValues), QueryexTimeZones.Aliases),
                new ValueSetDescriptor(CoreValueSets.Region, Array.AsReadOnly(regionValues)),
            ];
        }
    }
}
