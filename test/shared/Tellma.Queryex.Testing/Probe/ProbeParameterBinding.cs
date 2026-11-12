// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;
using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Lexing;
using Tellma.Core.Queryex.Pipeline;
using Tellma.Core.Queryex.Syntax;

namespace Tellma.Queryex.Testing.Probe
{
    /// <summary>
    ///     Looks at the tree a text's arguments were written into, before it is printed.
    /// </summary>
    /// <remarks>
    ///     The engine only hands out the printed text, and two different trees can print alike when
    ///     the printer's grouping is wrong. Comparing the tree that was printed with the parse of what
    ///     it printed as is what shows that every literal reads back as itself wherever it landed.
    /// </remarks>
    public static class ProbeParameterBinding
    {
        /// <summary>
        ///     Whether parsing the printed form of a text with its arguments written in gives back the
        ///     very tree that was printed.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="options">The arguments, the omitted parameters and what they are declared as.</param>
        /// <returns>True when it does, or when the whole text folded to a truth value.</returns>
        /// <exception cref="ArgumentException">The text does not parse, or the options are refused.</exception>
        public static bool RoundTrips(string text, ParameterBindingOptions options)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(options);

            var arguments = ParameterArguments.Resolve(options, nameof(options));
            FoldedPredicateList bound = arguments.Apply(Parse(text), options.Mode);
            if (bound.Constant is not null)
            {
                return true;
            }

            string printed = SyntaxPrinter.Print(bound.List!);
            return SyntaxEquivalence.AreEquivalent(bound.List, Parse(printed));
        }

        /// <summary>Scans and parses one text under the default ceilings.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The parsed list.</returns>
        /// <exception cref="ArgumentException">The text does not parse.</exception>
        private static ExpressionListSyntax Parse(string text)
        {
            DiagnosticSink sink = new();
            DiagnosticScope scope = sink.Scope(DiagnosticLocation.None);
            ParseBudget budget = new(QueryexLimits.Default);

            return Lexer.TryTokenize(text, budget, scope, out Token[] tokens)
                && Parser.TryParse(text, tokens, budget, scope, out ExpressionListSyntax list)
                ? list
                : throw new ArgumentException(
                    "The text does not parse: " + string.Join(", ", sink.Drain().Select(d => d.Code)),
                    nameof(text));
        }
    }
}
