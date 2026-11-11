// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Localization;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>Reading an <c>Accept-Language</c> header: order, quality values, extensions and malformed elements.</summary>
    public class AcceptLanguageTests
    {
        [Theory]
        [InlineData("ar-SA", "ar-SA")]
        [InlineData("ar", "ar")]
        [InlineData("AR-sa", "ar-SA")]
        [InlineData("en-GB,ar-SA;q=0.9,fr;q=0.8", "en-GB|ar-SA|fr")]
        [InlineData("fr;q=0.8, ar-SA;q=0.9 ,en-GB", "en-GB|ar-SA|fr")]
        [InlineData("fr;q=0.5,ar;q=0.5,en;q=0.5", "fr|ar|en")]
        [InlineData("en;q=0.1,ar;q=1.0,fr;q=0.555", "ar|fr|en")]
        [InlineData("en,ar,fr,de,es", "en|ar|fr")]
        [InlineData("de;q=0.1,en,ar,fr", "en|ar|fr")]
        [InlineData("en;q=0,ar", "ar")]
        [InlineData("en;q=0.000,ar;q=0.001", "ar")]
        [InlineData("en ; q=0.5 , ar", "ar|en")]
        [InlineData("en;Q=0.5,ar", "ar|en")]
        [InlineData(",en,,ar,", "en|ar")]
        [InlineData("en;q=0.,ar;q=1", "ar")]
        public void Ranges_come_in_preference_order_and_only_the_first_three(string header, string expected)
        {
            IReadOnlyList<LanguageRange> ranges = AcceptLanguage.Parse(header, out bool malformed);

            Assert.False(malformed);
            Assert.Equal(expected, Describe(ranges));
        }

        [Theory]
        [InlineData("ar-SA-u-ca-islamic-umalqura", "ar-SA")]
        [InlineData("ar-SA-u-nu-arab", "ar-SA")]
        [InlineData("ar-u-nu-latn", "ar")]
        [InlineData("en-GB-x-private-US", "en-GB")]
        [InlineData("de-DE-t-en-US", "de-DE")]
        [InlineData("ar-u-rg-aezzzz-SA", "ar")]
        public void Extensions_are_dropped(string header, string expected)
        {
            IReadOnlyList<LanguageRange> ranges = AcceptLanguage.Parse(header, out bool malformed);

            Assert.False(malformed);
            Assert.Equal(expected, Describe(ranges));
        }

        [Theory]
        [InlineData("zh-Hans-CN", "zh-CN")]
        [InlineData("zh-Hant", "zh")]
        [InlineData("zh-yue-HK", "zh-HK")]
        [InlineData("sr-Latn-RS", "sr-RS")]
        [InlineData("es-419", "es")]
        [InlineData("de-1996", "de")]
        [InlineData("de-DE-1996", "de-DE")]
        [InlineData("en-001-US", "en")]
        public void The_region_is_the_two_letter_subtag_after_the_language_and_its_script(string header, string expected)
        {
            Assert.Equal(expected, Describe(AcceptLanguage.Parse(header, out _)));
        }

        [Theory]
        [InlineData("*", "")]
        [InlineData("*;q=0.5,ar", "ar|")]
        [InlineData("x-klingon,ar", "|ar")]
        [InlineData("i-default", "")]
        public void A_range_that_names_no_language_is_well_formed_and_matches_nothing(string header, string expected)
        {
            IReadOnlyList<LanguageRange> ranges = AcceptLanguage.Parse(header, out bool malformed);

            Assert.False(malformed);
            Assert.Equal(expected, Describe(ranges));
        }

        [Theory]
        [InlineData("ar_SA", "")]
        [InlineData("ar-SA;q=2", "")]
        [InlineData("ar-SA;q=0.1234", "")]
        [InlineData("ar-SA;q=abc", "")]
        [InlineData("ar-SA;q=", "")]
        [InlineData("ar-SA;level=1", "")]
        [InlineData("ar-SA;q=0.5;q=0.6", "")]
        [InlineData("ar-", "")]
        [InlineData("-ar", "")]
        [InlineData("ar--SA", "")]
        [InlineData("123", "")]
        [InlineData("abcdefghi", "")]
        [InlineData("ar-abcdefghi", "")]
        [InlineData("عربي", "")]
        [InlineData("en us", "")]
        [InlineData("**", "")]
        [InlineData("ar_SA,en-GB", "en-GB")]
        [InlineData("en-GB;q=x,ar-SA", "ar-SA")]
        public void A_malformed_element_is_skipped_and_flagged(string header, string expected)
        {
            IReadOnlyList<LanguageRange> ranges = AcceptLanguage.Parse(header, out bool malformed);

            Assert.True(malformed);
            Assert.Equal(expected, Describe(ranges));
        }

        [Theory]
        [InlineData("ar-SA", true, "ar", "SA")]
        [InlineData(" en ", true, "en", null)]
        [InlineData("EN-us-u-nu-latn", true, "en", "US")]
        [InlineData("*", false, "", null)]
        [InlineData("x-private", false, "", null)]
        [InlineData("not a tag", false, "", null)]
        [InlineData("", false, "", null)]
        public void A_single_tag_reads_as_its_language_and_region(string tag, bool read, string language, string? region)
        {
            Assert.Equal(read, AcceptLanguage.TryReadTag(tag, out LanguageRange range));
            Assert.Equal(new LanguageRange(language, region), range);
        }

        /// <summary>Writes ranges as <c>language-REGION</c> joined by bars; a range without a language is empty.</summary>
        /// <param name="ranges">The ranges.</param>
        /// <returns>The text.</returns>
        private static string Describe(IReadOnlyList<LanguageRange> ranges)
        {
            return string.Join('|', ranges.Select(static range => range.Region is null ? range.Language : $"{range.Language}-{range.Region}"));
        }
    }
}
