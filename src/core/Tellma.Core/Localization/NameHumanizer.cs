// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     Turns a code name into English words, the last link of every label chain:
    ///     <c>CreditExposureReport</c> reads "Credit exposure report", <c>autoNumberOnPost</c> reads
    ///     "Auto number on post", <c>acme.sales</c> reads "Acme sales".
    /// </summary>
    internal static class NameHumanizer
    {
        /// <summary>
        ///     Splits a name into words at separators (<c>.</c>, <c>-</c>, <c>_</c>, spaces) and at case
        ///     changes, keeps an acronym's capitals (<c>GLAccount</c> reads "GL account"), lowercases
        ///     every other word and capitalizes the first.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>The words; the name itself when it has none.</returns>
        public static string Humanize(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            // --- Words: separators split, and so does a capital after a lowercase letter or a digit,
            // or a capital that starts a word after an acronym ---
            List<string> words = [];
            StringBuilder word = new();
            for (int index = 0; index < name.Length; index++)
            {
                char c = name[index];
                if (c is '.' or '-' or '_' or ' ')
                {
                    Flush(word, words);
                    continue;
                }

                if (word.Length > 0 && char.IsUpper(c))
                {
                    char previous = name[index - 1];
                    bool afterLower = char.IsLower(previous) || char.IsDigit(previous);
                    bool endsAcronym = char.IsUpper(previous) && index + 1 < name.Length && char.IsLower(name[index + 1]);
                    if (afterLower || endsAcronym)
                    {
                        Flush(word, words);
                    }
                }

                _ = word.Append(c);
            }

            Flush(word, words);

            // --- Casing: acronyms stay, other words lowercase, the first letter capital ---
            string text = string.Join(' ', words.Select(static w => w.Length > 1 && w.All(static ch => !char.IsLetter(ch) || char.IsUpper(ch)) ? w : w.ToLowerInvariant()));
            return text.Length == 0 ? name : char.ToUpperInvariant(text[0]) + text[1..];
        }

        /// <summary>Ends the current word, if any.</summary>
        /// <param name="word">The word being built; cleared.</param>
        /// <param name="words">Receives the word.</param>
        private static void Flush(StringBuilder word, List<string> words)
        {
            if (word.Length > 0)
            {
                words.Add(word.ToString());
                _ = word.Clear();
            }
        }
    }
}
