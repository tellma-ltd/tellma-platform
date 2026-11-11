// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Localization;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary>
    ///     <c>core.languages</c>: a declared language is described, a declared region is its
    ///     language's, an added language is well formed, symbols are unique, and the platform's
    ///     strings ship for every offered language but English.
    /// </summary>
    public class LanguagesCheckTests
    {
        private const string Check = "core.languages";

        private static readonly LanguageInfo HausaInfo = new("ha", "Hausa", "Ha", false);

        private static readonly LanguageFormats HausaFormats = new("ha", "NG", ["NG", "NE"], null);

        [Fact]
        public async Task A_distribution_that_declares_nothing_offers_english_and_passes()
        {
            // The real satellite probe: English is the neutral language and needs none.
            Assert.Empty((await TestComposition.CheckAsync(static _ => { })).From(Check));
        }

        [Fact]
        public async Task A_sound_declaration_whose_strings_ship_passes()
        {
            List<string> probed = [];
            IReadOnlyList<CompositionProblem> problems = await RunAsync(
                new LanguageDeclaration(["en-GB", "ar-SA", "am", "ha-NE"], [new AddedLanguage(HausaInfo, HausaFormats)]),
                language =>
                {
                    probed.Add(language);
                    return true;
                });

            Assert.Empty(problems);

            // English is never probed; every other offered language is, the added one included.
            Assert.Equal(["ar", "am", "ha"], probed);
        }

        [Fact]
        public async Task An_offered_language_whose_strings_do_not_ship_is_reported_with_the_remedy()
        {
            IReadOnlyList<CompositionProblem> problems = await RunAsync(
                new LanguageDeclaration(["en", "ar-SA", "fr"], []), static language => language == "fr");

            CompositionProblem problem = Assert.Single(problems);
            Assert.Equal(Check, problem.Source);
            Assert.Contains("offers the language 'ar'", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("ar/Tellma.Core.resources.dll", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("SatelliteResourceLanguages", problem.Fix, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_language_that_is_described_but_not_offered_needs_no_strings()
        {
            // Hausa is added and never offered; the catalogue's fourteen other languages are not offered either.
            IReadOnlyList<CompositionProblem> problems = await RunAsync(
                new LanguageDeclaration(["en"], [new AddedLanguage(HausaInfo, HausaFormats)]), static _ => false);

            Assert.Empty(problems);
        }

        [Fact]
        public void The_real_probe_finds_the_satellites_the_platform_ships_and_no_other()
        {
            // The platform's Arabic and Amharic strings are beside the suite's build output.
            Assert.True(LanguagesCheck.HasCoreSatellite("ar"));
            Assert.True(LanguagesCheck.HasCoreSatellite("am"));
            Assert.False(LanguagesCheck.HasCoreSatellite("fr"));
            Assert.False(LanguagesCheck.HasCoreSatellite("tlh"));
            Assert.False(LanguagesCheck.HasCoreSatellite("not a culture"));
        }

        [Fact]
        public async Task A_composition_offering_the_languages_the_platform_ships_passes()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma.Languages(["en-GB", "ar-SA", "am"]));

            Assert.Empty(problems.From(Check));
        }

        [Theory]
        [InlineData("zz", "The declared language 'zz' (from 'zz') is not in the language catalogue and was not added.")]
        [InlineData("zz-ZZ", "The declared language 'zz' (from 'zz-ZZ') is not in the language catalogue and was not added.")]
        [InlineData("ar-FR", "The declared code 'ar-FR' names the region 'FR', which is not offered with the language 'ar'.")]
        [InlineData("ar_SA", "The declared language code 'ar_SA' is neither a language ('ar') nor a language with a default region ('ar-SA').")]
        [InlineData("ar-SA-u-nu-latn", "The declared language code 'ar-SA-u-nu-latn' is neither a language ('ar') nor a language with a default region ('ar-SA').")]
        [InlineData("ar-Arab-SA", "The declared language code 'ar-Arab-SA' is neither a language ('ar') nor a language with a default region ('ar-SA').")]
        [InlineData("", "The declared language code '' is neither a language ('ar') nor a language with a default region ('ar-SA').")]
        [InlineData("ar\n", "The declared language code 'ar\n' is neither a language ('ar') nor a language with a default region ('ar-SA').")]
        [InlineData("en-US", "The language 'en' is declared more than once; 'en-US' repeats it.")]
        [InlineData("EN", "The language 'en' is declared more than once; 'EN' repeats it.")]
        public async Task A_declared_code_that_does_not_resolve_is_reported(string code, string expected)
        {
            IReadOnlyList<CompositionProblem> problems = await RunAsync(new LanguageDeclaration(["en-GB", code], []), static _ => true);

            CompositionProblem problem = Assert.Single(problems);
            Assert.Equal(expected, problem.Problem);
            Assert.False(string.IsNullOrWhiteSpace(problem.Fix));
        }

        [Fact]
        public async Task An_empty_declaration_is_reported()
        {
            CompositionProblem problem = Assert.Single(await RunAsync(new LanguageDeclaration([], []), static _ => true));

            Assert.Equal("The distribution declares no language: Languages was called with an empty list.", problem.Problem);
        }

        [Fact]
        public async Task A_null_code_is_reported_and_not_thrown()
        {
            CompositionProblem problem = Assert.Single(await RunAsync(new LanguageDeclaration(["en", null!], []), static _ => true));

            Assert.StartsWith("The declared language code '' is neither", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Every_problem_of_a_declaration_is_reported_at_once()
        {
            IReadOnlyList<CompositionProblem> problems = await RunAsync(
                new LanguageDeclaration(["zz", "ar-FR", "de", "tr"], []), static language => language == "de");

            // The unknown code, the foreign region, and the strings of ar and tr: ar is offered
            // even though its region was refused.
            Assert.Equal(4, problems.Count);
            Assert.Contains(problems, static problem => problem.Problem.Contains("'zz'", StringComparison.Ordinal));
            Assert.Contains(problems, static problem => problem.Problem.Contains("'ar-FR'", StringComparison.Ordinal));
            Assert.Contains(problems, static problem => problem.Problem.Contains("offers the language 'ar'", StringComparison.Ordinal));
            Assert.Contains(problems, static problem => problem.Problem.Contains("offers the language 'tr'", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("HA", "ha", "NG", "NG,NE", null, "The added language code 'HA' is not a bare language subtag of two to eight lower-case letters.")]
        [InlineData("ha-NG", "ha-NG", "NG", "NG,NE", null, "The added language code 'ha-NG' is not a bare language subtag of two to eight lower-case letters.")]
        [InlineData("h", "h", "NG", "NG", null, "The added language code 'h' is not a bare language subtag of two to eight lower-case letters.")]
        [InlineData("ar", "ar", "NG", "NG", null, "The language 'ar' was added, but the catalogue already describes a language with that code.")]
        [InlineData("ha", "hau", "NG", "NG,NE", null, "The language 'ha' was added with formats for the language 'hau'.")]
        [InlineData("ha", "ha", "NG", "", null, "The added language 'ha' has no region.")]
        [InlineData("ha", "ha", "GH", "NG,NE", null, "The added language 'ha' has the default region 'GH', which is not one of its regions.")]
        [InlineData("ha", "ha", "NG", "NG,ng", null, "The added language 'ha' lists the region 'ng', which is not a two-letter upper-case region code.")]
        [InlineData("ha", "ha", "NG", "NG,NGA", null, "The added language 'ha' lists the region 'NGA', which is not a two-letter upper-case region code.")]
        [InlineData("ha", "ha", "NG", "NG,NE,NG", null, "The added language 'ha' lists the region 'NG' twice.")]
        [InlineData("ha", "ha", "NG", "NG,NE", "Arab", "The added language 'ha' names the native digits 'Arab', which is not a CLDR numbering-system id such as 'arab'.")]
        [InlineData("ha", "ha", "NG", "NG,NE", "u-nu-arab", "The added language 'ha' names the native digits 'u-nu-arab', which is not a CLDR numbering-system id such as 'arab'.")]
        public async Task A_malformed_added_language_is_reported(
            string code, string formatsLanguage, string defaultRegion, string regions, string? nativeDigits, string expected)
        {
            AddedLanguage added = new(
                new LanguageInfo(code, "Hausa", "Ha", false),
                new LanguageFormats(formatsLanguage, defaultRegion, regions.Split(',', StringSplitOptions.RemoveEmptyEntries), nativeDigits));

            IReadOnlyList<CompositionProblem> problems = await RunAsync(new LanguageDeclaration(["en"], [added]), static _ => true);

            CompositionProblem problem = Assert.Single(problems);
            Assert.Equal(expected, problem.Problem);
            Assert.False(string.IsNullOrWhiteSpace(problem.Fix));
        }

        [Theory]
        [InlineData("", "Ha")]
        [InlineData("Hausa", " ")]
        public async Task An_added_language_without_a_name_or_a_symbol_is_reported(string nativeName, string symbol)
        {
            AddedLanguage added = new(new LanguageInfo("ha", nativeName, symbol, false), HausaFormats);

            CompositionProblem problem = Assert.Single(await RunAsync(new LanguageDeclaration(["en"], [added]), static _ => true));

            Assert.Equal("The added language 'ha' has no native name or no symbol.", problem.Problem);
        }

        [Fact]
        public async Task An_addition_without_its_parts_is_reported_and_not_thrown()
        {
            IReadOnlyList<CompositionProblem> problems = await RunAsync(
                new LanguageDeclaration(["en"], [null!, new AddedLanguage(null!, HausaFormats), new AddedLanguage(HausaInfo, null!)]), static _ => true);

            Assert.Equal(3, problems.Count);
            Assert.All(problems, static problem => Assert.Equal("A language was added without its description or without its formats.", problem.Problem));
        }

        [Fact]
        public async Task Offering_a_refused_addition_reports_the_addition_once_and_not_an_unknown_language()
        {
            AddedLanguage added = new(HausaInfo, HausaFormats with { Regions = [] });

            CompositionProblem problem = Assert.Single(await RunAsync(new LanguageDeclaration(["en", "ha"], [added]), static _ => true));

            Assert.Equal("The added language 'ha' has no region.", problem.Problem);
        }

        [Fact]
        public async Task A_language_added_twice_is_reported()
        {
            AddedLanguage added = new(HausaInfo, HausaFormats);
            AddedLanguage again = new(HausaInfo with { Symbol = "Hau" }, HausaFormats);

            CompositionProblem problem = Assert.Single(await RunAsync(new LanguageDeclaration(["en"], [added, again]), static _ => true));

            Assert.Equal("The language 'ha' was added, but the catalogue already describes a language with that code.", problem.Problem);
        }

        [Theory]
        [InlineData("E")]
        [InlineData("e")]
        [InlineData("ع")]
        public async Task A_symbol_two_languages_share_is_reported_without_regard_to_case(string symbol)
        {
            AddedLanguage added = new(HausaInfo with { Symbol = symbol }, HausaFormats);

            CompositionProblem problem = Assert.Single(await RunAsync(new LanguageDeclaration(["en"], [added]), static _ => true));

            Assert.Contains("share the symbol", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("'ha'", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void The_catalogues_own_symbols_are_unique()
        {
            var set = LanguageSet.Resolve(LanguageDeclaration.None);

            Assert.Empty(set.Problems);
            Assert.Equal(set.All.Count, set.All.Select(static language => language.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public async Task The_builders_declaration_reaches_the_check()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma
                .AddLanguage(HausaInfo, HausaFormats with { DefaultRegion = "GH" })
                .Languages(["en-GB", "ha", "zz"]));

            // The malformed addition, the unknown code, and Hausa's strings, which the suite's
            // build output does not hold.
            List<CompositionProblem> languages = problems.From(Check);
            Assert.Equal(3, languages.Count);
            Assert.Contains(languages, static problem => problem.Problem.Contains("default region 'GH'", StringComparison.Ordinal));
            Assert.Contains(languages, static problem => problem.Problem.Contains("'zz'", StringComparison.Ordinal));
            Assert.Contains(languages, static problem => problem.Problem.Contains("ha/Tellma.Core.resources.dll", StringComparison.Ordinal));
        }

        [Fact]
        public void Declaring_the_languages_twice_with_different_lists_fails_composition()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(static tellma => tellma.Languages(["en", "ar"]).Languages(["ar", "en"]));

            CompositionProblem problem = Assert.Single(problems);
            Assert.Equal("Languages was called twice, with [en, ar] and with [ar, en].", problem.Problem);
            Assert.Contains("once", problem.Fix, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Declaring_the_same_languages_twice_is_harmless()
        {
            IServiceCollection services = TestComposition.Compose(static tellma => tellma.Languages(["en-GB", "fr"]).Languages(["en-GB", "fr"]));
            await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

            Assert.Equal(["en", "fr"], provider.GetRequiredService<ILanguageCatalog>().Offered.Select(static language => language.Code));
        }

        [Fact]
        public void The_declaration_closes_when_the_composition_method_returns()
        {
            TellmaBuilder? captured = null;
            _ = TestComposition.Compose(tellma => captured = tellma);

            _ = Assert.Throws<InvalidOperationException>(() => captured!.Languages(["en"]));
            _ = Assert.Throws<InvalidOperationException>(() => captured!.AddLanguage(HausaInfo, HausaFormats));
        }

        [Fact]
        public void Null_arguments_to_the_builder_are_refused()
        {
            _ = TestComposition.Compose(static tellma =>
            {
                _ = Assert.Throws<ArgumentNullException>(() => tellma.Languages(null!));
                _ = Assert.Throws<ArgumentNullException>(() => tellma.AddLanguage(null!, HausaFormats));
                _ = Assert.Throws<ArgumentNullException>(() => tellma.AddLanguage(HausaInfo, null!));
            });
        }

        [Fact]
        public void The_builder_copies_the_declared_list()
        {
            List<string> codes = ["en-GB", "ar-SA"];
            IServiceCollection services = TestComposition.Compose(tellma =>
            {
                _ = tellma.Languages(codes);
                codes.Add("zz");
            });
            using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

            Assert.Equal(["en-GB", "ar-SA"], provider.GetRequiredService<LanguageDeclaration>().Codes);
        }

        /// <summary>Runs the check over a declaration with a stand-in for the satellite probe.</summary>
        /// <param name="declaration">The declaration.</param>
        /// <param name="hasCoreSatellite">Whether the platform's strings ship for a language.</param>
        /// <returns>The problems.</returns>
        private static async Task<IReadOnlyList<CompositionProblem>> RunAsync(LanguageDeclaration declaration, Func<string, bool> hasCoreSatellite)
        {
            TellmaCompositionRecord record = new(TestComposition.Slug, TestComposition.Configuration(), new TestEnvironment());
            record.Settle([], new Dictionary<Type, Type>(), declaration);
            LanguagesCheck check = new(record, hasCoreSatellite);

            Assert.False(check.RequiresInfrastructure);
            await using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
            return await check.CheckAsync(provider, TestContext.Current.CancellationToken);
        }
    }
}
