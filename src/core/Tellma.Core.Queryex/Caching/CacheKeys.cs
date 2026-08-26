// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Tellma.Core.Queryex.Binding;
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
        /// <param name="languageVersion">The language version it bound under.</param>
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
        internal BindKey(
            string text,
            QueryexSchema schema,
            EntityDescriptor root,
            QueryexMode mode,
            int languageVersion,
            bool hasUser,
            bool hasGroupingKeys,
            bool directions,
            QueryexLimits limits,
            ImmutableArray<ParameterSymbol> declarations)
        {
            Text = text;
            Schema = schema;
            Root = root;
            Mode = mode;
            LanguageVersion = languageVersion;
            HasUser = hasUser;
            HasGroupingKeys = hasGroupingKeys;
            Directions = directions;
            Limits = limits;
            Declarations = declarations;

            HashCode hash = default;
            hash.Add(text, StringComparer.Ordinal);
            hash.Add(schema);
            hash.Add(root);
            hash.Add(mode);
            hash.Add(languageVersion);
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

            _hash = hash.ToHashCode();
        }

        /// <summary>The expression text.</summary>
        internal string Text { get; }

        /// <summary>The schema it bound against.</summary>
        internal QueryexSchema Schema { get; }

        /// <summary>The language version it bound under.</summary>
        internal int LanguageVersion { get; }

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
                || HasUser != other.HasUser
                || HasGroupingKeys != other.HasGroupingKeys
                || Directions != other.Directions
                || !Limits.Equals(other.Limits)
                || Declarations.Length != other.Declarations.Length)
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
    ///         the shape of the filter trees they came from, the options, and the declarations.
    ///         Rendered rather than digested, for the same reason a parse is keyed on its text — a
    ///         collision would run a different query than the one that was asked for.
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
        /// <returns>The key.</returns>
        internal static QueryKey Of(QuerySpec spec, QueryCompilationOptions options)
        {
            StringBuilder key = new();
            Field(key, spec.Root.Name);
            Field(key, spec.Aggregate ? "1" : "0");
            Field(key, spec.Select);
            Field(key, spec.OrderBy);
            Field(key, Number(spec.Skip));
            Field(key, Number(spec.Take));
            Field(key, Number(options.LanguageVersion));
            Field(key, options.HasUser ? "1" : "0");
            Field(key, Number(options.BatchOrdinal));
            Field(key, Ceilings(options.Limits));

            Render(key, spec.Filter);
            key.Append(Separator);
            Render(key, spec.Having);

            foreach (QueryexParameterDeclaration declaration in options.Parameters)
            {
                key.Append(Separator);
                Field(key, declaration.Name);
                Field(key, Number((int)declaration.Type));
                key.Append(declaration.IsNotNull ? '!' : '?');
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
                    key.Append('L');
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
