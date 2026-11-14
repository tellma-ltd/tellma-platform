// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;
using System.Text.RegularExpressions;

namespace Tellma.Core.Crud
{
    /// <summary>
    ///     The name grammars of the stack layer: route segments and action names, the securable action
    ///     an action name implies, named filter names and confirmation keys.
    /// </summary>
    internal static partial class StackNames
    {
        /// <summary>The longest action name, segments and separators included.</summary>
        public const int MaxActionNameLength = 128;

        /// <summary>
        ///     The kebab-case form of a PascalCase name: a hyphen before each word, words lower-cased,
        ///     an acronym kept as one word (<c>InvoiceLines</c> → <c>invoice-lines</c>,
        ///     <c>UXItems</c> → <c>ux-items</c>).
        /// </summary>
        /// <param name="name">The name, such as a table's.</param>
        /// <returns>The kebab-case form.</returns>
        public static string Kebab(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            StringBuilder kebab = new(name.Length + 8);
            for (int index = 0; index < name.Length; index++)
            {
                char c = name[index];
                if (char.IsUpper(c) && index > 0)
                {
                    // A word starts at a capital after a lowercase letter or a digit, and at the last
                    // capital of an acronym that a lowercase letter follows.
                    char previous = name[index - 1];
                    bool afterLower = char.IsLower(previous) || char.IsDigit(previous);
                    bool endsAcronym = char.IsUpper(previous) && index + 1 < name.Length && char.IsLower(name[index + 1]);
                    if (afterLower || endsAcronym)
                    {
                        _ = kebab.Append('-');
                    }
                }

                _ = kebab.Append(c is '_' or ' ' ? '-' : char.ToLowerInvariant(c));
            }

            return kebab.ToString();
        }

        /// <summary>Whether a text is one kebab-case route segment: lowercase ASCII words of letters and digits, the first starting with a letter.</summary>
        /// <param name="segment">The text.</param>
        /// <returns>True when it is.</returns>
        public static bool IsSegment(string? segment)
        {
            return segment is not null && SegmentPattern().IsMatch(segment);
        }

        /// <summary>Whether a text is an action name: one or more route segments joined by <c>/</c>, within <see cref="MaxActionNameLength" />.</summary>
        /// <param name="name">The text.</param>
        /// <returns>True when it is.</returns>
        public static bool IsActionName(string? name)
        {
            return name is { Length: > 0 and <= MaxActionNameLength } && name.Split('/').All(IsSegment);
        }

        /// <summary>
        ///     The securable action an action name implies: the PascalCase form of its last segment
        ///     (<c>invite</c> → <c>Invite</c>, <c>take-over</c> → <c>TakeOver</c>,
        ///     <c>preferences/set</c> → <c>Set</c>).
        /// </summary>
        /// <param name="name">A valid action name.</param>
        /// <returns>The action.</returns>
        public static string ActionOf(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            string last = name[(name.LastIndexOf('/') + 1)..];
            return string.Concat(last.Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(static word => char.ToUpperInvariant(word[0]) + word[1..]));
        }

        /// <summary>Whether a text is a named filter's name: a lowercase ASCII letter, then lowercase letters, digits and hyphens.</summary>
        /// <param name="name">The text.</param>
        /// <returns>True when it is.</returns>
        public static bool IsNamedFilterName(string? name)
        {
            return name is not null && NamedFilterPattern().IsMatch(name);
        }

        /// <summary>Whether a text is a confirmation key: two or more PascalCase segments joined by dots, such as <c>Inventory.BackdatedRecost</c>.</summary>
        /// <param name="key">The text.</param>
        /// <returns>True when it is.</returns>
        public static bool IsConfirmationKey(string? key)
        {
            return key is { Length: <= 256 } && ConfirmationKeyPattern().IsMatch(key);
        }

        /// <summary>One kebab-case segment.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
        private static partial Regex SegmentPattern();

        /// <summary>A named filter's name.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex("^[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
        private static partial Regex NamedFilterPattern();

        /// <summary>A dotted PascalCase key.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex("^[A-Z][A-Za-z0-9]*(\\.[A-Z][A-Za-z0-9]*)+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
        private static partial Regex ConfirmationKeyPattern();
    }
}
