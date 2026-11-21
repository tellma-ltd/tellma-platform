// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Diagnostics;

namespace Tellma.Core.Queryex.Binding
{
    /// <summary>
    ///     One leaf of a composed filter, at the place in the tree it was written.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A leaf's bound tree is cached on its text and its root, so one instance appears at
    ///         every place the same text sits under the same current root — under two rebases onto
    ///         the same entity, say, or in two semi-joins over it. Each of those places lowers onto
    ///         its own joins and is its own input to report against, so the location cannot be a
    ///         property of the shared tree. This node is that place: built per occurrence, per
    ///         compilation, and never cached.
    ///     </para>
    ///     <para>
    ///         It means exactly what its expression means. Lowering reads the location from it, so a
    ///         ceiling the leaf pushes over names the leaf rather than the whole clause.
    ///     </para>
    /// </remarks>
    internal sealed class TypedFilterLeaf : TypedExpr
    {
        /// <summary>The expression, as a list.</summary>
        private readonly TypedExpr[] _children;

        /// <summary>Initializes the node.</summary>
        /// <param name="expression">The leaf's bound expression, possibly shared with other occurrences.</param>
        /// <param name="location">The input this occurrence is.</param>
        internal TypedFilterLeaf(TypedExpr expression, DiagnosticLocation location)
            : base(
                TypedExprKind.FilterLeaf,
                expression.Type,
                expression.Span,
                Fold([expression]),
                expression.NodeCount + 1,
                expression.StructuralHash)
        {
            Expression = expression;
            Location = location;
            _children = [expression];
        }

        /// <summary>The leaf's bound expression.</summary>
        internal TypedExpr Expression { get; }

        /// <summary>The input this occurrence is.</summary>
        internal DiagnosticLocation Location { get; }

        /// <inheritdoc />
        internal override IReadOnlyList<TypedExpr> Children => _children;
    }

    /// <summary>
    ///     A filter criterion rebased onto the row a to-one navigation path reaches.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Built from a filter tree's structure rather than from any text, so it never comes out
    ///         of the bound-expression cache: each compilation builds its own. Its criterion was bound
    ///         with the path's target as its root, so every path inside it is relative to that target
    ///         and so is every nullity the analysis concluded about it.
    ///     </para>
    ///     <para>
    ///         That is sound exactly where the target row exists, which is why lowering puts a presence
    ///         test in front of the criterion whenever the path may reach nothing.
    ///     </para>
    /// </remarks>
    internal sealed class TypedVia : TypedExpr
    {
        /// <summary>The criterion, as a list.</summary>
        private readonly TypedExpr[] _children;

        /// <summary>Initializes the node.</summary>
        /// <param name="navigations">The navigation steps from the current root, in order.</param>
        /// <param name="segments">The segment names as the caller wrote them.</param>
        /// <param name="inner">The criterion, bound against the path's target.</param>
        /// <param name="location">The input the criterion is, which the path's joins are written in.</param>
        internal TypedVia(
            ImmutableArray<NavigationDescriptor> navigations,
            ImmutableArray<string> segments,
            TypedExpr inner,
            DiagnosticLocation location)
            : base(
                TypedExprKind.Via,
                BoundType.Bool,
                default,
                Fold([inner]) | TypedExprFlags.ContainsPath,
                inner.NodeCount + 1,
                HashVia(navigations, inner))
        {
            Navigations = navigations;
            Segments = segments;
            Inner = inner;
            Location = location;
            _children = [inner];
        }

        /// <summary>The input the criterion is, which the path's joins are written in.</summary>
        internal DiagnosticLocation Location { get; }

        /// <summary>The navigation steps from the current root, in order.</summary>
        internal ImmutableArray<NavigationDescriptor> Navigations { get; }

        /// <summary>The segment names as the caller wrote them, for prefixing reported paths.</summary>
        internal ImmutableArray<string> Segments { get; }

        /// <summary>The criterion, bound against the path's target.</summary>
        internal TypedExpr Inner { get; }

        /// <inheritdoc />
        internal override IReadOnlyList<TypedExpr> Children => _children;

        /// <summary>Hashes the node by the route it takes and the criterion it carries.</summary>
        /// <param name="navigations">The navigation steps.</param>
        /// <param name="inner">The criterion.</param>
        /// <returns>The hash.</returns>
        private static int HashVia(ImmutableArray<NavigationDescriptor> navigations, TypedExpr inner)
        {
            HashCode hash = default;
            hash.Add((int)TypedExprKind.Via);
            foreach (NavigationDescriptor navigation in navigations)
            {
                hash.Add(navigation.Name, StringComparer.OrdinalIgnoreCase);
            }

            hash.Add(inner.StructuralHash);
            return hash.ToHashCode();
        }
    }

    /// <summary>
    ///     A semi-join: some row of another entity points at the current row and satisfies a
    ///     criterion.
    /// </summary>
    /// <remarks>
    ///     Built from a filter tree's structure rather than from any text, per occurrence and per
    ///     compilation, like <see cref="TypedVia" />.
    ///     Its criterion was bound with the searched entity as its root, so it is relative to that
    ///     entity's rows, every one of which exists — which is what lets the criterion's nullity be
    ///     taken at its word inside the subquery.
    /// </remarks>
    internal sealed class TypedExists : TypedExpr
    {
        /// <summary>The criterion, as a list.</summary>
        private readonly TypedExpr[] _children;

        /// <summary>Initializes the node.</summary>
        /// <param name="entity">The entity whose rows are searched.</param>
        /// <param name="navigation">Its navigation to the current root.</param>
        /// <param name="inner">The criterion, bound against <paramref name="entity" />.</param>
        /// <param name="location">The input the criterion is, which the subquery's source is written in.</param>
        internal TypedExists(
            EntityDescriptor entity,
            NavigationDescriptor navigation,
            TypedExpr inner,
            DiagnosticLocation location)
            : base(
                TypedExprKind.Exists,
                BoundType.Bool,
                default,
                Fold([inner]) | TypedExprFlags.ContainsPath,
                inner.NodeCount + 1,
                HashCode.Combine(
                    TypedExprKind.Exists,
                    entity.Name.ToUpperInvariant(),
                    navigation.Name.ToUpperInvariant(),
                    inner.StructuralHash))
        {
            Entity = entity;
            Navigation = navigation;
            Inner = inner;
            Location = location;
            _children = [inner];
        }

        /// <summary>The input the criterion is, which the subquery's source is written in.</summary>
        internal DiagnosticLocation Location { get; }

        /// <summary>The entity whose rows are searched.</summary>
        internal EntityDescriptor Entity { get; }

        /// <summary>The navigation of <see cref="Entity" /> that points at the current root.</summary>
        internal NavigationDescriptor Navigation { get; }

        /// <summary>The criterion, bound against <see cref="Entity" />.</summary>
        internal TypedExpr Inner { get; }

        /// <inheritdoc />
        internal override IReadOnlyList<TypedExpr> Children => _children;
    }
}
