// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Testing.Resources
{
    /// <summary>The resource keys a string literal of the source stands for: a key spelled outright, or one derived from a code the source raises.</summary>
    /// <param name="literal">The literal's content.</param>
    /// <returns>The keys it uses; none when it uses none.</returns>
    public delegate IEnumerable<string> LiteralKeyRule(string literal);

    /// <summary>The literal rules of the platform's key conventions.</summary>
    public static class LiteralKeyRules
    {
        /// <summary>A literal is the key it spells (<c>"Entity_DisplayNameFallback"</c>).</summary>
        public static LiteralKeyRule Verbatim { get; } = static literal => [literal];

        /// <summary>
        ///     A dotted literal is a code whose key writes its dots <c>_</c>
        ///     (<c>"Settings.LanguageNotOffered"</c> uses <c>Settings_LanguageNotOffered</c>).
        /// </summary>
        public static LiteralKeyRule Dotted { get; } = static literal => literal.Contains('.', StringComparison.Ordinal) ? [literal.Replace('.', '_')] : [];

        /// <summary>
        ///     A kebab-case literal is a problem code, whose title and detail are
        ///     <c>Problem_&lt;code&gt;</c> and <c>Problem_&lt;code&gt;_Detail</c> with its hyphens
        ///     written <c>_</c> (<c>"concurrency-conflict"</c> uses <c>Problem_concurrency_conflict</c>).
        /// </summary>
        public static LiteralKeyRule ProblemCode { get; } = static literal =>
        {
            if (!IsKebabCase(literal))
            {
                return [];
            }

            string key = "Problem_" + literal.Replace('-', '_');
            return [key, key + "_Detail"];
        };

        /// <summary>A literal is a query diagnostic's code, whose message is <c>Queryex_&lt;code&gt;</c> (<c>"QX3001"</c>).</summary>
        public static LiteralKeyRule QueryDiagnostic { get; } = static literal => IsDiagnosticCode(literal) ? ["Queryex_" + literal] : [];

        /// <summary>Every rule of the platform's conventions: verbatim, dotted, problem codes and query diagnostics.</summary>
        public static IReadOnlyList<LiteralKeyRule> Platform { get; } = [Verbatim, Dotted, ProblemCode, QueryDiagnostic];

        /// <summary>Whether a literal is lower-case words of ASCII letters and digits joined by single hyphens.</summary>
        /// <param name="literal">The literal.</param>
        /// <returns>True for a kebab-case code.</returns>
        private static bool IsKebabCase(string literal)
        {
            return literal.Length > 0
                && literal.Split('-').All(static word => word.Length > 0 && char.IsAsciiLetterLower(word[0]) && word.All(static c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)));
        }

        /// <summary>Whether a literal is <c>QX</c> and four digits.</summary>
        /// <param name="literal">The literal.</param>
        /// <returns>True for a diagnostic code.</returns>
        private static bool IsDiagnosticCode(string literal)
        {
            return literal.Length == 6 && literal.StartsWith("QX", StringComparison.Ordinal) && literal[2..].All(char.IsAsciiDigit);
        }
    }
}
