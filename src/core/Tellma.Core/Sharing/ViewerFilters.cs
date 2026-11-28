// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Sharing
{
    // Spec 0014 §5.1 lists me() among the context functions a stored permission's filter may call;
    // it is the only one whose value differs from one holder to the next (now() and today() are the
    // instant's). The scan mirrors the Queryex lexer's token boundaries: a string literal (a doubled
    // quote escaping one), a bracketed identifier (a doubled closing bracket escaping one) and a
    // named parameter are skipped whole, so only a call written as code counts.
    /// <summary>
    ///     Tells a stored permission's filter that reads the current user, through <c>me()</c>, from
    ///     one whose rows are the same for every holder. The scan is lexical and errs towards
    ///     reading the user: a text it cannot tell apart counts as reading it.
    /// </summary>
    internal static class ViewerFilters
    {
        /// <summary>The context function that reads the current user.</summary>
        private const string CurrentUser = "me";

        /// <summary>Whether a filter calls <c>me()</c>.</summary>
        /// <param name="filter">The filter's Queryex text.</param>
        /// <returns>True when it calls <c>me()</c> outside string literals, bracketed identifiers and parameters.</returns>
        /// <exception cref="ArgumentNullException">The filter is null.</exception>
        public static bool ReadsCurrentUser(string filter)
        {
            ArgumentNullException.ThrowIfNull(filter);

            int index = 0;
            while (index < filter.Length)
            {
                // --- Runs whose text is never a call: literals, bracketed names, parameters ---
                char current = filter[index];
                if (current == '\'')
                {
                    index = SkipQuoted(filter, index, '\'');
                    continue;
                }

                if (current == '[')
                {
                    index = SkipQuoted(filter, index, ']');
                    continue;
                }

                if (current == '@')
                {
                    index = SkipWord(filter, index + 1);
                    continue;
                }

                // --- A word: the function's name when not a member of a path and followed by a call ---
                if (IsWordStart(current))
                {
                    int end = SkipWord(filter, index);
                    if (end - index == CurrentUser.Length
                        && string.Compare(filter, index, CurrentUser, 0, CurrentUser.Length, StringComparison.OrdinalIgnoreCase) == 0
                        && !FollowsDot(filter, index)
                        && Next(filter, end) == '(')
                    {
                        return true;
                    }

                    index = end;
                    continue;
                }

                index++;
            }

            return false;
        }

        /// <summary>The index past a run that a closing character ends, where the character doubled escapes itself.</summary>
        /// <param name="text">The text.</param>
        /// <param name="start">The run's opening character.</param>
        /// <param name="closing">The closing character.</param>
        /// <returns>The index past the run, or the text's length when it never closes.</returns>
        private static int SkipQuoted(string text, int start, char closing)
        {
            int index = start + 1;
            while (index < text.Length)
            {
                if (text[index] != closing)
                {
                    index++;
                    continue;
                }

                // A doubled closing character escapes itself and the run goes on.
                if (index + 1 < text.Length && text[index + 1] == closing)
                {
                    index += 2;
                    continue;
                }

                return index + 1;
            }

            return text.Length;
        }

        /// <summary>The index past a word's letters, digits and underscores.</summary>
        /// <param name="text">The text.</param>
        /// <param name="start">The word's first character.</param>
        /// <returns>The index past the word.</returns>
        private static int SkipWord(string text, int start)
        {
            int index = start;
            while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '_'))
            {
                index++;
            }

            return index;
        }

        /// <summary>Whether a character starts a word.</summary>
        /// <param name="character">The character.</param>
        /// <returns>True for a letter or an underscore.</returns>
        private static bool IsWordStart(char character)
        {
            return char.IsLetter(character) || character == '_';
        }

        /// <summary>Whether the first character before a position, past white space, is a dot.</summary>
        /// <param name="text">The text.</param>
        /// <param name="index">The position.</param>
        /// <returns>True when the word there is a member of a path.</returns>
        private static bool FollowsDot(string text, int index)
        {
            int before = index - 1;
            while (before >= 0 && char.IsWhiteSpace(text[before]))
            {
                before--;
            }

            return before >= 0 && text[before] == '.';
        }

        /// <summary>The first character from a position, past white space.</summary>
        /// <param name="text">The text.</param>
        /// <param name="index">The position.</param>
        /// <returns>The character; NUL at the end of the text.</returns>
        private static char Next(string text, int index)
        {
            int at = index;
            while (at < text.Length && char.IsWhiteSpace(text[at]))
            {
                at++;
            }

            return at < text.Length ? text[at] : '\0';
        }
    }
}
