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
    /// <summary>What folding omitted parameters out of one text left of it.</summary>
    /// <param name="Constant">The truth value the whole text folded to, or null when text is left.</param>
    /// <param name="Text">
    ///     What is left, printed back with only the parentheses it needs; the truth value's spelling
    ///     when the whole text folded to one.
    /// </param>
    /// <param name="Untouched">Whether folding returned the very tree it was given.</param>
    public sealed record ProbeFolded(bool? Constant, string Text, bool Untouched);

    /// <summary>
    ///     Looks at what folding omitted parameters does to a parse tree, before anything binds it.
    /// </summary>
    /// <remarks>
    ///     Folding is syntactic, and a compiled statement only shows its consequences: a clause that
    ///     is not there could have been folded away or never written. This shows the step itself,
    ///     printed as text an author could have typed, so the rule is pinned where it is applied.
    /// </remarks>
    public static class ProbeFolding
    {
        /// <summary>Folds a text read as one predicate: a filter leaf or a group-level one.</summary>
        /// <param name="text">The text.</param>
        /// <param name="parameters">The declared parameters.</param>
        /// <param name="omitted">The declared parameters left out.</param>
        /// <param name="negated">Whether an odd number of negations stand above the text.</param>
        /// <returns>What is left.</returns>
        /// <exception cref="ArgumentException">
        ///     The text does not parse, or a name left out is not declared.
        /// </exception>
        public static ProbeFolded Predicate(
            string text,
            IReadOnlyList<QueryexParameterDeclaration> parameters,
            IReadOnlyCollection<string> omitted,
            bool negated = false)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(parameters);
            ArgumentNullException.ThrowIfNull(omitted);

            ExpressionListSyntax syntax = Parse(text);
            FoldedPredicateList folded = Resolve(parameters, omitted).FoldPredicate(syntax, negated, out _);

            return folded.Constant is bool constant
                ? new ProbeFolded(constant, constant ? "true" : "false", Untouched: false)
                : new ProbeFolded(null, SyntaxPrinter.Print(folded.List!), ReferenceEquals(folded.List, syntax));
        }

        /// <summary>Folds a text read as values: a select list or an ordering list.</summary>
        /// <param name="text">The text.</param>
        /// <param name="parameters">The declared parameters.</param>
        /// <param name="omitted">The declared parameters left out.</param>
        /// <returns>What is left.</returns>
        /// <exception cref="ArgumentException">
        ///     The text does not parse, or a name left out is not declared.
        /// </exception>
        public static ProbeFolded Values(
            string text,
            IReadOnlyList<QueryexParameterDeclaration> parameters,
            IReadOnlyCollection<string> omitted)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(parameters);
            ArgumentNullException.ThrowIfNull(omitted);

            ExpressionListSyntax syntax = Parse(text);
            ExpressionListSyntax folded = Resolve(parameters, omitted).FoldValues(syntax, out _);

            return new ProbeFolded(null, SyntaxPrinter.Print(folded), ReferenceEquals(folded, syntax));
        }

        /// <summary>Resolves the names left out against the declarations.</summary>
        /// <param name="parameters">The declared parameters.</param>
        /// <param name="omitted">The names left out.</param>
        /// <returns>The resolved set.</returns>
        private static OmittedParameters Resolve(
            IReadOnlyList<QueryexParameterDeclaration> parameters,
            IReadOnlyCollection<string> omitted)
        {
            return OmittedParameters.Resolve(omitted, Symbols.From(parameters), nameof(omitted));
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
