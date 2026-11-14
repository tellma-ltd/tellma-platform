// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;

namespace Tellma.Core.Blobs
{
    /// <summary>
    ///     Sanitises an upload's original file name into the display-only name a blob row keeps and a
    ///     download offers in <c>Content-Disposition</c>. The result is never used as a path.
    /// </summary>
    /// <remarks>
    ///     The rules: the name is trimmed; path separators, control characters, bidirectional
    ///     formatting characters, line and paragraph separators and unpaired surrogates each become <c>_</c>; the result is
    ///     normalised to NFC; a bare <c>.</c> or <c>..</c> becomes <c>file</c>; and a result longer
    ///     than <see cref="MaxLength" /> characters is shortened, keeping a short extension. Blank
    ///     input yields null.
    /// </remarks>
    internal static class BlobFileNames
    {
        /// <summary>The longest sanitised name, in UTF-16 code units (the column's length).</summary>
        public const int MaxLength = 255;

        /// <summary>The name that replaces a bare <c>.</c> or <c>..</c>.</summary>
        public const string Placeholder = "file";

        // The longest extension (dot included) that survives shortening; a longer tail is not an
        // extension worth keeping.
        private const int MaxPreservedExtensionLength = 16;

        // What every refused character becomes.
        private const char Replacement = '_';

        /// <summary>Sanitises a file name.</summary>
        /// <param name="fileName">The client's file name, or null.</param>
        /// <returns>The display-only name, or null when the input is null or blank.</returns>
        public static string? Sanitize(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            // Replace character by character, pairing surrogates so a valid astral character (an
            // emoji) survives and a lone half, which no encoder can write, does not.
            string trimmed = fileName.Trim();
            StringBuilder builder = new(trimmed.Length);
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (char.IsHighSurrogate(c) && i + 1 < trimmed.Length && char.IsLowSurrogate(trimmed[i + 1]))
                {
                    builder.Append(c).Append(trimmed[i + 1]);
                    i++;
                    continue;
                }

                builder.Append(IsRefused(c) ? Replacement : c);
            }

            // NFC, so a name typed on a system that decomposes accents displays and compares like
            // the same name typed elsewhere; safe now that no unpaired surrogate remains.
            string sanitized = builder.ToString().Normalize(NormalizationForm.FormC);

            return sanitized switch
            {
                "." or ".." => Placeholder,
                { Length: <= MaxLength } => sanitized,
                _ => Shorten(sanitized),
            };
        }

        /// <summary>Whether a character is replaced: a path separator, a control, a line or paragraph separator, a surrogate half or a bidirectional control.</summary>
        /// <param name="c">The character.</param>
        /// <returns>True when it is replaced.</returns>
        private static bool IsRefused(char c)
        {
            return c is '/' or '\\'
                || char.IsControl(c)
                || char.IsSurrogate(c)

                // The line and paragraph separators, which break a display name across lines.
                || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator

                // The bidirectional marks, embeddings, overrides and isolates, which can make
                // "invoice_cod.exe" display as "invoice_exe.doc". Written as escapes so the source
                // itself carries no invisible reordering characters. ZWJ and ZWNJ are kept: emoji
                // sequences and Persian need them.
                || c is '\u200E' or '\u200F' or '\u061C'
                || c is >= '\u202A' and <= '\u202E'
                || c is >= '\u2066' and <= '\u2069';
        }

        /// <summary>Shortens a name to <see cref="MaxLength" />, keeping its extension when it is short.</summary>
        /// <param name="name">The over-long name.</param>
        /// <returns>The shortened name.</returns>
        private static string Shorten(string name)
        {
            int dot = name.LastIndexOf('.');
            string extension = dot > 0 && name.Length - dot <= MaxPreservedExtensionLength ? name[dot..] : string.Empty;
            string stem = extension.Length > 0 ? name[..dot] : name;

            return TruncateByTextElements(stem, MaxLength - extension.Length) + extension;
        }

        /// <summary>
        ///     Truncates to at most <paramref name="maxLength" /> code units without splitting a
        ///     surrogate pair or a combining sequence.
        /// </summary>
        /// <param name="value">The text.</param>
        /// <param name="maxLength">The largest result length.</param>
        /// <returns>The longest whole-element prefix that fits.</returns>
        private static string TruncateByTextElements(string value, int maxLength)
        {
            int length = 0;
            TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(value);
            while (elements.MoveNext())
            {
                int next = elements.ElementIndex + ((string)elements.Current).Length;
                if (next > maxLength)
                {
                    break;
                }

                length = next;
            }

            return value[..length];
        }
    }
}
