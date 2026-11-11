// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Localization;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>
    ///     The language catalogue: the frozen entries and their invariants, and what a distribution's
    ///     declaration makes of them.
    /// </summary>
    /// <remarks>
    ///     The assertions against the host's cultures are sensitive to its ICU release, which is
    ///     the point: they run on Windows' ICU and on the Linux images' to show that the frozen
    ///     data holds on each.
    /// </remarks>
    public class LanguageCatalogTests
    {
        /// <summary>Acme's declaration: English with British formats first, Arabic with Saudi formats.</summary>
        private static readonly LanguageCatalog Acme = new(new LanguageDeclaration(["en-GB", "ar-SA"], []));

        /// <summary>Every catalogue pair, as theory data.</summary>
        public static TheoryData<string> Pairs => [.. CultureDataSource.CataloguePairs()];

        [Fact]
        public void The_catalogue_lists_the_fifteen_languages_in_order_with_their_symbols()
        {
            string[] expected =
            [
                "en E", "ar ع", "am አ", "om Om", "ti ት", "so So", "sw Sw", "fr F", "es Es", "pt P", "de D", "tr T", "ur ا", "hi हि", "zh 中",
            ];

            Assert.Equal(expected, new LanguageCatalog(LanguageDeclaration.None).All.Select(static language => $"{language.Code} {language.Symbol}"));
        }

        [Fact]
        public void Symbols_and_codes_are_unique()
        {
            IReadOnlyList<LanguageInfo> all = new LanguageCatalog(LanguageDeclaration.None).All;

            Assert.Equal(all.Count, all.Select(static language => language.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(all.Count, all.Select(static language => language.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void Every_entry_describes_itself_in_its_own_script_and_direction()
        {
            LanguageCatalog catalog = new(LanguageDeclaration.None);

            Assert.Equal(new LanguageInfo("ar", "العربية", "ع", true), catalog.Find("ar"));
            Assert.Equal(new LanguageInfo("en", "English", "E", false), catalog.Find("en"));
            Assert.Equal(["ar", "ur"], catalog.All.Where(static language => language.IsRightToLeft).Select(static language => language.Code));

            // The frozen direction agrees with what the host knows of the language.
            Assert.All(catalog.All, static language =>
                Assert.Equal(CultureInfo.GetCultureInfo(language.Code).TextInfo.IsRightToLeft, language.IsRightToLeft));
        }

        [Fact]
        public void Every_code_is_a_predefined_culture()
        {
            Assert.All(
                new LanguageCatalog(LanguageDeclaration.None).All,
                static language => Assert.Equal(language.Code, CultureInfo.GetCultureInfo(language.Code, predefinedOnly: true).Name));
        }

        [Fact]
        public void The_catalogue_holds_243_pairs()
        {
            LanguageCatalog catalog = new(LanguageDeclaration.None);
            var counts = catalog.All.ToDictionary(static language => language.Code, language => catalog.Formats(language.Code).Regions.Count);

            Assert.Equal(243, counts.Values.Sum());
            Assert.Equal(104, counts["en"]);
            Assert.Equal(26, counts["ar"]);
            Assert.Equal(["CN", "HK", "MO", "SG", "TW"], catalog.Formats("zh").Regions);
            Assert.DoesNotContain("SA", catalog.Formats("en").Regions);
        }

        [Theory]
        [MemberData(nameof(Pairs))]
        public void Every_pair_resolves_as_a_predefined_culture(string pair)
        {
            // The five Chinese pairs are alias names: the host resolves them to its own spelling
            // (zh-CN to zh-Hans-CN), which is still a predefined culture of the same language.
            var culture = CultureInfo.GetCultureInfo(pair, predefinedOnly: true);

            Assert.Equal(pair[..2], culture.TwoLetterISOLanguageName);
        }

        [Fact]
        public void Regions_are_two_upper_case_letters_distinct_and_in_ordinal_order()
        {
            LanguageCatalog catalog = new(LanguageDeclaration.None);

            Assert.All(catalog.All, language =>
            {
                IReadOnlyList<string> regions = catalog.Formats(language.Code).Regions;
                Assert.All(regions, static region => Assert.Matches("^[A-Z]{2}$", region));
                Assert.Equal(regions.Distinct().Order(StringComparer.Ordinal), regions);
            });
        }

        [Fact]
        public void Native_digits_are_frozen_for_arabic_urdu_and_hindi_only()
        {
            LanguageCatalog catalog = new(LanguageDeclaration.None);
            var digits = catalog.All
                .Where(language => catalog.Formats(language.Code).NativeDigits is not null)
                .ToDictionary(static language => language.Code, language => catalog.Formats(language.Code).NativeDigits);

            Assert.Equal(new Dictionary<string, string?> { ["ar"] = "arab", ["ur"] = "arabext", ["hi"] = "deva" }, digits);
        }

        [Fact]
        public void The_default_region_is_britain_for_english_and_the_likely_region_otherwise()
        {
            LanguageCatalog catalog = new(LanguageDeclaration.None);

            // CLDR's likely region of each language, frozen: a host's own answer differs by
            // platform and release (.NET itself answers SA for Arabic), so none is consulted.
            Dictionary<string, string> expected = new()
            {
                ["en"] = "GB",
                ["ar"] = "EG",
                ["am"] = "ET",
                ["om"] = "ET",
                ["ti"] = "ET",
                ["so"] = "SO",
                ["sw"] = "TZ",
                ["fr"] = "FR",
                ["es"] = "ES",
                ["pt"] = "BR",
                ["de"] = "DE",
                ["tr"] = "TR",
                ["ur"] = "PK",
                ["hi"] = "IN",
                ["zh"] = "CN",
            };

            Assert.Equal(expected, catalog.All.ToDictionary(static language => language.Code, language => catalog.Formats(language.Code).DefaultRegion));
            Assert.All(catalog.All, language =>
            {
                LanguageFormats formats = catalog.Formats(language.Code);
                Assert.Contains(formats.DefaultRegion, formats.Regions);
                Assert.Equal(language.Code, formats.Language);
            });
        }

        [Fact]
        public void A_distribution_that_declares_nothing_offers_english()
        {
            LanguageCatalog catalog = new(LanguageDeclaration.None);

            Assert.Equal(["en"], catalog.Offered.Select(static language => language.Code));
            Assert.True(catalog.IsOffered("en"));
            Assert.False(catalog.IsOffered("ar"));
            Assert.True(catalog.IsRegionOffered("GB"));
            Assert.False(catalog.IsRegionOffered("SA"));
        }

        [Fact]
        public void The_declared_languages_are_offered_in_order_and_the_first_is_the_default()
        {
            Assert.Equal(["en", "ar"], Acme.Offered.Select(static language => language.Code));
            Assert.Equal(["ar", "en"], new LanguageCatalog(new LanguageDeclaration(["ar", "en-US"], [])).Offered.Select(static language => language.Code));
        }

        [Fact]
        public void Only_the_bare_language_is_offered_and_codes_ignore_case()
        {
            Assert.True(Acme.IsOffered("ar"));
            Assert.True(Acme.IsOffered("AR"));
            Assert.False(Acme.IsOffered("ar-SA"));
            Assert.False(Acme.IsOffered("fr"));
            Assert.False(Acme.IsOffered(null!));
            Assert.Equal("ar", new LanguageCatalog(new LanguageDeclaration(["AR-sa"], [])).Offered[0].Code);
        }

        [Fact]
        public void Find_searches_the_whole_catalogue_offered_or_not()
        {
            Assert.Equal("Français", Acme.Find("fr")?.NativeName);
            Assert.Equal("Français", Acme.Find("FR")?.NativeName);
            Assert.Null(Acme.Find("xx"));
            Assert.Null(Acme.Find(null!));
        }

        [Fact]
        public void A_declared_region_becomes_the_default_region_of_its_language()
        {
            LanguageFormats arabic = Acme.Formats("ar");

            Assert.Equal("ar", arabic.Language);
            Assert.Equal("SA", arabic.DefaultRegion);
            Assert.Equal("arab", arabic.NativeDigits);
            Assert.Equal(26, arabic.Regions.Count);
            Assert.Equal("AE", arabic.Regions[0]);
            Assert.Equal("YE", arabic.Regions[^1]);
        }

        [Fact]
        public void A_language_without_a_declared_region_keeps_the_catalogue_default()
        {
            // Offered without a region, and not offered at all: both answer from the catalogue.
            Assert.Equal("EG", new LanguageCatalog(new LanguageDeclaration(["ar"], [])).Formats("ar").DefaultRegion);
            Assert.Equal("FR", Acme.Formats("fr").DefaultRegion);
            Assert.Equal("FR", Acme.Formats("FR").DefaultRegion);
        }

        [Fact]
        public void Formats_of_an_unknown_language_throws()
        {
            _ = Assert.Throws<KeyNotFoundException>(() => Acme.Formats("xx"));
            _ = Assert.Throws<KeyNotFoundException>(() => Acme.Formats("ar-SA"));
            _ = Assert.Throws<ArgumentNullException>(() => Acme.Formats(null!));
        }

        [Fact]
        public void A_region_is_offered_when_an_offered_language_has_it()
        {
            // AE is offered with ar and with en; FR only with languages Acme does not offer.
            Assert.True(Acme.IsRegionOffered("AE"));
            Assert.True(Acme.IsRegionOffered("SA"));
            Assert.True(Acme.IsRegionOffered("US"));
            Assert.False(Acme.IsRegionOffered("FR"));
            Assert.False(Acme.IsRegionOffered("ZZ"));
            Assert.False(Acme.IsRegionOffered(null!));
        }

        [Fact]
        public void An_added_language_answers_from_the_formats_it_was_given()
        {
            AddedLanguage hausa = new(new LanguageInfo("ha", "Hausa", "Ha", false), new LanguageFormats("ha", "NG", ["NG", "GH", "NE"], null));
            LanguageCatalog catalog = new(new LanguageDeclaration(["en", "ha-GH"], [hausa]));

            Assert.Equal(["en", "ha"], catalog.Offered.Select(static language => language.Code));
            Assert.Equal(hausa.Info, catalog.Find("ha"));
            Assert.Equal(hausa.Info, catalog.All[^1]);
            Assert.Equal(16, catalog.All.Count);

            // Its regions, held in ordinal order like the catalogue's own; the declared region is the default.
            LanguageFormats formats = catalog.Formats("ha");
            Assert.Equal(["GH", "NE", "NG"], formats.Regions);
            Assert.Equal("GH", formats.DefaultRegion);
            Assert.Null(formats.NativeDigits);
            Assert.True(catalog.IsRegionOffered("NE"));
        }

        [Fact]
        public void An_added_language_is_described_but_not_offered_until_declared()
        {
            AddedLanguage hausa = new(new LanguageInfo("ha", "Hausa", "Ha", false), new LanguageFormats("ha", "NG", ["NG"], null));
            LanguageCatalog catalog = new(new LanguageDeclaration(["en"], [hausa]));

            Assert.NotNull(catalog.Find("ha"));
            Assert.False(catalog.IsOffered("ha"));
            Assert.Equal("NG", catalog.Formats("ha").DefaultRegion);
            Assert.False(catalog.IsRegionOffered("NE"));
        }

        [Fact]
        public void A_declaration_with_mistakes_resolves_to_what_is_sound_in_it()
        {
            // An unknown language, a malformed code, a repeated language and a region the language
            // lacks: the catalogue is still built, and the startup gate reports each.
            LanguageCatalog catalog = new(new LanguageDeclaration(["xx", "e", "ar-SA", "ar-EG", "en-SA"], []));

            Assert.Equal(["ar", "en"], catalog.Offered.Select(static language => language.Code));
            Assert.Equal("SA", catalog.Formats("ar").DefaultRegion);
            Assert.Equal("GB", catalog.Formats("en").DefaultRegion);

            // Nothing sound at all falls back to English.
            Assert.Equal(["en"], new LanguageCatalog(new LanguageDeclaration(["xx"], [])).Offered.Select(static language => language.Code));
            Assert.Equal(["en"], new LanguageCatalog(new LanguageDeclaration([], [])).Offered.Select(static language => language.Code));
        }

        [Fact]
        public void A_declaration_is_copied_when_it_is_made()
        {
            List<string> codes = ["en", "ar"];
            LanguageDeclaration declaration = new(codes, []);
            codes.Add("fr");

            Assert.Equal(["en", "ar"], declaration.Codes);
            Assert.Null(LanguageDeclaration.None.Codes);
            Assert.Empty(LanguageDeclaration.None.Added);
            _ = Assert.Throws<ArgumentNullException>(() => new LanguageDeclaration(["en"], null!));
            _ = Assert.Throws<ArgumentNullException>(() => new LanguageCatalog(null!));
        }
    }
}
