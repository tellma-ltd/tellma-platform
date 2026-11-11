// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     A distribution's language declaration resolved against the catalogue: the languages it can
    ///     describe, the ones it offers, each language's formats, and everything wrong with the
    ///     declaration.
    /// </summary>
    /// <remarks>
    ///     Resolution never fails. What is wrong is skipped (an unknown code is not offered, a
    ///     second language under one code is not added) and listed in <see cref="Problems" />, which
    ///     the startup gate reports; a host therefore never runs on a declaration with problems, and
    ///     a build step can still construct the catalogue to report them.
    /// </remarks>
    internal sealed partial class LanguageSet
    {
        /// <summary>The language a distribution offers when it declares none, and the fallback when nothing it declared resolves.</summary>
        public const string English = "en";

        /// <summary>Creates the resolved set.</summary>
        /// <param name="all">Every described language, catalogue first.</param>
        /// <param name="offered">The offered languages, in declaration order.</param>
        /// <param name="formats">Every described language's formats, by code.</param>
        /// <param name="problems">What is wrong with the declaration.</param>
        private LanguageSet(
            IReadOnlyList<LanguageInfo> all,
            IReadOnlyList<LanguageInfo> offered,
            Dictionary<string, LanguageFormats> formats,
            IReadOnlyList<LanguageProblem> problems)
        {
            All = all;
            Offered = offered;
            Problems = problems;
            ByCode = all.ToFrozenDictionary(static language => language.Code, StringComparer.OrdinalIgnoreCase);
            Formats = formats.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
            OfferedCodes = offered.Select(static language => language.Code).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
            OfferedRegions = offered
                .SelectMany(language => formats[language.Code].Regions)
                .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Every described language: the catalogue's, in catalogue order, then the added ones.</summary>
        public IReadOnlyList<LanguageInfo> All { get; }

        /// <summary>The offered languages, in declaration order; never empty.</summary>
        public IReadOnlyList<LanguageInfo> Offered { get; }

        /// <summary>Every described language by code, without regard to case.</summary>
        public FrozenDictionary<string, LanguageInfo> ByCode { get; }

        /// <summary>Every described language's formats by code, with the distribution's default region where it declared one.</summary>
        public FrozenDictionary<string, LanguageFormats> Formats { get; }

        /// <summary>The offered codes, without regard to case.</summary>
        public FrozenSet<string> OfferedCodes { get; }

        /// <summary>The regions offered with at least one offered language, without regard to case.</summary>
        public FrozenSet<string> OfferedRegions { get; }

        /// <summary>What is wrong with the declaration; empty when it is sound.</summary>
        public IReadOnlyList<LanguageProblem> Problems { get; }

        /// <summary>Resolves a declaration against the catalogue.</summary>
        /// <param name="declaration">The declaration.</param>
        /// <returns>The resolved set.</returns>
        public static LanguageSet Resolve(LanguageDeclaration declaration)
        {
            List<LanguageProblem> problems = [];
            List<LanguageInfo> all = [];
            Dictionary<string, LanguageFormats> formats = new(StringComparer.OrdinalIgnoreCase);

            // --- The catalogue ---
            foreach (CatalogueLanguage entry in LanguageCatalogData.Languages)
            {
                all.Add(entry.Info);
                formats.Add(entry.Info.Code, entry.Formats);
            }

            // --- The added languages ---
            // A refused addition is remembered, so a declared code naming it is not reported a
            // second time as unknown.
            HashSet<string> refused = new(StringComparer.OrdinalIgnoreCase);
            foreach (AddedLanguage added in declaration.Added)
            {
                LanguageFormats? usable = CheckAdded(added, formats, problems);
                if (usable is not null)
                {
                    all.Add(added.Info);
                    formats.Add(added.Info.Code, usable);
                }
                else if (!string.IsNullOrWhiteSpace(added?.Info?.Code) && !formats.ContainsKey(added.Info.Code))
                {
                    _ = refused.Add(added.Info.Code);
                }
            }

            // --- Symbols are unique: two languages would label their columns alike ---
            foreach (IGrouping<string, LanguageInfo> clash in all
                .Where(static language => !string.IsNullOrEmpty(language.Symbol))
                .GroupBy(static language => language.Symbol, StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Count() > 1))
            {
                string codes = string.Join(" and ", clash.Select(static language => $"'{language.Code}'"));
                problems.Add(new LanguageProblem(
                    $"The languages {codes} share the symbol '{clash.Key}'.",
                    "Give the language added with AddLanguage a symbol no other language has: its symbol labels its column of every multilingual field."));
            }

            // --- The offered codes ---
            List<LanguageInfo> offered = [];
            if (declaration.Codes is not null)
            {
                ResolveOffered(declaration.Codes, all, formats, refused, offered, problems);
            }

            // English is what an undeclared distribution offers, and what stands in for a
            // declaration nothing of which resolved, so the catalogue always has a default.
            if (offered.Count == 0)
            {
                offered.Add(all.First(static language => language.Code == English));
            }

            return new LanguageSet(all.AsReadOnly(), offered.AsReadOnly(), formats, problems.AsReadOnly());
        }

        /// <summary>Resolves the declared codes into the offered languages, giving each its declared default region.</summary>
        /// <param name="codes">The declared codes.</param>
        /// <param name="all">Every described language.</param>
        /// <param name="formats">Every described language's formats; a declared region replaces the default region here.</param>
        /// <param name="refused">The codes of additions that were refused.</param>
        /// <param name="offered">Receives the offered languages.</param>
        /// <param name="problems">Receives the problems.</param>
        private static void ResolveOffered(
            IReadOnlyList<string> codes,
            List<LanguageInfo> all,
            Dictionary<string, LanguageFormats> formats,
            HashSet<string> refused,
            List<LanguageInfo> offered,
            List<LanguageProblem> problems)
        {
            if (codes.Count == 0)
            {
                problems.Add(new LanguageProblem(
                    "The distribution declares no language: Languages was called with an empty list.",
                    "Declare at least one language, the default first, such as tellma.Languages([\"en-GB\", \"ar-SA\"]), or remove the call to offer English."));
                return;
            }

            foreach (string code in codes)
            {
                // --- The form: a language, or a language and a region ---
                Match match = code is null ? Match.Empty : DeclaredCode().Match(code);
                if (!match.Success)
                {
                    problems.Add(new LanguageProblem(
                        $"The declared language code '{code}' is neither a language ('ar') nor a language with a default region ('ar-SA').",
                        "Declare each language as its two- or three-letter code, optionally followed by a hyphen and a two-letter region."));
                    continue;
                }

                string languageCode = match.Groups["language"].Value;
                LanguageInfo? language = all.Find(candidate => string.Equals(candidate.Code, languageCode, StringComparison.OrdinalIgnoreCase));
                if (language is null)
                {
                    if (!refused.Contains(languageCode))
                    {
                        problems.Add(new LanguageProblem(
                            $"The declared language '{languageCode}' (from '{code}') is not in the language catalogue and was not added.",
                            $"Add it with tellma.AddLanguage(info, formats) before offering it, or remove '{code}' from Languages."));
                    }

                    continue;
                }

                if (offered.Contains(language))
                {
                    problems.Add(new LanguageProblem(
                        $"The language '{language.Code}' is declared more than once; '{code}' repeats it.",
                        $"Declare '{language.Code}' once in Languages, with the one region its users get by default."));
                    continue;
                }

                offered.Add(language);

                // --- The default region, when the code carries one ---
                if (!match.Groups["region"].Success)
                {
                    continue;
                }

                LanguageFormats catalogued = formats[language.Code];
                string region = match.Groups["region"].Value;
                string? known = catalogued.Regions.FirstOrDefault(candidate => string.Equals(candidate, region, StringComparison.OrdinalIgnoreCase));
                if (known is null)
                {
                    problems.Add(new LanguageProblem(
                        $"The declared code '{code}' names the region '{region}', which is not offered with the language '{language.Code}'.",
                        $"Declare '{language.Code}' with one of its regions, such as '{language.Code}-{catalogued.DefaultRegion}', or without a region."));
                    continue;
                }

                formats[language.Code] = catalogued with { DefaultRegion = known };
            }
        }

        /// <summary>Checks one added language, and returns its formats in the catalogue's form when it can be described.</summary>
        /// <param name="added">The added language.</param>
        /// <param name="formats">The formats of the languages described so far.</param>
        /// <param name="problems">Receives the problems.</param>
        /// <returns>The formats to describe the language with; null when it cannot be added.</returns>
        private static LanguageFormats? CheckAdded(AddedLanguage? added, Dictionary<string, LanguageFormats> formats, List<LanguageProblem> problems)
        {
            const string Fix = "Call tellma.AddLanguage(new LanguageInfo(code, nativeName, symbol, isRightToLeft), new LanguageFormats(code, defaultRegion, regions, nativeDigits)) with the same lower-case code in both, at least one two-letter upper-case region, and a default region among them.";

            if (added?.Info is null || added.Formats is null)
            {
                problems.Add(new LanguageProblem("A language was added without its description or without its formats.", Fix));
                return null;
            }

            LanguageInfo info = added.Info;
            LanguageFormats given = added.Formats;

            // --- The code ---
            if (info.Code is null || !LanguageCode().IsMatch(info.Code))
            {
                problems.Add(new LanguageProblem(
                    $"The added language code '{info.Code}' is not a bare language subtag of two to eight lower-case letters.", Fix));
                return null;
            }

            if (formats.ContainsKey(info.Code))
            {
                problems.Add(new LanguageProblem(
                    $"The language '{info.Code}' was added, but the catalogue already describes a language with that code.",
                    $"Remove the AddLanguage call for '{info.Code}'; declare it in Languages to offer it."));
                return null;
            }

            bool usable = true;
            if (!string.Equals(given.Language, info.Code, StringComparison.Ordinal))
            {
                problems.Add(new LanguageProblem(
                    $"The language '{info.Code}' was added with formats for the language '{given.Language}'.", Fix));
            }

            // --- The description ---
            if (string.IsNullOrWhiteSpace(info.NativeName) || string.IsNullOrWhiteSpace(info.Symbol))
            {
                problems.Add(new LanguageProblem(
                    $"The added language '{info.Code}' has no native name or no symbol.", Fix));
            }

            // --- The regions ---
            List<string> regions = [];
            foreach (string region in given.Regions ?? [])
            {
                if (region is null || !RegionCode().IsMatch(region))
                {
                    problems.Add(new LanguageProblem(
                        $"The added language '{info.Code}' lists the region '{region}', which is not a two-letter upper-case region code.", Fix));
                }
                else if (regions.Contains(region, StringComparer.Ordinal))
                {
                    problems.Add(new LanguageProblem(
                        $"The added language '{info.Code}' lists the region '{region}' twice.", Fix));
                }
                else
                {
                    regions.Add(region);
                }
            }

            if (regions.Count == 0)
            {
                problems.Add(new LanguageProblem($"The added language '{info.Code}' has no region.", Fix));
                usable = false;
            }

            string? defaultRegion = given.DefaultRegion;
            if (usable && !regions.Contains(defaultRegion, StringComparer.Ordinal))
            {
                problems.Add(new LanguageProblem(
                    $"The added language '{info.Code}' has the default region '{defaultRegion}', which is not one of its regions.", Fix));
                defaultRegion = null;
            }

            // --- The native digits ---
            string? nativeDigits = given.NativeDigits;
            if (nativeDigits is not null && !NumberingSystem().IsMatch(nativeDigits))
            {
                problems.Add(new LanguageProblem(
                    $"The added language '{info.Code}' names the native digits '{nativeDigits}', which is not a CLDR numbering-system id such as 'arab'.",
                    "Pass the CLDR numbering-system id of the language's digits, or null when its digits are Latin."));
                nativeDigits = null;
            }

            if (!usable)
            {
                return null;
            }

            // Regions are held in ordinal order, as the catalogue's own are.
            regions.Sort(StringComparer.Ordinal);
            return new LanguageFormats(info.Code, defaultRegion ?? regions[0], regions.AsReadOnly(), nativeDigits);
        }

        /// <summary>A declared code: a language, optionally with a region.</summary>
        /// <returns>The expression.</returns>
        [GeneratedRegex(@"^(?<language>[A-Za-z]{2,8})(-(?<region>[A-Za-z]{2}))?\z", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, 1000)]
        private static partial Regex DeclaredCode();

        /// <summary>An added language's code.</summary>
        /// <returns>The expression.</returns>
        [GeneratedRegex(@"^[a-z]{2,8}\z", RegexOptions.CultureInvariant, 1000)]
        private static partial Regex LanguageCode();

        /// <summary>A region code.</summary>
        /// <returns>The expression.</returns>
        [GeneratedRegex(@"^[A-Z]{2}\z", RegexOptions.CultureInvariant, 1000)]
        private static partial Regex RegionCode();

        /// <summary>A CLDR numbering-system id.</summary>
        /// <returns>The expression.</returns>
        [GeneratedRegex(@"^[a-z][a-z0-9]{2,7}\z", RegexOptions.CultureInvariant, 1000)]
        private static partial Regex NumberingSystem();
    }

    /// <summary>One thing wrong with a language declaration.</summary>
    /// <param name="Problem">What is wrong, naming the code.</param>
    /// <param name="Fix">The remedy.</param>
    internal sealed record LanguageProblem(string Problem, string Fix);
}
