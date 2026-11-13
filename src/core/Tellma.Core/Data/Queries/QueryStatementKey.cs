// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Queryex;

namespace Tellma.Core.Data.Queries
{
    // Spec 0012 §5.4: Query, Rows and Count return the existing handle for a structurally identical
    // statement; a select that is a subset of another is not merged.
    /// <summary>
    ///     The structural identity of a query statement: its kind and every input that shapes its
    ///     text or its values, arguments by value, written as one unambiguous string; and the limits
    ///     it compiles under.
    /// </summary>
    /// <param name="Text">The canonical text of the statement's inputs.</param>
    /// <param name="Limits">The limits it compiles under.</param>
    internal sealed record QueryStatementKey(string Text, QueryexLimits Limits)
    {
        /// <summary>Starts a key of one kind of statement.</summary>
        /// <param name="kind">The kind, such as <c>rows</c>.</param>
        /// <returns>The writer.</returns>
        public static Writer For(string kind)
        {
            return new Writer().Add(kind);
        }

        /// <summary>Writes a key's inputs as length-prefixed tokens, so no two input sequences share a text.</summary>
        internal sealed class Writer
        {
            /// <summary>The text so far.</summary>
            private readonly StringBuilder _text = new();

            /// <summary>Adds a text token, or a null one.</summary>
            /// <param name="token">The token.</param>
            /// <returns>This writer.</returns>
            public Writer Add(string? token)
            {
                _ = token is null
                    ? _text.Append("~;")
                    : _text.Append(token.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(token).Append(';');
                return this;
            }

            /// <summary>Adds a number, or a null one.</summary>
            /// <param name="number">The number.</param>
            /// <returns>This writer.</returns>
            public Writer Add(int? number)
            {
                return Add(number?.ToString(CultureInfo.InvariantCulture));
            }

            /// <summary>Adds a flag.</summary>
            /// <param name="flag">The flag.</param>
            /// <returns>This writer.</returns>
            public Writer Add(bool flag)
            {
                return Add(flag ? "1" : "0");
            }

            /// <summary>Adds every input of a query spec.</summary>
            /// <param name="spec">The spec.</param>
            /// <returns>This writer.</returns>
            public Writer Add(QuerySpec spec)
            {
                ArgumentNullException.ThrowIfNull(spec);
                _ = Add(spec.Root.Name).Add(spec.Select).Add(spec.Aggregate).Add(spec.Filter).Add(spec.Having)
                    .Add(spec.OrderBy).Add(spec.Skip).Add(spec.Take).Add(spec.Restrictions);
                _ = Add(spec.UndeclaredColumns.Count);
                foreach (string column in spec.UndeclaredColumns)
                {
                    _ = Add(column);
                }

                return this;
            }

            /// <summary>Adds key-set restrictions, in order.</summary>
            /// <param name="restrictions">The restrictions.</param>
            /// <returns>This writer.</returns>
            public Writer Add(IReadOnlyList<KeySetRestriction> restrictions)
            {
                ArgumentNullException.ThrowIfNull(restrictions);
                _ = Add(restrictions.Count);
                foreach (KeySetRestriction restriction in restrictions)
                {
                    _ = Add(restriction.Path).Add(restriction.TableSource);
                }

                return this;
            }

            /// <summary>Adds a filter tree, node by node in pre-order, or a null one.</summary>
            /// <param name="tree">The tree.</param>
            /// <returns>This writer.</returns>
            public Writer Add(FilterTree? tree)
            {
                return tree switch
                {
                    null => Add((string?)null),
                    FilterTree.LeafNode leaf => Add("leaf").Add(leaf.Text).Add(leaf.QueryexVersion),
                    FilterTree.NotNode negation => Add("not").Add(negation.Operand),
                    FilterTree.AndNode conjunction => Children("and", conjunction.Children),
                    FilterTree.OrNode disjunction => Children("or", disjunction.Children),
                    FilterTree.ViaNode via => Add("via").Add(via.Navigation).Add(via.Inner),
                    FilterTree.ExistsNode exists => Add("exists").Add(exists.Entity).Add(exists.Navigation).Add(exists.Inner),
                    _ => throw new ArgumentException($"The filter node {tree.GetType().Name} is not one the key knows.", nameof(tree)),
                };
            }

            /// <summary>Adds the declared parameters in order, each with its value by value, or as omitted.</summary>
            /// <param name="arguments">The arguments, or null for none.</param>
            /// <returns>This writer.</returns>
            public Writer Add(QueryArguments? arguments)
            {
                IReadOnlyList<QueryexParameterDeclaration> declarations = arguments?.Declarations ?? [];
                _ = Add(declarations.Count);
                foreach (QueryexParameterDeclaration declaration in declarations)
                {
                    _ = Add(declaration.Name).Add(declaration.Type.ToString()).Add(declaration.IsNotNull);
                    _ = arguments!.Values.TryGetValue(declaration.Name, out object? value)
                        ? Add("value").Add(value?.GetType().FullName).Add(Invariant(value))
                        : Add("omitted");
                }

                return this;
            }

            /// <summary>The key.</summary>
            /// <param name="limits">The limits the statement compiles under.</param>
            /// <returns>The key.</returns>
            public QueryStatementKey Build(QueryexLimits limits)
            {
                return new QueryStatementKey(_text.ToString(), limits);
            }

            /// <summary>A value's invariant, round-trippable text.</summary>
            /// <param name="value">The value.</param>
            /// <returns>The text, or null for null.</returns>
            private static string? Invariant(object? value)
            {
                return value switch
                {
                    null => null,
                    DateTime moment => moment.ToString("O", CultureInfo.InvariantCulture),
                    DateTimeOffset instant => instant.ToString("O", CultureInfo.InvariantCulture),
                    DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
                    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                    _ => value.ToString(),
                };
            }

            /// <summary>Adds a connective and its children.</summary>
            /// <param name="connective">The connective's name.</param>
            /// <param name="children">The children.</param>
            /// <returns>This writer.</returns>
            private Writer Children(string connective, IReadOnlyList<FilterTree> children)
            {
                _ = Add(connective).Add(children.Count);
                foreach (FilterTree child in children)
                {
                    _ = Add(child);
                }

                return this;
            }
        }
    }
}
