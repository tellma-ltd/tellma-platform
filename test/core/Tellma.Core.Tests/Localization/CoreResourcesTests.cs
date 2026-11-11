// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Jeffijoe.MessageFormat;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Localization;
using Tellma.Core.Resources;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>
    ///     Core's shared resources: the key grammar, every value a well-formed ICU message, the Arabic
    ///     satellite complete, the region names generated for exactly the catalogued regions, a
    ///     title for every problem code and a message for every query diagnostic, and the empty
    ///     server-only resources.
    /// </summary>
    public sealed partial class CoreResourcesTests
    {
        /// <summary>The cultures Core ships resources for: the neutral English set and its satellites.</summary>
        public static readonly TheoryData<string> Shipped = [string.Empty, "ar", "am"];

        [Theory]
        [MemberData(nameof(Shipped))]
        public void Every_key_follows_the_grammar(string culture)
        {
            Assert.All(Entries(culture).Keys, static key => Assert.Matches(KeyGrammar(), key));
        }

        [Theory]
        [MemberData(nameof(Shipped))]
        public void Every_value_is_a_well_formed_icu_message(string culture)
        {
            CultureInfo info = NegotiatedCultures.Get(culture.Length == 0 ? "en" : culture, culture switch { "ar" => "SA", "am" => "ET", _ => "GB" });
            MessageFormatter formatter = ProductionFormatter(info);

            Assert.All(Entries(culture), entry =>
            {
                // An ASCII apostrophe starts an ICU quote; the resources write the typographic one.
                Assert.DoesNotContain('\'', entry.Value);
                Exception? failure = Record.Exception(() => formatter.FormatMessage(entry.Value, new AnyArgument(entry.Value)));
                Assert.True(failure is null, $"{entry.Key}: {failure?.Message}");
            });
        }

        [Theory]
        [InlineData("{d, date}")]
        [InlineData("{d, date, short}")]
        [InlineData("{d, date, medium}")]
        [InlineData("{d, date, long}")]
        [InlineData("{d, date, full}")]
        [InlineData("{t, time, short}")]
        [InlineData("{count, plural, one {# day} other {# days}} from {d, date, long}")]
        public void The_audit_accepts_every_date_and_time_style_the_localizer_renders(string pattern)
        {
            // The audit formats as the localizer does, so a message with a date is not refused.
            MessageFormatter formatter = ProductionFormatter(NegotiatedCultures.Get("en", "GB"));

            Assert.NotEmpty(formatter.FormatMessage(pattern, new AnyArgument(pattern)));
        }

        [Fact]
        public void Every_satellite_key_exists_in_the_neutral_resources_and_the_arabic_satellite_is_complete()
        {
            HashSet<string> neutral = [.. Entries(string.Empty).Keys];

            Assert.Empty(Entries("ar").Keys.Except(neutral));
            Assert.Empty(Entries("am").Keys.Except(neutral));
            Assert.Empty(neutral.Except(Entries("ar").Keys));
        }

        [Theory]
        [MemberData(nameof(Shipped))]
        public void Region_names_cover_exactly_the_catalogued_regions(string culture)
        {
            LanguageCatalog catalog = new(new LanguageDeclaration(["en"], []));
            string[] regions = [.. catalog.All.SelectMany(language => catalog.Formats(language.Code).Regions).Distinct().Order(StringComparer.Ordinal)];
            Dictionary<string, string> entries = Entries(culture);

            Assert.Equal(
                regions.Select(static region => "Region_" + region),
                entries.Keys.Where(static key => key.StartsWith("Region_", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
            Assert.All(regions, region => Assert.False(string.IsNullOrWhiteSpace(entries["Region_" + region])));
        }

        [Fact]
        public void Region_names_are_each_languages_own()
        {
            Assert.Equal("Saudi Arabia", Entries(string.Empty)["Region_SA"]);
            Assert.Equal("المملكة العربية السعودية", Entries("ar")["Region_SA"]);
            Assert.Equal("ኢትዮጵያ", Entries("am")["Region_ET"]);
        }

        [Fact]
        public void Every_problem_code_the_platforms_exceptions_raise_has_a_title_and_a_detail()
        {
            // The exceptions with a fixed code declare it as a constant; three take theirs from
            // the raise site, which always passes the same one.
            IEnumerable<string> codes = typeof(TellmaException).Assembly.GetTypes()
                .Where(static type => typeof(TellmaException).IsAssignableFrom(type))
                .Select(static type => type.GetField("ProblemCode", BindingFlags.NonPublic | BindingFlags.Static)?.GetRawConstantValue() as string)
                .OfType<string>()
                .Concat(["concurrency-conflict", "forbidden", "partial-failure"]);
            Dictionary<string, string> neutral = Entries(string.Empty);

            Assert.All(codes, code =>
            {
                string key = "Problem_" + code.Replace('-', '_');
                Assert.Contains(key, neutral.Keys);
                Assert.Contains(key + "_Detail", neutral.Keys);
            });
        }

        [Fact]
        public void Every_query_diagnostic_has_a_message_and_every_message_a_diagnostic()
        {
            // The query engine takes no dependency on Core, so its catalogue is read from source.
            string root = TaxonomyCheck.FindRepositoryRoot(AppContext.BaseDirectory)
                ?? throw new InvalidOperationException("The repository root was not found above the test's output.");
            string source = File.ReadAllText(Path.Combine(root, "src", "core", "Tellma.Core.Queryex", "Diagnostics", "DiagnosticCodes.cs"));
            string[] codes = [.. DiagnosticCode().Matches(source).Select(static match => "Queryex_" + match.Groups[1].Value).Order(StringComparer.Ordinal)];

            Assert.NotEmpty(codes);
            Assert.Equal(codes, Entries(string.Empty).Keys.Where(static key => key.StartsWith("Queryex_", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        }

        [Fact]
        public void The_server_only_resources_exist_and_hold_nothing_yet()
        {
            ResourceManager resources = new("Tellma.Core.Resources.ServerStrings", typeof(ServerStrings).Assembly);
            ResourceSet? set = resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);

            Assert.NotNull(set);
            Assert.Empty(set.Cast<DictionaryEntry>());
        }

        /// <summary>The entries of one culture's resources alone, without its parents'.</summary>
        /// <param name="culture">The culture's name; empty for the neutral resources.</param>
        /// <returns>The values by key.</returns>
        private static Dictionary<string, string> Entries(string culture)
        {
            ResourceSet set = Strings.ResourceManager.GetResourceSet(CultureInfo.GetCultureInfo(culture), createIfNotExists: true, tryParents: false)
                ?? throw new InvalidOperationException($"Core ships no resources for '{culture}'.");
            return set.Cast<DictionaryEntry>().ToDictionary(static entry => (string)entry.Key, static entry => (string)entry.Value!, StringComparer.Ordinal);
        }

        /// <summary>The formatter the localizer renders with: the calendar-aware date and time hook included.</summary>
        /// <param name="culture">The culture the formatter formats in.</param>
        /// <returns>The formatter.</returns>
        private static MessageFormatter ProductionFormatter(CultureInfo culture)
        {
            return new MessageFormatter(useCache: false, culture: culture, customValueFormatter: new CalendarValueFormatter());
        }

        /// <summary>A key: words of letters and digits, joined by underscores.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"^[A-Za-z0-9]+(_[A-Za-z0-9]+)*$", RegexOptions.CultureInvariant)]
        private static partial Regex KeyGrammar();

        /// <summary>A placeholder formatted as a date or a time, capturing its argument's name.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"\{\s*(\w+)\s*,\s*(?:date|time)\b", RegexOptions.CultureInvariant)]
        private static partial Regex DateOrTimePlaceholder();

        /// <summary>A diagnostic code constant of the query engine's catalogue.</summary>
        /// <returns>The pattern, capturing the code.</returns>
        [GeneratedRegex(@"const string \w+ = ""(QX\d{4})"";", RegexOptions.CultureInvariant)]
        private static partial Regex DiagnosticCode();

        /// <summary>
        ///     Arguments that name everything a message asks for: an instant for a placeholder the
        ///     message formats as a date or a time, and the number 3 for any other, which every
        ///     placeholder, plural, number and select of Core's resources formats.
        /// </summary>
        /// <param name="pattern">The message the arguments are for.</param>
        private sealed class AnyArgument(string pattern) : IReadOnlyDictionary<string, object?>
        {
            /// <summary>The names the message formats as a date or a time.</summary>
            private readonly HashSet<string> _instants = [.. DateOrTimePlaceholder().Matches(pattern).Select(static match => match.Groups[1].Value)];

            /// <inheritdoc />
            public object? this[string key] => Sample(key);

            /// <inheritdoc />
            public IEnumerable<string> Keys => [];

            /// <inheritdoc />
            public IEnumerable<object?> Values => [];

            /// <inheritdoc />
            public int Count => 0;

            /// <inheritdoc />
            public bool ContainsKey(string key)
            {
                return true;
            }

            /// <inheritdoc />
            public bool TryGetValue(string key, [MaybeNullWhen(false)] out object? value)
            {
                value = Sample(key);
                return true;
            }

            /// <summary>The sample value of one argument.</summary>
            /// <param name="key">The argument's name.</param>
            /// <returns>An instant for a date or time placeholder, else 3.</returns>
            private object Sample(string key)
            {
                return _instants.Contains(key) ? new DateTimeOffset(2026, 3, 2, 22, 30, 0, TimeSpan.Zero) : 3;
            }

            /// <inheritdoc />
            public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
            {
                return Enumerable.Empty<KeyValuePair<string, object?>>().GetEnumerator();
            }

            /// <inheritdoc />
            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
