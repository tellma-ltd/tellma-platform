// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Calendars;
using Tellma.Core.Localization;
using Tellma.Core.Queryex;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>The value-set registry: the platform's four sets, their values, order and labels, and the sets features add.</summary>
    public class ValueSetRegistryTests
    {
        private static readonly AddedLanguage Hausa = new(
            new LanguageInfo("ha", "Hausa", "Ha", false), new LanguageFormats("ha", "NG", ["NG", "NE", "XK"], null));

        [Fact]
        public void The_platforms_four_sets_come_first_in_order()
        {
            ValueSetRegistry registry = Registry(contributed: [new ValueSetDescriptor("gl.AccountClass", [new ValueSetEntry("Asset", "AccountClass_Asset", null)])]);

            Assert.Equal(["core.Language", "core.Calendar", "core.TimeZone", "core.Region", "gl.AccountClass"], registry.All.Select(static set => set.Name));
            Assert.Equal(
                [CoreValueSets.Language, CoreValueSets.Calendar, CoreValueSets.TimeZone, CoreValueSets.Region],
                registry.All.Take(4).Select(static set => set.Name));
        }

        [Fact]
        public void The_language_set_holds_every_described_language_labelled_by_its_own_name()
        {
            // Wider than the offer: Acme offers two languages, and the set holds the whole catalogue
            // and the added language, so a stored language that is no longer offered keeps its label.
            ValueSetDescriptor languages = Registry(new LanguageDeclaration(["en-GB", "ar-SA"], [Hausa])).Find(CoreValueSets.Language)!;

            Assert.Equal(
                ["en", "ar", "am", "om", "ti", "so", "sw", "fr", "es", "pt", "de", "tr", "ur", "hi", "zh", "ha"],
                languages.Values.Select(static entry => entry.Value));
            Assert.Equal(new ValueSetEntry("ar", null, "العربية"), languages.Values[1]);
            Assert.Equal(new ValueSetEntry("ha", null, "Hausa"), languages.Values[^1]);
            Assert.All(languages.Values, static entry => Assert.Null(entry.LabelKey));
            Assert.Null(languages.Aliases);
        }

        [Fact]
        public void The_calendar_set_holds_the_composed_codes_labelled_by_key()
        {
            ValueSetDescriptor calendars = Registry().Find(CoreValueSets.Calendar)!;

            Assert.Equal(
                [new ValueSetEntry("gc", "Calendar_gc", null), new ValueSetEntry("uq", "Calendar_uq", null), new ValueSetEntry("et", "Calendar_et", null)],
                calendars.Values);
            Assert.Null(calendars.Aliases);
        }

        [Fact]
        public void The_time_zone_set_is_the_query_languages_table_labelled_by_id()
        {
            ValueSetDescriptor zones = Registry().Find(CoreValueSets.TimeZone)!;

            // One zone list: every zone a user can pick compiles in a query.
            Assert.Equal(QueryexTimeZones.Ids, zones.Values.Select(static entry => entry.Value));
            Assert.Equal(zones.Values.Select(static entry => entry.Value).Order(StringComparer.Ordinal), zones.Values.Select(static entry => entry.Value));
            Assert.All(zones.Values, static entry =>
            {
                Assert.Null(entry.LabelKey);
                Assert.Equal(entry.Value, entry.Label);
            });
            Assert.Contains(zones.Values, static entry => entry.Value == "Asia/Kolkata");
        }

        [Fact]
        public void The_time_zone_sets_aliases_are_the_query_languages_and_none_is_a_value()
        {
            ValueSetDescriptor zones = Registry().Find(CoreValueSets.TimeZone)!;
            HashSet<string> values = new(zones.Values.Select(static entry => entry.Value), StringComparer.OrdinalIgnoreCase);

            Assert.Same(QueryexTimeZones.Aliases, zones.Aliases);
            Assert.Equal("Asia/Kolkata", zones.Aliases!["Asia/Calcutta"]);
            Assert.All(zones.Aliases, alias =>
            {
                Assert.DoesNotContain(alias.Key, values);
                Assert.Contains(alias.Value, values);
            });
        }

        [Fact]
        public void The_region_set_is_the_ordinal_union_of_every_described_languages_regions_labelled_by_key()
        {
            LanguageDeclaration declaration = new(["en"], [Hausa]);
            LanguageCatalog catalog = new(declaration);
            ValueSetDescriptor regions = Registry(declaration).Find(CoreValueSets.Region)!;

            string[] expected =
            [
                .. catalog.All.SelectMany(language => catalog.Formats(language.Code).Regions).Distinct().Order(StringComparer.Ordinal),
            ];
            Assert.Equal(expected, regions.Values.Select(static entry => entry.Value));
            Assert.All(regions.Values, static entry => Assert.Equal(new ValueSetEntry(entry.Value, "Region_" + entry.Value, null), entry));

            // Regions of languages the distribution does not offer, and the added language's own.
            Assert.Contains(regions.Values, static entry => entry.Value == "FR");
            Assert.Contains(regions.Values, static entry => entry.Value == "XK");
            Assert.Equal("AE", regions.Values[0].Value);
        }

        [Fact]
        public void A_set_is_found_by_its_exact_name()
        {
            ValueSetRegistry registry = Registry();

            Assert.NotNull(registry.Find("core.TimeZone"));
            Assert.Null(registry.Find("core.timezone"));
            Assert.Null(registry.Find("CORE.TIMEZONE"));
            Assert.Null(registry.Find("core.Nothing"));
            Assert.Null(registry.Find(null!));
        }

        [Fact]
        public void A_contributed_set_is_copied_so_later_changes_do_not_reach_readers()
        {
            List<ValueSetEntry> values = [new("A", null, "A")];
            Dictionary<string, string> aliases = new() { ["a1"] = "A" };
            ValueSetRegistry registry = Registry(contributed: [new ValueSetDescriptor("acme.Grade", values, aliases)]);

            values.Add(new ValueSetEntry("B", null, "B"));
            aliases["b1"] = "B";

            ValueSetDescriptor grade = registry.Find("acme.Grade")!;
            Assert.Equal([new ValueSetEntry("A", null, "A")], grade.Values);
            Assert.Equal(["a1"], grade.Aliases!.Keys);
        }

        [Fact]
        public void The_first_set_of_a_name_wins_and_a_set_without_a_name_or_values_is_skipped()
        {
            // The startup gate reports each of these; the registry still has to be constructible.
            ValueSetRegistry registry = Registry(contributed:
            [
                new ValueSetDescriptor("acme.Grade", [new ValueSetEntry("A", null, "A")]),
                new ValueSetDescriptor("acme.Grade", [new ValueSetEntry("B", null, "B")]),
                new ValueSetDescriptor("core.Calendar", [new ValueSetEntry("zz", null, "zz")]),
                new ValueSetDescriptor(null!, [new ValueSetEntry("C", null, "C")]),
                new ValueSetDescriptor("acme.Empty", null!),
                null!,
            ]);

            Assert.Equal(5, registry.All.Count);
            Assert.Equal("A", registry.Find("acme.Grade")!.Values[0].Value);
            Assert.Equal("gc", registry.Find("core.Calendar")!.Values[0].Value);
        }

        [Fact]
        public void Null_arguments_are_refused()
        {
            LanguageCatalog languages = new(LanguageDeclaration.None);
            CalendarRegistry calendars = new([new GregorianCalendarSystem()]);

            _ = Assert.Throws<ArgumentNullException>(() => new ValueSetRegistry(null!, calendars, []));
            _ = Assert.Throws<ArgumentNullException>(() => new ValueSetRegistry(languages, null!, []));
            _ = Assert.Throws<ArgumentNullException>(() => new ValueSetRegistry(languages, calendars, null!));
        }

        /// <summary>A registry over the platform's three calendars.</summary>
        /// <param name="declaration">The distribution's language declaration; none when null.</param>
        /// <param name="contributed">The contributed sets.</param>
        /// <returns>The registry.</returns>
        private static ValueSetRegistry Registry(LanguageDeclaration? declaration = null, ValueSetDescriptor[]? contributed = null)
        {
            return new ValueSetRegistry(
                new LanguageCatalog(declaration ?? LanguageDeclaration.None),
                new CalendarRegistry([new GregorianCalendarSystem(), new UmAlQuraCalendarSystem(), new EthiopianCalendarSystem()]),
                contributed ?? []);
        }
    }
}
