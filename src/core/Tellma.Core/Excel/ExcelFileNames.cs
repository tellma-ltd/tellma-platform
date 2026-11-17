// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     The download names of exported workbooks: <c>&lt;stem&gt; &lt;yyyy-MM-dd HHmm&gt;.xlsx</c>,
    ///     the stem in its ASCII form, cut so the whole name fits 255 characters, and the time in the
    ///     display zone.
    /// </summary>
    internal static class ExcelFileNames
    {
        /// <summary>The stem of a report whose title has no ASCII form.</summary>
        public const string ReportStem = "report";

        /// <summary>The extension every exported workbook carries.</summary>
        public const string Extension = ".xlsx";

        /// <summary>The longest file name composed: the most a stored file name holds.</summary>
        public const int MaxLength = 255;

        /// <summary>The length of the stamp after the stem: <c> yyyy-MM-dd HHmm</c>.</summary>
        private const int StampLength = 16;

        /// <summary>The longest stem kept.</summary>
        private static readonly int MaxStemLength = MaxLength - StampLength - Extension.Length;

        /// <summary>Composes a file name.</summary>
        /// <param name="stem">The wanted stem: an entity's plural label, or a report's title.</param>
        /// <param name="fallback">The stem used when the wanted one's ASCII form leaves nothing.</param>
        /// <param name="now">The instant the file is stamped with.</param>
        /// <param name="zone">The display zone the instant is shown in.</param>
        /// <returns>The file name, at most 255 characters.</returns>
        /// <exception cref="ArgumentException"><paramref name="fallback" /> has no ASCII form either.</exception>
        public static string Compose(string stem, string fallback, DateTimeOffset now, TimeZoneInfo zone)
        {
            ArgumentNullException.ThrowIfNull(stem);
            ArgumentNullException.ThrowIfNull(fallback);
            ArgumentNullException.ThrowIfNull(zone);

            string ascii = AsciiForm(stem);
            if (ascii.Length == 0)
            {
                ascii = AsciiForm(fallback);
                if (ascii.Length == 0)
                {
                    throw new ArgumentException("The fallback stem must keep at least one ASCII letter, digit, space, underscore or hyphen.", nameof(fallback));
                }
            }

            // A long title is cut, never the stamp: the name must fit the stored file name's length.
            if (ascii.Length > MaxStemLength)
            {
                ascii = ascii[..MaxStemLength].TrimEnd();
            }

            DateTimeOffset local = TimeZoneInfo.ConvertTime(now, zone);
            return string.Create(CultureInfo.InvariantCulture, $"{ascii} {local:yyyy-MM-dd HHmm}{Extension}");
        }

        /// <summary>
        ///     The ASCII form of a stem: every character outside <c>[A-Za-z0-9 _-]</c> dropped, runs of
        ///     spaces collapsed and the ends trimmed.
        /// </summary>
        /// <param name="stem">The stem.</param>
        /// <returns>The ASCII form; empty when nothing is left.</returns>
        public static string AsciiForm(string stem)
        {
            ArgumentNullException.ThrowIfNull(stem);
            StringBuilder builder = new(stem.Length);
            foreach (char c in stem)
            {
                bool kept = char.IsAsciiLetterOrDigit(c) || c is ' ' or '_' or '-';

                // A space never follows a space, so a dropped word leaves no double gap.
                if (kept && !(c == ' ' && (builder.Length == 0 || builder[^1] == ' ')))
                {
                    _ = builder.Append(c);
                }
            }

            return builder.ToString().TrimEnd();
        }
    }
}
