// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Queryex.Testing.Semantics
{
    /// <summary>
    ///     How the second implementation compares text.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Written out rather than delegated to the platform's own comparison, because the
    ///         platform's answer differs between operating systems. A check that means one thing on
    ///         one machine and another elsewhere proves nothing about the language, so this compares
    ///         by a table stated here.
    ///     </para>
    ///     <para>
    ///         The table covers a deliberately small repertoire, and a guard refuses any fixture or
    ///         corpus text that steps outside it. Widening the repertoire is then a decision somebody
    ///         makes on purpose, with the weights to go with it, rather than something that happens
    ///         by accident the first time an unusual character is typed.
    ///     </para>
    ///     <para>
    ///         Two rules of the backend's are reproduced exactly. Comparison pads the shorter of two
    ///         values with spaces, so trailing spaces do not distinguish one value from another;
    ///         searching within a value does not pad, so there they do. That difference is why the
    ///         matching functions are written the way they are.
    ///     </para>
    /// </remarks>
    public static class Collation
    {
        /// <summary>The lowest character the repertoire covers.</summary>
        private const char First = ' ';

        /// <summary>The highest character the repertoire covers.</summary>
        private const char Last = '~';

        /// <summary>Whether every character of a value is one this table has a weight for.</summary>
        /// <param name="text">The text.</param>
        /// <returns>True when it is.</returns>
        public static bool IsInRepertoire(string text)
        {
            ArgumentNullException.ThrowIfNull(text);

            foreach (char character in text)
            {
                if (character is < First or > Last)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Orders two values the way the declared collation does.</summary>
        /// <param name="left">One value.</param>
        /// <param name="right">The other.</param>
        /// <returns>Negative, zero, or positive.</returns>
        public static int Compare(string left, string right)
        {
            ArgumentNullException.ThrowIfNull(left);
            ArgumentNullException.ThrowIfNull(right);

            // Padded to a common length rather than compared and then length-broken, because that is
            // what the backend does: two values that differ only in trailing spaces are one value.
            int length = Math.Max(left.Length, right.Length);
            for (int index = 0; index < length; index++)
            {
                int one = Weight(index < left.Length ? left[index] : ' ');
                int other = Weight(index < right.Length ? right[index] : ' ');
                if (one != other)
                {
                    return one < other ? -1 : 1;
                }
            }

            return 0;
        }

        /// <summary>Whether two values are the same value.</summary>
        /// <param name="left">One value.</param>
        /// <param name="right">The other.</param>
        /// <returns>True when they are.</returns>
        public static bool AreEqual(string left, string right)
        {
            return Compare(left, right) == 0;
        }

        /// <summary>Where one value first occurs inside another, counting from one.</summary>
        /// <param name="haystack">The value searched.</param>
        /// <param name="needle">The value looked for.</param>
        /// <returns>The position, or zero when it does not occur.</returns>
        /// <remarks>
        ///     No padding here, so a trailing space is an ordinary character that has to be matched.
        ///     The empty value occurs at the very beginning of everything, including of itself.
        /// </remarks>
        public static int IndexOf(string haystack, string needle)
        {
            ArgumentNullException.ThrowIfNull(haystack);
            ArgumentNullException.ThrowIfNull(needle);

            if (needle.Length == 0)
            {
                return 1;
            }

            for (int start = 0; start + needle.Length <= haystack.Length; start++)
            {
                bool matches = true;
                for (int offset = 0; offset < needle.Length; offset++)
                {
                    if (Weight(haystack[start + offset]) != Weight(needle[offset]))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    return start + 1;
                }
            }

            return 0;
        }

        /// <summary>The number of characters a value has, ignoring trailing spaces.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The length.</returns>
        /// <remarks>
        ///     The backend's own rule, and the one the language adopts: a length that counted
        ///     trailing spaces would disagree with the equality that ignores them.
        /// </remarks>
        public static int Length(string text)
        {
            ArgumentNullException.ThrowIfNull(text);

            int length = text.Length;
            while (length > 0 && text[length - 1] == ' ')
            {
                length--;
            }

            return length;
        }

        /// <summary>The weight one character sorts and matches by.</summary>
        /// <param name="character">The character.</param>
        /// <returns>Its weight.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     The character is outside the repertoire this table covers.
        /// </exception>
        private static int Weight(char character)
        {
            if (character is < First or > Last)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(character),
                    character,
                    "The collation table covers only the characters the fixtures are allowed to use.");
            }

            // Case-insensitive: the two cases of a letter are the same character as far as ordering
            // and matching are concerned, which is what the declared collation says.
            return character is >= 'a' and <= 'z' ? character - ('a' - 'A') : character;
        }
    }
}
