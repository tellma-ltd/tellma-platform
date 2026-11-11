// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;

namespace Tellma.Core.Data.Batches
{
    /// <summary>The name rules of statement text: plain words, variable names and bracket-quoted object names.</summary>
    internal static class SqlNames
    {
        /// <summary>The most parts an object name may have: schema-qualified table, or database too.</summary>
        private const int MaxObjectNameParts = 3;

        /// <summary>Whether a word is ASCII letters, digits and underscores, starting with a letter or underscore.</summary>
        /// <param name="word">The word.</param>
        /// <returns>True when it is.</returns>
        public static bool IsPlainWord(ReadOnlySpan<char> word)
        {
            if (word.IsEmpty || char.IsAsciiDigit(word[0]))
            {
                return false;
            }

            foreach (char character in word)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character != '_')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether a name is a variable name: <c>@</c> and a plain word.</summary>
        /// <param name="name">The name.</param>
        /// <returns>True when it is.</returns>
        public static bool IsVariableName(string name)
        {
            return name.Length > 1 && name[0] == '@' && IsPlainWord(name.AsSpan(1));
        }

        /// <summary>
        ///     Bracket-quotes an object name part by part, doubling each closing bracket:
        ///     <c>fixture.Widgets</c> becomes <c>[fixture].[Widgets]</c>.
        /// </summary>
        /// <param name="name">The bare name, its parts separated by dots.</param>
        /// <returns>The quoted name.</returns>
        /// <exception cref="ArgumentException">A part is empty, or the name has too many parts.</exception>
        public static string QuoteObjectName(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            string[] parts = name.Split('.');
            if (parts.Length > MaxObjectNameParts || Array.Exists(parts, static part => part.Length == 0))
            {
                throw new ArgumentException(
                    $"The object name '{name}' must be one to {MaxObjectNameParts} non-empty parts separated by dots, without brackets.", nameof(name));
            }

            StringBuilder quoted = new(name.Length + (parts.Length * 2));
            for (int index = 0; index < parts.Length; index++)
            {
                if (index > 0)
                {
                    _ = quoted.Append('.');
                }

                _ = quoted.Append('[').Append(parts[index].Replace("]", "]]", StringComparison.Ordinal)).Append(']');
            }

            return quoted.ToString();
        }
    }
}
