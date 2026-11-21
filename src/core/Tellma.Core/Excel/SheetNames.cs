// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers;
using System.Globalization;
using System.Text;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     The sheet names of one workbook: each scrubbed of the characters Excel refuses, at most 31
    ///     characters, never <c>History</c>, and unique within the workbook with <c> (2)</c>,
    ///     <c> (3)</c> suffixes. Excel compares sheet names ignoring case, and so does this.
    /// </summary>
    internal sealed class SheetNames
    {
        /// <summary>The longest sheet name Excel accepts.</summary>
        public const int MaxLength = 31;

        /// <summary>The name of the hidden manifest sheet.</summary>
        public const string Manifest = "_tellma";

        /// <summary>The name Excel reserves for its change history.</summary>
        private const string Reserved = "History";

        /// <summary>The name a sheet takes when scrubbing leaves nothing of its own.</summary>
        private const string Fallback = "Sheet";

        /// <summary>The characters a sheet name may not hold.</summary>
        private static readonly SearchValues<char> Refused = SearchValues.Create("/\\?*:[]");

        /// <summary>The names taken so far, compared ignoring case.</summary>
        private readonly HashSet<string> _taken = new(StringComparer.OrdinalIgnoreCase) { Reserved };

        /// <summary>
        ///     Scrubs a name: the characters Excel refuses, control characters and what XML cannot carry
        ///     (a lone surrogate, U+FFFE, U+FFFF) dropped, the apostrophes Excel refuses at either end
        ///     trimmed, cut to 31 characters.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>The scrubbed name; <c>Sheet</c> when nothing is left.</returns>
        public static string Scrub(string name)
        {
            string scrubbed = Cut(Clean(name), MaxLength).TrimEnd();
            return scrubbed.Length == 0 ? Fallback : scrubbed;
        }

        /// <summary>
        ///     Cleans a name as <see cref="Scrub" /> does but without cutting it: the characters Excel
        ///     refuses dropped and the ends trimmed, so a caller can tell how long the name would be.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>The cleaned name, possibly empty and possibly longer than 31 characters.</returns>
        public static string Clean(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            // --- Drop what Excel refuses anywhere in a name, and what the workbook's XML cannot carry ---
            StringBuilder builder = new(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsHighSurrogate(c) && i + 1 < name.Length && char.IsLowSurrogate(name[i + 1]))
                {
                    // A well-formed pair is one character outside the basic plane, and is kept.
                    _ = builder.Append(c).Append(name[i + 1]);
                    i++;
                }
                else if (!char.IsSurrogate(c) && XmlStrings.IsXmlChar(c) && !char.IsControl(c) && !Refused.Contains(c))
                {
                    _ = builder.Append(c);
                }
            }

            // --- Trim: white space, and the apostrophe Excel refuses at either end ---
            return builder.ToString().Trim().Trim('\'').Trim();
        }

        /// <summary>Takes a unique name: the scrubbed name, or it with the first free <c> (n)</c> suffix.</summary>
        /// <param name="name">The wanted name, scrubbed or not.</param>
        /// <returns>The name taken, which no later call returns again.</returns>
        public string Take(string name)
        {
            string scrubbed = Scrub(name);
            if (_taken.Add(scrubbed))
            {
                return scrubbed;
            }

            // The suffix always fits: the base is cut to leave room for it.
            for (int n = 2; ; n++)
            {
                string suffix = string.Create(CultureInfo.InvariantCulture, $" ({n})");
                string candidate = string.Concat(Cut(scrubbed, MaxLength - suffix.Length).TrimEnd(), suffix);
                if (_taken.Add(candidate))
                {
                    return candidate;
                }
            }
        }

        /// <summary>Cuts text to a length without splitting a surrogate pair.</summary>
        /// <param name="text">The text.</param>
        /// <param name="length">The most characters kept.</param>
        /// <returns>The text, cut.</returns>
        private static string Cut(string text, int length)
        {
            return text.Length <= length ? text : char.IsHighSurrogate(text[length - 1]) ? text[..(length - 1)] : text[..length];
        }
    }
}
