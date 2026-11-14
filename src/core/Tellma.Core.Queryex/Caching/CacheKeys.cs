// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Pipeline;
using Tellma.Core.Queryex.Syntax;

namespace Tellma.Core.Queryex.Caching
{
    /// <summary>
    ///     What makes a cached parse the parse of this text.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Keyed on the text itself, compared character by character, rather than on a digest of
    ///         it. A digest collides, and a collision here would hand back the parse of a different
    ///         expression — which on an access-control criterion means running someone else's rule
    ///         in place of the one that was written. The hash is only ever used to find the bucket.
    ///     </para>
    ///     <para>
    ///         The ceilings are part of the key too: a parse produced under a permissive call site's
    ///         limits must not be handed to a stricter one, which would let the stricter caller's
    ///         ceiling be bypassed by whoever went first.
    ///     </para>
    /// </remarks>
    /// <param name="Text">The expression text.</param>
    /// <param name="Limits">The ceilings it was parsed under.</param>
    internal sealed record ParseKey(string Text, QueryexLimits Limits);

    /// <summary>
    ///     What makes a cached bound tree the binding of this text.
    /// </summary>
    /// <remarks>
    ///     Everything the binding depended on, and nothing more. The schema is held by identity
    ///     rather than by any digest of what it contains, so the descriptors a cached tree points at
    ///     are the very ones the current compilation compares against — matching a grouping key to
    ///     an ordering term compares descriptors, and two structurally identical schema instances
    ///     would fail that comparison for no reason a reader could see.
    /// </remarks>
    internal sealed class BindKey : IEquatable<BindKey>
    {
        /// <summary>The hash, computed once.</summary>
        private readonly int _hash;

        /// <summary>Initializes a key.</summary>
        /// <param name="text">The expression text.</param>
        /// <param name="schema">The schema it bound against.</param>
        /// <param name="root">The entity paths resolved from.</param>
        /// <param name="mode">The position it was bound for.</param>
        /// <param name="queryexVersion">The Queryex version it bound under.</param>
        /// <param name="hasUser">Whether execution will have a signed-in user.</param>
        /// <param name="hasGroupingKeys">Whether the enclosing query groups by anything.</param>
        /// <param name="directions">Whether direction suffixes were accepted.</param>
        /// <param name="limits">The ceilings it was bound under.</param>
        /// <param name="declarations">
        ///     The declarations of the parameters this text actually names, sorted by name. The
        ///     projection matters: a parameter declared as certainly present and one declared as
        ///     possibly absent bind to trees with different guards, so sharing one entry across the
        ///     two would emit the guards of the wrong declaration.
        /// </param>
        /// <param name="omitted">
        ///     The declarations of the omitted parameters this text refers to, sorted by name; empty
        ///     when it refers to none, or when nothing is omitted. A text bound with one of its
        ///     parameters folded away is a different tree from the same text bound with it present.
        /// </param>
        /// <param name="negated">
        ///     Whether the text was folded under an odd number of negations in the tree it was
        ///     composed into. What an omitted parameter folds to depends on that parity, so the same
        ///     text under the same omissions folds to different trees on either side of a negation.
        ///     Always false for a text that refers to no omitted parameter.
        /// </param>
        internal BindKey(
            string text,
            QueryexSchema schema,
            EntityDescriptor root,
            QueryexMode mode,
            int queryexVersion,
            bool hasUser,
            bool hasGroupingKeys,
            bool directions,
            QueryexLimits limits,
            ImmutableArray<ParameterSymbol> declarations,
            ImmutableArray<ParameterSymbol> omitted,
            bool negated)
        {
            Text = text;
            Schema = schema;
            Root = root;
            Mode = mode;
            QueryexVersion = queryexVersion;
            HasUser = hasUser;
            HasGroupingKeys = hasGroupingKeys;
            Directions = directions;
            Limits = limits;
            Declarations = declarations;
            Omitted = omitted;
            Negated = negated;

            HashCode hash = default;
            hash.Add(text, StringComparer.Ordinal);
            hash.Add(schema);
            hash.Add(root);
            hash.Add(mode);
            hash.Add(queryexVersion);
            hash.Add(hasUser);
            hash.Add(hasGroupingKeys);
            hash.Add(directions);
            hash.Add(limits);
            foreach (ParameterSymbol declaration in declarations)
            {
                hash.Add(declaration.Name, StringComparer.OrdinalIgnoreCase);
                hash.Add(declaration.Type);
                hash.Add(declaration.Nullity);
            }

            // The count stands between the two lists, so a name moving from one to the other
            // hashes apart.
            hash.Add(omitted.Length);
            foreach (ParameterSymbol declaration in omitted)
            {
                hash.Add(declaration.Name, StringComparer.OrdinalIgnoreCase);
                hash.Add(declaration.Type);
                hash.Add(declaration.Nullity);
            }

            hash.Add(negated);
            _hash = hash.ToHashCode();
        }

        /// <summary>The expression text.</summary>
        internal string Text { get; }

        /// <summary>The schema it bound against.</summary>
        internal QueryexSchema Schema { get; }

        /// <summary>The Queryex version it bound under.</summary>
        internal int QueryexVersion { get; }

        /// <summary>The entity paths resolved from.</summary>
        internal EntityDescriptor Root { get; }

        /// <summary>The position it was bound for.</summary>
        internal QueryexMode Mode { get; }

        /// <summary>Whether execution will have a signed-in user.</summary>
        internal bool HasUser { get; }

        /// <summary>Whether the enclosing query groups by anything.</summary>
        internal bool HasGroupingKeys { get; }

        /// <summary>Whether direction suffixes were accepted.</summary>
        internal bool Directions { get; }

        /// <summary>The ceilings it was bound under.</summary>
        internal QueryexLimits Limits { get; }

        /// <summary>The declarations of the parameters this text names.</summary>
        internal ImmutableArray<ParameterSymbol> Declarations { get; }

        /// <summary>The declarations of the omitted parameters this text refers to.</summary>
        internal ImmutableArray<ParameterSymbol> Omitted { get; }

        /// <summary>Whether the text was folded under an odd number of negations.</summary>
        internal bool Negated { get; }

        /// <inheritdoc />
        public bool Equals(BindKey? other)
        {
            if (other is null || _hash != other._hash)
            {
                return false;
            }

            if (!ReferenceEquals(Schema, other.Schema)
                || !ReferenceEquals(Root, other.Root)
                || Mode != other.Mode
                || QueryexVersion != other.QueryexVersion
                || HasUser != other.HasUser
                || HasGroupingKeys != other.HasGroupingKeys
                || Directions != other.Directions
                || !Limits.Equals(other.Limits)
                || Negated != other.Negated
                || Declarations.Length != other.Declarations.Length
                || Omitted.Length != other.Omitted.Length)
            {
                return false;
            }

            for (int index = 0; index < Declarations.Length; index++)
            {
                if (!Declarations[index].Equals(other.Declarations[index]))
                {
                    return false;
                }
            }

            for (int index = 0; index < Omitted.Length; index++)
            {
                if (!Omitted[index].Equals(other.Omitted[index]))
                {
                    return false;
                }
            }

            // Compared last, because it is the only part that costs anything and the cheap fields
            // have already rejected almost everything by the time it is reached.
            return string.Equals(Text, other.Text, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return Equals(obj as BindKey);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return _hash;
        }

        /// <summary>The declarations a parsed expression actually names, sorted by name.</summary>
        /// <param name="syntax">The parse tree.</param>
        /// <param name="declarations">Every declaration in scope.</param>
        /// <returns>The projection.</returns>
        internal static ImmutableArray<ParameterSymbol> Project(
            SyntaxNode syntax,
            IReadOnlyDictionary<string, ParameterSymbol> declarations)
        {
            if (declarations.Count == 0)
            {
                return [];
            }

            SortedDictionary<string, ParameterSymbol> named = new(StringComparer.OrdinalIgnoreCase);
            Collect(syntax, declarations, named);
            return [.. named.Values];
        }

        /// <summary>Finds every declared parameter a parsed expression names.</summary>
        /// <param name="node">The parsed node.</param>
        /// <param name="declarations">Every declaration in scope.</param>
        /// <param name="named">The projection being built.</param>
        private static void Collect(
            SyntaxNode node,
            IReadOnlyDictionary<string, ParameterSymbol> declarations,
            SortedDictionary<string, ParameterSymbol> named)
        {
            if (node is ParameterSyntax parameter
                && declarations.TryGetValue(parameter.Name, out ParameterSymbol? symbol))
            {
                named[symbol.Name] = symbol;
            }

            foreach (SyntaxNode child in node.Children)
            {
                Collect(child, declarations, named);
            }
        }
    }

    /// <summary>
    ///     What makes a cached compilation the compilation of this query.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The schema is held by identity and everything else is rendered: the clause texts with
    ///         the shape of the filter trees they came from — their rebases and semi-joins with the
    ///         names each carries — and the version stamped on each leaf, the key-set restrictions and the undeclared columns in order, whether the query pages,
    ///         the options, the omitted parameters, and the declarations. Not which page: the paging
    ///         values travel in their own slots, so every page of a query is one compilation.
    ///         Rendered rather than digested, for the same reason a parse is keyed on its text — a
    ///         collision would run a different query than the one that was asked for.
    ///     </para>
    ///     <para>
    ///         A constant statement is remembered beside the queries, under its items and their
    ///         types in order and its options. Its rendering opens with a marker no query's can, so
    ///         the two kinds never answer for each other.
    ///     </para>
    ///     <para>
    ///         The schema has to be in here even though every clause's own key already holds it,
    ///         because a hit is served before any clause is looked at. Held by identity rather than
    ///         by version, so that a host which rebuilds its schema without renaming anything still
    ///         gets SQL against the schema it just built.
    ///     </para>
    ///     <para>
    ///         Every rendered field carries its own length. Without that, two different queries can
    ///         render to one string — an ordering clause that is absent and one that is empty are
    ///         not the same query, and they compile differently.
    ///     </para>
    /// </remarks>
    internal sealed class QueryKey : IEquatable<QueryKey>
    {
        /// <summary>The character that separates the key's fields.</summary>
        private const char Separator = '\u001f';

        /// <summary>What the rendering of a constant statement's key opens with.</summary>
        private const char ConstantMarker = 'C';

        /// <summary>The hash, computed once.</summary>
        private readonly int _hash;

        /// <summary>Initializes a key.</summary>
        /// <param name="schema">The schema the query compiled against.</param>
        /// <param name="rendered">Everything else about the query, rendered.</param>
        private QueryKey(QueryexSchema schema, string rendered)
        {
            Schema = schema;
            Rendered = rendered;
            _hash = HashCode.Combine(schema, rendered.GetHashCode(StringComparison.Ordinal));
        }

        /// <summary>The schema the query compiled against.</summary>
        internal QueryexSchema Schema { get; }

        /// <summary>Everything else about the query, rendered.</summary>
        internal string Rendered { get; }

        /// <summary>Builds the key for one compilation.</summary>
        /// <param name="spec">The query.</param>
        /// <param name="options">The compilation options.</param>
        /// <param name="omitted">The parameters the compilation leaves out, resolved.</param>
        /// <returns>The key.</returns>
        internal static QueryKey Of(QuerySpec spec, QueryCompilationOptions options, OmittedParameters omitted)
        {
            StringBuilder key = new();
            Field(key, spec.Root.Name);
            Field(key, spec.Aggregate ? "1" : "0");
            Field(key, spec.Select);
            Field(key, spec.OrderBy);

            // Whether the query pages, never which page: the values travel in the paging slots,
            // which a hit re-stamps, so every page of a query shares one compilation.
            Field(key, spec.Skip is null ? "0" : "1");
            Field(key, spec.Take is null ? "0" : "1");
            Field(key, Number(options.QueryexVersion));
            Field(key, options.HasUser ? "1" : "0");
            Field(key, Number(options.BatchOrdinal));
            Field(key, Ceilings(options.Limits));

            Render(key, spec.Filter);
            key.Append(Separator);
            Render(key, spec.Having);

            // In order, each with its count first, because both lists are emitted in order and two
            // lists that render alike must be the same list.
            key.Append(Separator);
            Field(key, Number(spec.Restrictions.Count));
            foreach (KeySetRestriction restriction in spec.Restrictions)
            {
                Field(key, restriction.Path);
                Field(key, restriction.TableSource);
            }

            Field(key, Number(spec.UndeclaredColumns.Count));
            foreach (string column in spec.UndeclaredColumns)
            {
                Field(key, column);
            }

            // The resolved set rather than the names as passed: each once, under its declared
            // spelling and in name order, so two calls leaving out the same parameters share one
            // compilation however each wrote the list.
            Field(key, Number(omitted.Declarations.Length));
            foreach (ParameterSymbol declaration in omitted.Declarations)
            {
                Field(key, declaration.Name);
            }

            foreach (QueryexParameterDeclaration declaration in options.Parameters)
            {
                key.Append(Separator);
                Field(key, declaration.Name);
                Field(key, Number((int)declaration.Type));
                key.Append(declaration.IsNotNull ? '!' : '?');
            }

            return new QueryKey(options.Schema, key.ToString());
        }

        /// <summary>Builds the key for one constant statement.</summary>
        /// <param name="items">The items, in order.</param>
        /// <param name="options">The compilation options.</param>
        /// <returns>The key.</returns>
        internal static QueryKey OfConstant(ImmutableArray<ConstantItem> items, ConstantCompilationOptions options)
        {
            // A query's rendering opens with the length of its root's name, which is a digit; this
            // opens with a letter, so no constant statement can render like a query.
            StringBuilder key = new();
            key.Append(ConstantMarker).Append(Separator);
            Field(key, Number(options.QueryexVersion));
            Field(key, options.HasUser ? "1" : "0");
            Field(key, Number(options.BatchOrdinal));
            Field(key, Ceilings(options.Limits));

            // In order, with the count first: the columns come out in the order the items went in.
            Field(key, Number(items.Length));
            foreach (ConstantItem item in items)
            {
                Field(key, item.Text);
                Field(key, Number((int)item.Type));
            }

            return new QueryKey(options.Schema, key.ToString());
        }

        /// <inheritdoc />
        public bool Equals(QueryKey? other)
        {
            return other is not null
                && _hash == other._hash
                && ReferenceEquals(Schema, other.Schema)
                && string.Equals(Rendered, other.Rendered, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return Equals(obj as QueryKey);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return _hash;
        }

        /// <summary>Appends one field, its length, and the separator after it.</summary>
        /// <param name="key">The key being built.</param>
        /// <param name="value">The field, or null.</param>
        /// <remarks>
        ///     The length is what makes the rendering reversible in the only sense that matters: two
        ///     different field lists cannot produce one string. A field that is absent is written
        ///     differently from one that is empty, because the two are different questions.
        /// </remarks>
        private static void Field(StringBuilder key, string? value)
        {
            if (value is null)
            {
                key.Append('-').Append(Separator);
                return;
            }

            key.Append(value.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(value)
                .Append(Separator);
        }

        /// <summary>Renders a number without reading any culture.</summary>
        /// <param name="value">The number, or null.</param>
        /// <returns>The rendered number, or null when there is none.</returns>
        private static string? Number(int? value)
        {
            return value?.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Renders the ceilings.</summary>
        /// <param name="limits">The ceilings.</param>
        /// <returns>The rendered ceilings.</returns>
        private static string Ceilings(QueryexLimits limits)
        {
            return string.Join(
                ',',
                limits.MaxInputLength.ToString(CultureInfo.InvariantCulture),
                limits.MaxTokens.ToString(CultureInfo.InvariantCulture),
                limits.MaxSyntaxDepth.ToString(CultureInfo.InvariantCulture),
                limits.MaxTypedNodes.ToString(CultureInfo.InvariantCulture),
                limits.MaxListItems.ToString(CultureInfo.InvariantCulture),
                limits.MaxJoins.ToString(CultureInfo.InvariantCulture),
                limits.MaxParameters.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Renders a filter tree, structure and all.</summary>
        /// <param name="key">The key being built.</param>
        /// <param name="tree">The tree, or null when there is none.</param>
        private static void Render(StringBuilder key, FilterTree? tree)
        {
            switch (tree)
            {
                case null:
                    key.Append('-');
                    return;

                case FilterTree.LeafNode leaf:

                    // The stamp is part of the leaf: two leaves with one text bind under different
                    // rules once a second version exists, and an unstamped leaf takes whatever
                    // version the compilation names, which the key holds separately.
                    key.Append('L');
                    Field(key, Number(leaf.QueryexVersion));
                    Field(key, leaf.Text);
                    return;

                case FilterTree.NotNode negation:
                    key.Append("N(");
                    Render(key, negation.Operand);
                    key.Append(')');
                    return;

                case FilterTree.AndNode conjunction:
                    RenderChildren(key, 'A', conjunction.Children);
                    return;

                case FilterTree.OrNode disjunction:
                    RenderChildren(key, 'O', disjunction.Children);
                    return;

                // The names are fields of their own, so a rebase onto one path can never render like
                // a rebase onto another, nor a semi-join like a rebase.
                case FilterTree.ViaNode via:
                    key.Append('V');
                    Field(key, via.Navigation);
                    key.Append('(');
                    Render(key, via.Inner);
                    key.Append(')');
                    return;

                case FilterTree.ExistsNode exists:
                    key.Append('E');
                    Field(key, exists.Entity);
                    Field(key, exists.Navigation);
                    key.Append('(');
                    Render(key, exists.Inner);
                    key.Append(')');
                    return;

                default:
                    key.Append('?');
                    return;
            }
        }

        /// <summary>Renders a connective and its children.</summary>
        /// <param name="key">The key being built.</param>
        /// <param name="marker">The connective's marker.</param>
        /// <param name="children">The children.</param>
        private static void RenderChildren(StringBuilder key, char marker, IReadOnlyList<FilterTree> children)
        {
            key.Append(marker).Append('(');
            for (int index = 0; index < children.Count; index++)
            {
                if (index > 0)
                {
                    key.Append(',');
                }

                Render(key, children[index]);
            }

            key.Append(')');
        }
    }
}
