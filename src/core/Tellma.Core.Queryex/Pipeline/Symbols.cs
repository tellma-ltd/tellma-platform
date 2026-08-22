// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Queryex.Binding;

namespace Tellma.Core.Queryex.Pipeline
{
    /// <summary>Turns declared parameters into what the binder resolves names against.</summary>
    internal static class Symbols
    {
        /// <summary>Builds the lookup for a set of declarations.</summary>
        /// <param name="declarations">The declarations.</param>
        /// <returns>The symbols, by name.</returns>
        /// <exception cref="ArgumentException">
        ///     Two declarations share a name, or one declares a type no value can be bound as.
        /// </exception>
        internal static FrozenDictionary<string, ParameterSymbol> From(
            IReadOnlyList<QueryexParameterDeclaration> declarations)
        {
            if (declarations.Count == 0)
            {
                return FrozenDictionary<string, ParameterSymbol>.Empty;
            }

            Dictionary<string, ParameterSymbol> symbols = new(StringComparer.OrdinalIgnoreCase);
            foreach (QueryexParameterDeclaration declaration in declarations)
            {
                ArgumentNullException.ThrowIfNull(declaration);
                RequireName(declaration.Name);

                if (declaration.Type is QueryexType.QxHierarchyId or QueryexType.QxGeography)
                {
                    // Neither has a form a value can be bound in, so a declaration of one could
                    // never be satisfied at execution. Caught here rather than at the first use, so
                    // the caller learns about it whether or not the expression happens to use it.
                    throw new ArgumentException(
                        "A parameter cannot be declared with a type that has no bound representation.",
                        nameof(declarations));
                }

                ParameterSymbol symbol = new(
                    declaration.Name,
                    BoundTypes.FromPublic(declaration.Type),
                    declaration.IsNotNull ? QueryexNullity.NotNull : QueryexNullity.Nullable);

                if (!symbols.TryAdd(declaration.Name, symbol))
                {
                    throw new ArgumentException(
                        "A parameter must not be declared more than once.",
                        nameof(declarations));
                }
            }

            return symbols.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Rejects a declared name that could never be written in an expression.</summary>
        /// <param name="name">The declared name.</param>
        /// <exception cref="ArgumentException">It could not.</exception>
        /// <remarks>
        ///     Checked for the same reason every other name is: a declaration nothing can refer to is
        ///     a host mistake worth hearing about at once. It matters twice over here, because the
        ///     name is part of what a compilation is remembered under.
        /// </remarks>
        private static void RequireName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A declared parameter must be named.", nameof(name));
            }

            // The same rule every logical name follows: a bracketed reference escapes every reserved
            // word and every punctuation mark, but it cannot carry its own closing bracket, and a
            // character no diagnostic could render is a name nobody could act on.
            QueryexSchemaBuilder.ValidateLogicalName(name, nameof(name));
        }
    }
}
