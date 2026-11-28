// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Collections.Immutable;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Functions;
using Tellma.Core.Queryex.Syntax;

namespace Tellma.Core.Queryex.Pipeline
{
    /// <summary>
    ///     What folding took out of one parsed text, as far as the identity of its binding goes.
    /// </summary>
    /// <remarks>
    ///     A folded tree is no longer determined by its text alone. It is determined by the text,
    ///     the omitted parameters the text refers to, and the parity of the negations above it —
    ///     and those two are exactly what a binding of the folded tree is remembered under beside
    ///     the text.
    /// </remarks>
    /// <param name="Omitted">The omitted declarations the text refers to, sorted by name.</param>
    /// <param name="Negated">Whether the text was folded under an odd number of negations.</param>
    internal sealed record AppliedFolding(ImmutableArray<ParameterSymbol> Omitted, bool Negated);

    /// <summary>
    ///     The declared parameters one compilation leaves out, resolved against their declarations.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A parameter left out is not a parameter given an absent value. An absent value makes
    ///         a comparison against it false for every row; a parameter left out imposes no
    ///         restriction at all, which is what a report run means by leaving an optional parameter
    ///         blank. So the names are folded out of the parse trees before anything is bound, and
    ///         none of them ever becomes a slot.
    ///     </para>
    ///     <para>
    ///         Immutable once resolved. The set is part of what a compilation is — and, for each
    ///         text that refers to one of its names, part of what that text's binding is.
    ///     </para>
    /// </remarks>
    internal sealed class OmittedParameters
    {
        /// <summary>The omitted declarations, by name ignoring case.</summary>
        private readonly FrozenDictionary<string, ParameterSymbol> _symbols;

        /// <summary>Initializes a set.</summary>
        /// <param name="declarations">The omitted declarations, sorted by name.</param>
        private OmittedParameters(ImmutableArray<ParameterSymbol> declarations)
        {
            Declarations = declarations;
            _symbols = declarations.ToFrozenDictionary(
                static declaration => declaration.Name,
                StringComparer.OrdinalIgnoreCase);

            Folder = new OmittedParameterFolder(declarations.ToFrozenDictionary(
                static declaration => declaration.Name,
                static declaration => CastRules.TargetName(declaration.Type),
                StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>The set that leaves nothing out.</summary>
        internal static OmittedParameters None { get; } = new([]);

        /// <summary>Whether nothing is left out.</summary>
        internal bool IsEmpty => Declarations.IsEmpty;

        /// <summary>
        ///     The omitted declarations, each once, under its declared spelling and sorted by name
        ///     ignoring case.
        /// </summary>
        /// <remarks>
        ///     Normalized, so two calls that leave out the same parameters are the same compilation
        ///     however each spelled or ordered the names.
        /// </remarks>
        internal ImmutableArray<ParameterSymbol> Declarations { get; }

        /// <summary>What folds these parameters out of a parse tree.</summary>
        internal OmittedParameterFolder Folder { get; }

        /// <summary>Whether a parameter is one of those left out.</summary>
        /// <param name="name">The parameter's name, matched ignoring case.</param>
        /// <returns>True when it is left out.</returns>
        internal bool Contains(string name)
        {
            return _symbols.ContainsKey(name);
        }

        /// <summary>Resolves the parameters a compilation leaves out against its declarations.</summary>
        /// <param name="options">The compilation options.</param>
        /// <param name="parameterName">The caller's parameter name, for the exception.</param>
        /// <returns>The resolved set.</returns>
        /// <exception cref="ArgumentException">
        ///     A name is null or names no declared parameter, or the declarations are not usable.
        /// </exception>
        internal static OmittedParameters Resolve(QueryCompilationOptions options, string parameterName)
        {
            // The declarations are only resolved when something is left out, which is the only time
            // they are needed here; a compilation that leaves nothing out pays nothing for this.
            return options.OmittedParameters.Count == 0
                ? None
                : Resolve(options.OmittedParameters, Symbols.From(options.Parameters), parameterName);
        }

        /// <summary>Resolves the names a caller left out against the declarations.</summary>
        /// <param name="names">The names, matched ignoring case.</param>
        /// <param name="declarations">Every declared parameter, by name.</param>
        /// <param name="parameterName">The caller's parameter name, for the exception.</param>
        /// <returns>The resolved set.</returns>
        /// <exception cref="ArgumentException">A name is null or names no declared parameter.</exception>
        /// <remarks>
        ///     Only a declared parameter can be left out: the declaration is where the type of the
        ///     absent value comes from, and a name nothing declares is a host mistake worth hearing
        ///     about at the call that made it.
        /// </remarks>
        internal static OmittedParameters Resolve(
            IReadOnlyCollection<string> names,
            FrozenDictionary<string, ParameterSymbol> declarations,
            string parameterName)
        {
            if (names.Count == 0)
            {
                return None;
            }

            SortedDictionary<string, ParameterSymbol> omitted = new(StringComparer.OrdinalIgnoreCase);
            foreach (string? name in names)
            {
                if (name is null || !declarations.TryGetValue(name, out ParameterSymbol? declared))
                {
                    throw new ArgumentException(
                        "An omitted parameter must be one of the declared parameters; '" + name
                            + "' is not declared.",
                        parameterName);
                }

                omitted[declared.Name] = declared;
            }

            return new OmittedParameters([.. omitted.Values]);
        }

        /// <summary>Folds these parameters out of a list read as values.</summary>
        /// <param name="syntax">The parsed list.</param>
        /// <param name="folding">
        ///     What was folded, for the identity of the list's binding; null when the list refers to
        ///     none of these parameters and is returned untouched.
        /// </param>
        /// <returns>The list to bind.</returns>
        internal ExpressionListSyntax FoldValues(ExpressionListSyntax syntax, out AppliedFolding? folding)
        {
            ImmutableArray<ParameterSymbol> referred = ReferredToBy(syntax);
            if (referred.IsEmpty)
            {
                folding = null;
                return syntax;
            }

            // A value list has no skeleton of its own, and the condition of an aggregation starts a
            // new one, so no negation above the list reaches anything folded in it.
            folding = new AppliedFolding(referred, Negated: false);
            return Folder.FoldValues(syntax);
        }

        /// <summary>Folds these parameters out of a text read as one predicate.</summary>
        /// <param name="syntax">The parsed text.</param>
        /// <param name="negated">Whether an odd number of negations stand above the text.</param>
        /// <param name="folding">
        ///     What was folded, for the identity of the text's binding; null when the text refers to
        ///     none of these parameters and is returned untouched.
        /// </param>
        /// <returns>The truth value the text folded to, or the list to bind.</returns>
        internal FoldedPredicateList FoldPredicate(
            ExpressionListSyntax syntax,
            bool negated,
            out AppliedFolding? folding)
        {
            ImmutableArray<ParameterSymbol> referred = ReferredToBy(syntax);
            if (referred.IsEmpty)
            {
                folding = null;
                return new FoldedPredicateList(null, syntax);
            }

            folding = new AppliedFolding(referred, negated);
            return Folder.FoldPredicate(syntax, negated);
        }

        /// <summary>The omitted declarations a parsed text refers to, sorted by name.</summary>
        /// <param name="syntax">The parsed text.</param>
        /// <returns>The declarations; empty when the text refers to none of them.</returns>
        /// <remarks>
        ///     Read off the names the parser collected, so asking costs nothing a tree walk would.
        ///     A text that refers to none is untouched by folding, and binds exactly as it would
        ///     with nothing left out.
        /// </remarks>
        internal ImmutableArray<ParameterSymbol> ReferredToBy(ExpressionListSyntax syntax)
        {
            if (IsEmpty || syntax.ReferencedParameters.Count == 0)
            {
                return [];
            }

            ImmutableArray<ParameterSymbol>.Builder referred = ImmutableArray.CreateBuilder<ParameterSymbol>();
            foreach (string name in syntax.ReferencedParameters)
            {
                if (_symbols.TryGetValue(name, out ParameterSymbol? declared))
                {
                    referred.Add(declared);
                }
            }

            return referred.ToImmutable();
        }
    }
}
