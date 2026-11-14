// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     Reads an <c>Accept-Language</c> header value: its language ranges in the order the client
    ///     prefers them, each reduced to a language and, when it names one, a two-letter region.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Ranges are ordered by their quality value, highest first, the header's own order
    ///         deciding ties; a range of quality zero is one the client refuses and is left out.
    ///         Only the first <see cref="MaxRanges" /> are kept: a browser's long tail of
    ///         languages says little about the language the reader wants now.
    ///     </para>
    ///     <para>
    ///         Extensions are dropped: everything from the first single-character subtag on
    ///         (<c>-u-nu-arab</c>, <c>-t-…</c>, <c>-x-…</c>), so <c>ar-SA-u-ca-islamic-umalqura</c>
    ///         reads as <c>ar-SA</c>. The calendar and the digits are never taken from the language.
    ///     </para>
    /// </remarks>
    internal static class AcceptLanguage
    {
        /// <summary>How many of the header's ranges take part in negotiation.</summary>
        public const int MaxRanges = 3;

        /// <summary>The ASCII letters.</summary>
        private static readonly SearchValues<char> Letters =
            SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");

        /// <summary>The ASCII letters and digits.</summary>
        private static readonly SearchValues<char> LettersAndDigits =
            SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789");

        /// <summary>Reads a header value.</summary>
        /// <param name="header">The header's value.</param>
        /// <param name="malformed">True when an element of the list is not a language range with an optional quality value; that element is skipped.</param>
        /// <returns>The first ranges in preference order, at most <see cref="MaxRanges" />.</returns>
        public static IReadOnlyList<LanguageRange> Parse(string header, out bool malformed)
        {
            malformed = false;
            List<(LanguageRange Range, int Quality)> ranges = [];

            // --- Each element: a range, optionally followed by ;q= and a quality value ---
            foreach (string element in header.Split(','))
            {
                ReadOnlySpan<char> text = element.AsSpan().Trim(" \t");
                if (text.IsEmpty)
                {
                    // An empty element is legal list syntax, and says nothing.
                    continue;
                }

                int quality = 1000;
                int semicolon = text.IndexOf(';');
                if (semicolon >= 0)
                {
                    if (!TryReadQuality(text[(semicolon + 1)..].Trim(" \t"), out quality))
                    {
                        malformed = true;
                        continue;
                    }

                    text = text[..semicolon].TrimEnd(" \t");
                }

                if (!TryReadRange(text, out LanguageRange range))
                {
                    malformed = true;
                    continue;
                }

                if (quality > 0)
                {
                    ranges.Add((range, quality));
                }
            }

            // --- Highest quality first; the header's order decides ties (OrderBy is stable) ---
            return [.. ranges.OrderByDescending(static item => item.Quality).Take(MaxRanges).Select(static item => item.Range)];
        }

        /// <summary>Reads one language tag, such as a surface's fixed message language.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="range">The tag's language and region.</param>
        /// <returns>True when the tag is well formed and names a language.</returns>
        public static bool TryReadTag(string tag, out LanguageRange range)
        {
            return TryReadRange(tag.AsSpan().Trim(" \t"), out range) && range.Language.Length > 0;
        }

        /// <summary>Reads a quality parameter: <c>q=</c> and a value from 0 to 1 with at most three decimals.</summary>
        /// <param name="text">The parameter.</param>
        /// <param name="quality">The value in thousandths.</param>
        /// <returns>True when the parameter is well formed.</returns>
        private static bool TryReadQuality(ReadOnlySpan<char> text, out int quality)
        {
            quality = 0;
            if (text.Length < 3 || text.Length > 7 || (text[0] is not ('q' or 'Q')) || text[1] != '=')
            {
                return false;
            }

            ReadOnlySpan<char> value = text[2..];
            if (value[0] is not ('0' or '1') || (value.Length > 1 && value[1] != '.'))
            {
                return false;
            }

            // The whole part, then up to three decimals as thousandths.
            quality = (value[0] - '0') * 1000;
            int scale = 100;
            for (int i = 2; i < value.Length; i++)
            {
                if (!char.IsAsciiDigit(value[i]))
                {
                    return false;
                }

                quality += (value[i] - '0') * scale;
                scale /= 10;
            }

            return quality <= 1000;
        }

        /// <summary>Reads a language range: <c>*</c>, or a language tag whose extensions are dropped.</summary>
        /// <param name="text">The range.</param>
        /// <param name="range">The range's language and region; a range that names no language (<c>*</c>, a private-use tag) has an empty language.</param>
        /// <returns>True when the range is well formed.</returns>
        private static bool TryReadRange(ReadOnlySpan<char> text, out LanguageRange range)
        {
            range = new LanguageRange(string.Empty, null);
            if (text is "*")
            {
                return true;
            }

            // --- The form: subtags of one to eight ASCII letters or digits, the first letters only ---
            string? language = null;
            string? region = null;
            bool first = true;
            bool extension = false;
            bool regionPossible = true;
            foreach (Range part in text.Split('-'))
            {
                ReadOnlySpan<char> subtag = text[part];
                if (subtag.Length is 0 or > 8 || subtag.ContainsAnyExcept(first ? Letters : LettersAndDigits))
                {
                    return false;
                }

                if (first)
                {
                    first = false;

                    // A single-letter first subtag (x-…, i-…) is a tag that names no language.
                    extension = subtag.Length == 1;
                    language = extension ? null : subtag.ToString().ToLowerInvariant();
                    continue;
                }

                // --- Everything from the first single-character subtag on is an extension ---
                extension |= subtag.Length == 1;
                if (extension || !regionPossible)
                {
                    continue;
                }

                // --- A script (four letters) or an extended language (three) may precede the region ---
                if (subtag.Length == 2 && !subtag.ContainsAnyExcept(Letters))
                {
                    region = subtag.ToString().ToUpperInvariant();
                    regionPossible = false;
                }
                else if (!(subtag.Length is 3 or 4 && !subtag.ContainsAnyExcept(Letters)))
                {
                    // A numeric region or a variant: no two-letter region follows it.
                    regionPossible = false;
                }
            }

            range = new LanguageRange(language ?? string.Empty, language is null ? null : region);
            return true;
        }
    }

    /// <summary>One language range of a request: its language and, when it names one, its region.</summary>
    /// <param name="Language">The language subtag in lower case; empty when the range names no language.</param>
    /// <param name="Region">The two-letter region in upper case; null when the range names none.</param>
    internal readonly record struct LanguageRange(string Language, string? Region);
}
