// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;

namespace Tellma.Core.Queryex.Lexing
{
    /// <summary>
    ///     The reserved words.
    /// </summary>
    /// <remarks>
    ///     The lexer scans an identifier and then looks it up here, rather than looking ahead
    ///     character by character. That is why <c>Notes</c>, <c>Ordering</c>, and <c>Internal</c>
    ///     are ordinary identifiers with no special handling, and why function names are not
    ///     reserved at all — an identifier is a function name only when a parenthesis follows it.
    /// </remarks>
    internal static class Keywords
    {
        /// <summary>The reserved words, matched case-insensitively.</summary>
        private static readonly FrozenDictionary<string, TokenKind> Table =
            new Dictionary<string, TokenKind>(StringComparer.OrdinalIgnoreCase)
            {
                ["and"] = TokenKind.And,
                ["or"] = TokenKind.Or,
                ["not"] = TokenKind.Not,
                ["in"] = TokenKind.In,
                ["is"] = TokenKind.Is,
                ["null"] = TokenKind.Null,
                ["true"] = TokenKind.True,
                ["false"] = TokenKind.False,
                ["asc"] = TokenKind.Asc,
                ["desc"] = TokenKind.Desc,
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        /// <summary>Resolves an unbracketed identifier to a reserved word, if it is one.</summary>
        /// <param name="identifier">The scanned identifier text.</param>
        /// <param name="kind">The reserved word's kind, when it is one.</param>
        /// <returns>True when the identifier is reserved.</returns>
        internal static bool TryResolve(string identifier, out TokenKind kind)
        {
            return Table.TryGetValue(identifier, out kind);
        }

        /// <summary>Whether a name would be read as a reserved word if written unbracketed.</summary>
        /// <param name="name">The name to test.</param>
        /// <returns>True when the name is reserved.</returns>
        internal static bool IsReserved(string name)
        {
            return Table.ContainsKey(name);
        }
    }
}
