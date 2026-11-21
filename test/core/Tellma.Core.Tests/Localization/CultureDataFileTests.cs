// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using Tellma.Core.Calendars;
using Tellma.Core.Localization;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>
    ///     The pinned culture data: its text form, the embedded file's completeness, and that the
    ///     file is what its generator writes.
    /// </summary>
    public class CultureDataFileTests
    {
        /// <summary>
        ///     The environment variable that, set to <c>1</c>, makes the regeneration test fail
        ///     instead of skip on a host whose ICU data is not the pinned release.
        /// </summary>
        private const string RequirePinnedIcuVariable = "TELLMA_REQUIRE_PINNED_ICU";

        [Fact]
        public void The_embedded_data_covers_exactly_the_catalogue_pairs_in_order()
        {
            CultureDataFile pinned = CultureDataFile.Pinned;

            Assert.Equal(CultureDataSource.CataloguePairs(), pinned.Pairs);
            Assert.Equal(243, pinned.Cultures.Count);
            Assert.NotEmpty(pinned.Source);
            Assert.Matches("^[0-9a-f]{8}$", pinned.Host);
        }

        [Fact]
        public void The_embedded_data_is_utf8_without_a_byte_order_mark_and_in_canonical_form()
        {
            byte[] bytes = EmbeddedBytes();
            Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble), "The culture data starts with a byte-order mark.");

            // Writing what was read gives the same text: the file is exactly what the writer
            // produces, with nothing edited in by hand. A checkout may have turned line feeds into
            // carriage-return line feeds, which the reader tolerates.
            string text = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal);
            CultureDataFile pinned = CultureDataFile.Pinned;
            string rewritten = CultureDataFile.Write(
                pinned.Source, pinned.Host, [.. pinned.Pairs.Select(pair => new KeyValuePair<string, CultureSnapshot>(pair, pinned.Cultures[pair]))]);

            Assert.Equal(text, rewritten);
        }

        [Fact]
        public void No_pinned_value_carries_a_bidirectional_control_or_a_native_digit()
        {
            CultureDataFile pinned = CultureDataFile.Pinned;

            Assert.All(pinned.Pairs, pair => Assert.All(CultureField.All, field =>
            {
                string value = CultureText.Decode(pinned.Cultures[pair][field.Name]);
                Assert.DoesNotContain(value, static c => CalendarText.IsBidiControl(c));
                Assert.DoesNotContain(value, static c => char.IsDigit(c) && !char.IsAsciiDigit(c));
            }));
        }

        [Fact]
        public void Date_and_time_text_carries_ordinary_spaces_only()
        {
            // The calendar systems render with these; a number's group separator may be a no-break space.
            string[] fields =
            [
                "shortDate", "longDate", "shortTime", "longTime", "yearMonth", "monthDay", "dateSeparator", "timeSeparator", "am", "pm",
                "months", "monthsAbbreviated", "monthsGenitive", "monthsAbbreviatedGenitive", "days", "daysAbbreviated", "daysShortest",
            ];
            CultureDataFile pinned = CultureDataFile.Pinned;

            Assert.All(pinned.Pairs, pair => Assert.All(fields, field =>
                Assert.DoesNotContain(CultureText.Decode(pinned.Cultures[pair][field]), static c => CalendarText.IsSpaceVariant(c))));
        }

        [Fact]
        public void The_platforms_corrections_to_the_source_are_pinned()
        {
            CultureDataFile pinned = CultureDataFile.Pinned;

            // Oromo and Tigrinya write the day first, as Amharic does.
            Assert.All(["om-ET", "om-KE", "ti-ER", "ti-ET", "am-ET"], pair => Assert.Equal("dd/MM/yyyy", pinned.Cultures[pair]["shortDate"]));

            // Every Arabic pair writes the percent sign that goes with Latin digits.
            Assert.All(
                pinned.Pairs.Where(static pair => pair.StartsWith("ar-", StringComparison.Ordinal)),
                pair => Assert.Equal("%", pinned.Cultures[pair]["percentSymbol"]));
        }

        [Fact]
        public void The_embedded_data_is_what_the_generator_writes_on_the_pinned_ICU_release()
        {
            // Only a host with the ICU data the file was generated from can regenerate it; the
            // other hosts hold the cultures to the file instead.
            CultureDataFile pinned = CultureDataFile.Pinned;
            string host = CultureDataFile.HostFingerprint();
            if (host != pinned.Host)
            {
                // A run that declares its host to be the pinned release (the Linux leg of the
                // pull-request build) fails here, so that a runner image moving to another ICU
                // release is noticed instead of turning this guard into a skip nobody reads.
                Assert.False(
                    Environment.GetEnvironmentVariable(RequirePinnedIcuVariable) == "1",
                    $"{RequirePinnedIcuVariable}=1 says this host carries the ICU release the culture data pins ({pinned.Host}), but its ICU data is {host}. "
                    + "Regenerate the culture data on this host's release, or run this leg on an image with the pinned one.");

                Assert.Skip($"This host's ICU data ({host}) is not the release the culture data pins ({pinned.Host}).");
            }

            string text = Encoding.UTF8.GetString(EmbeddedBytes()).Replace("\r\n", "\n", StringComparison.Ordinal);

            Assert.Equal(text, CultureDataSource.Generate(pinned.Source));
        }

        [Fact]
        public void A_pair_section_lays_its_fields_over_its_languages()
        {
            string text = Minimal("xx") + "\n[xx-AA]\n\n[xx-BB]\nshortDate=M/d/yyyy\nam=\\u0041M\n";

            var file = CultureDataFile.Parse(text);

            Assert.Equal(["xx-AA", "xx-BB"], file.Pairs);
            Assert.Equal("dd/MM/yyyy", file.Cultures["xx-AA"]["shortDate"]);
            Assert.Equal("M/d/yyyy", file.Cultures["xx-BB"]["shortDate"]);
            Assert.Equal("\\u0041M", file.Cultures["xx-BB"]["am"]);
            Assert.Equal("a test", file.Source);
            Assert.Equal("0000abcd", file.Host);
            Assert.Same(file.Cultures["xx-AA"], file.Cultures["XX-aa"]);
        }

        [Fact]
        public void Writing_puts_the_most_common_value_in_the_language_section()
        {
            var file = CultureDataFile.Parse(Minimal("xx") + "\n[xx-AA]\nam=x\n\n[xx-BB]\nam=y\n\n[xx-CC]\nam=y\n");

            string written = CultureDataFile.Write(
                file.Source, file.Host, [.. file.Pairs.Select(pair => new KeyValuePair<string, CultureSnapshot>(pair, file.Cultures[pair]))]);

            // y is the majority, so only AA spells its own; reading it back gives the same cultures.
            Assert.Contains("\n[xx-AA]\nam=x\n\n[xx-BB]\n\n[xx-CC]\n", written, StringComparison.Ordinal);
            var reread = CultureDataFile.Parse(written);
            Assert.All(file.Pairs, pair => Assert.All(CultureField.All, field => Assert.Equal(file.Cultures[pair][field.Name], reread.Cultures[pair][field.Name])));
        }

        [Theory]
        [InlineData("shortDate=d\n", "not a name=value field inside a section")]
        [InlineData("[xx-AA]\n", "comes before its language's section")]
        [InlineData("[xx\n", "is not a section")]
        [InlineData("[xx]\nshortDate=a\nshortDate=b\n", "appears twice")]
        [InlineData("[xx]\nshortDate=a\n\n[xx-AA]\n", "incomplete")]
        [InlineData("[xx]\nnonsense\n", "not a name=value field")]
        public void Malformed_text_is_refused_with_the_reason(string text, string reason)
        {
            FormatException exception = Assert.Throws<FormatException>(() => CultureDataFile.Parse(text));

            Assert.Contains(reason, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_pair_listed_twice_or_an_unknown_field_is_refused()
        {
            _ = Assert.Throws<FormatException>(() => CultureDataFile.Parse(Minimal("xx") + "\n[xx-AA]\n\n[xx-AA]\n"));
            _ = Assert.Throws<FormatException>(() => CultureDataFile.Parse(Minimal("xx") + "\n[xx-AA]\nnoSuchField=1\n"));
        }

        [Theory]
        [InlineData("plain text", "plain text")]
        [InlineData(" lead and trail ", "\\u0020lead and trail\\u0020")]
        [InlineData("a;b\\c#d", "a\\u003Bb\\u005Cc\\u0023d")]
        [InlineData("h:mm\u202Ftt", "h:mm\\u202Ftt")]
        [InlineData("\u200F/", "\\u200F/")]
        [InlineData("", "")]
        [InlineData("يناير", "يناير")]
        public void A_value_round_trips_through_its_text_form(string value, string encoded)
        {
            Assert.Equal(encoded, CultureText.Encode(value));
            Assert.Equal(value, CultureText.Decode(encoded));
        }

        [Fact]
        public void Lists_round_trip_through_their_text_form()
        {
            string[] names = ["a;b", string.Empty, " c"];

            Assert.Equal("a\\u003Bb;;\\u0020c", CultureText.EncodeList(names));
            Assert.Equal(names, CultureText.DecodeList(CultureText.EncodeList(names)));
            Assert.Equal("3,2", CultureText.EncodeNumbers([3, 2]));
            Assert.Equal([3, 2], CultureText.DecodeNumbers("3,2"));
            Assert.Empty(CultureText.DecodeNumbers(string.Empty));
        }

        [Theory]
        [InlineData("\\u12")]
        [InlineData("\\x0041")]
        [InlineData("abc\\")]
        [InlineData("\\uZZZZ")]
        public void A_malformed_escape_is_refused(string text)
        {
            _ = Assert.Throws<FormatException>(() => CultureText.Decode(text));
        }

        [Fact]
        public void Bidirectional_controls_are_stripped_from_a_number_symbol_and_nothing_else()
        {
            Assert.Equal("-", CultureText.StripBidi("\u200E-"));
            Assert.Equal("%", CultureText.StripBidi("\u200E%\u200E"));
            Assert.Equal("-", CultureText.StripBidi("\u061C-"));
            Assert.Equal("\u00A0", CultureText.StripBidi("\u00A0"));
            Assert.Equal("ليس\u00A0رقمًا", CultureText.StripBidi("ليس\u00A0رقمًا"));
        }

        [Fact]
        public void A_snapshot_holds_exactly_the_fields()
        {
            var snapshot = CultureSnapshot.Read(CultureInfo.InvariantCulture);
            var values = CultureField.All.ToDictionary(static field => field.Name, field => snapshot[field.Name]);

            _ = Assert.Throws<ArgumentException>(() => new CultureSnapshot(values.Where(static pair => pair.Key != "am").ToDictionary()));
            _ = Assert.Throws<ArgumentException>(() => new CultureSnapshot(new Dictionary<string, string>(values) { ["extra"] = "1" }));
            Assert.Equal("x", snapshot.With(new Dictionary<string, string> { ["am"] = "x" })["am"]);
            Assert.Equal(CultureField.All.Length, CultureField.All.Select(static field => field.Name).Distinct().Count());
        }

        [Fact]
        public void Applying_a_snapshot_sets_every_field_of_a_culture()
        {
            // The invariant culture's values laid over French: every field reads back as invariant.
            var invariant = CultureSnapshot.Read(CultureInfo.InvariantCulture);
            CultureInfo culture = new("fr-FR", useUserOverride: false);

            invariant.Apply(culture);

            var actual = CultureSnapshot.Read(culture);
            Assert.All(CultureField.All, field => Assert.Equal(invariant[field.Name], actual[field.Name]));
            Assert.Equal("Sunday, 15 March 2026", new DateTime(2026, 3, 15).ToString("D", culture));
            Assert.Single(culture.DateTimeFormat.GetAllDateTimePatterns('d'));
        }

        /// <summary>A language section holding every field, with the invariant culture's values.</summary>
        /// <param name="language">The section's language.</param>
        /// <returns>The text, with its origin comments.</returns>
        private static string Minimal(string language)
        {
            var invariant = CultureSnapshot.Read(CultureInfo.InvariantCulture);
            StringBuilder builder = new($"# source: a test\n# host: 0000abcd\n\n[{language}]\n");
            foreach (CultureField field in CultureField.All)
            {
                string value = field.Name switch
                {
                    "shortDate" => "dd/MM/yyyy",
                    "am" => "y",
                    _ => invariant[field.Name],
                };
                _ = builder.Append(field.Name).Append('=').Append(value).Append('\n');
            }

            return builder.ToString();
        }

        /// <summary>The embedded culture data, as it was built in.</summary>
        /// <returns>The resource's bytes.</returns>
        private static byte[] EmbeddedBytes()
        {
            using Stream stream = typeof(CultureDataFile).Assembly.GetManifestResourceStream(CultureDataFile.ResourceName)!;
            using MemoryStream copy = new();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
    }
}
