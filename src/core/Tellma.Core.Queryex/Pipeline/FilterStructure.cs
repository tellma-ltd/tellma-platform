// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Diagnostics;

namespace Tellma.Core.Queryex.Pipeline
{
    /// <summary>A navigation rebase, resolved against the schema it compiles with.</summary>
    /// <param name="Navigations">The navigation steps from the current root, in order.</param>
    /// <param name="Segments">The segment names as the caller wrote them.</param>
    /// <param name="Target">The entity the path reaches, which the criterion resolves from.</param>
    internal sealed record ResolvedVia(
        ImmutableArray<NavigationDescriptor> Navigations,
        ImmutableArray<string> Segments,
        EntityDescriptor Target);

    /// <summary>A semi-join, resolved against the schema it compiles with.</summary>
    /// <param name="Entity">The entity whose rows are searched.</param>
    /// <param name="Navigation">Its navigation to the current root.</param>
    internal sealed record ResolvedExists(EntityDescriptor Entity, NavigationDescriptor Navigation);

    /// <summary>
    ///     Checks the rebases and semi-joins of a filter tree, which a host builds rather than a user
    ///     writes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The names a <see cref="FilterTree.ViaNode" /> or a <see cref="FilterTree.ExistsNode" />
    ///         carries are host structure, like a restriction's path: no user text reaches them, so a
    ///         name that does not fit the schema is a bug in the host and is refused as a caller error
    ///         rather than reported as a diagnostic a user could do nothing about.
    ///     </para>
    ///     <para>
    ///         The whole tree is checked before anything is bound, and on every call — a cache hit
    ///         included — so whether a host is told about its mistake never depends on what the user
    ///         text beside it did or on what an earlier call left in the cache.
    ///     </para>
    /// </remarks>
    internal static class FilterStructure
    {
        /// <summary>Checks every rebase and semi-join of one clause's filter tree.</summary>
        /// <param name="tree">The tree, when the clause has one.</param>
        /// <param name="schema">The schema, or null when there is none to resolve names against.</param>
        /// <param name="root">The clause's root entity, or null when there is no schema.</param>
        /// <param name="location">The clause.</param>
        /// <param name="rowLevel">
        ///     Whether the clause filters rows. A group-level predicate refuses both constructs, which
        ///     read rows that no longer exist once they have been grouped.
        /// </param>
        /// <param name="parameterName">The caller's parameter name, for the exception.</param>
        /// <exception cref="ArgumentException">A node does not fit the schema or the clause.</exception>
        internal static void Require(
            FilterTree? tree,
            QueryexSchema? schema,
            EntityDescriptor? root,
            DiagnosticLocation location,
            bool rowLevel,
            string parameterName)
        {
            if (tree is not null)
            {
                Visit(tree, schema, root, location, rowLevel, parameterName);
            }
        }

        /// <summary>Resolves a rebase against its current root.</summary>
        /// <param name="root">The current root.</param>
        /// <param name="via">The node.</param>
        /// <param name="location">Where the node sits, for the exception.</param>
        /// <param name="parameterName">The caller's parameter name, for the exception.</param>
        /// <returns>The resolved rebase.</returns>
        /// <exception cref="ArgumentException">A segment is not a navigation.</exception>
        /// <remarks>
        ///     Each segment resolves by logical name ignoring case, like a step of a path a user
        ///     writes, because a host writes the same names a user would.
        /// </remarks>
        internal static ResolvedVia ResolveVia(
            EntityDescriptor root,
            FilterTree.ViaNode via,
            DiagnosticLocation location,
            string parameterName)
        {
            string[] segments = via.Navigation.Split('.');
            ImmutableArray<NavigationDescriptor>.Builder navigations =
                ImmutableArray.CreateBuilder<NavigationDescriptor>(segments.Length);

            EntityDescriptor entity = root;
            foreach (string segment in segments)
            {
                NavigationDescriptor navigation = entity.FindNavigation(segment)
                    ?? throw Refused(
                        "The Via at " + location.Text + " does not resolve: '" + segment
                            + "' is not a navigation of " + entity.Name + ".",
                        parameterName);

                navigations.Add(navigation);
                entity = navigation.Target;
            }

            return new ResolvedVia(navigations.MoveToImmutable(), [.. segments], entity);
        }

        /// <summary>Resolves a semi-join against its current root.</summary>
        /// <param name="schema">The schema.</param>
        /// <param name="root">The current root.</param>
        /// <param name="exists">The node.</param>
        /// <param name="location">Where the node sits, for the exception.</param>
        /// <param name="parameterName">The caller's parameter name, for the exception.</param>
        /// <returns>The resolved semi-join.</returns>
        /// <exception cref="ArgumentException">
        ///     The entity is unknown, or its navigation is missing or points at another entity.
        /// </exception>
        internal static ResolvedExists ResolveExists(
            QueryexSchema schema,
            EntityDescriptor root,
            FilterTree.ExistsNode exists,
            DiagnosticLocation location,
            string parameterName)
        {
            EntityDescriptor entity = schema.FindEntity(exists.Entity)
                ?? throw Refused(
                    "The Exists at " + location.Text + " names '" + exists.Entity
                        + "', which is not an entity of the schema.",
                    parameterName);

            NavigationDescriptor navigation = entity.FindNavigation(exists.Navigation)
                ?? throw Refused(
                    "The Exists at " + location.Text + " does not resolve: '" + exists.Navigation
                        + "' is not a navigation of " + entity.Name + ".",
                    parameterName);

            // By identity: two schema versions can both declare an entity of one name, and only the
            // descriptor itself says which one a navigation points at.
            return ReferenceEquals(navigation.Target, root)
                ? new ResolvedExists(entity, navigation)
                : throw Refused(
                    "The Exists at " + location.Text + " does not resolve: " + entity.Name + "."
                        + navigation.Name + " points at " + navigation.Target.Name + ", not at "
                        + root.Name + ".",
                    parameterName);
        }

        /// <summary>Checks one node and everything under it.</summary>
        /// <param name="tree">The node.</param>
        /// <param name="schema">The schema, or null.</param>
        /// <param name="root">The current root, or null.</param>
        /// <param name="location">Where the node sits.</param>
        /// <param name="rowLevel">Whether the clause filters rows.</param>
        /// <param name="parameterName">The caller's parameter name, for the exception.</param>
        /// <remarks>
        ///     Recursive, which the tree's construction-time depth ceiling keeps bounded.
        /// </remarks>
        private static void Visit(
            FilterTree tree,
            QueryexSchema? schema,
            EntityDescriptor? root,
            DiagnosticLocation location,
            bool rowLevel,
            string parameterName)
        {
            switch (tree)
            {
                case FilterTree.NotNode negation:
                    Visit(negation.Operand, schema, root, location.Child(FilterConnective.Not, 0), rowLevel, parameterName);
                    return;

                case FilterTree.AndNode conjunction:
                    VisitChildren(conjunction.Children, FilterConnective.And, schema, root, location, rowLevel, parameterName);
                    return;

                case FilterTree.OrNode disjunction:
                    VisitChildren(disjunction.Children, FilterConnective.Or, schema, root, location, rowLevel, parameterName);
                    return;

                case FilterTree.ViaNode via:
                    RequireRowLevel("A Via", location, rowLevel, parameterName);

                    // Without a schema nothing resolves, so the criterion is checked for the one rule
                    // that needs none; its own nodes have no root to resolve from either.
                    EntityDescriptor? target = schema is null || root is null
                        ? null
                        : ResolveVia(root, via, location, parameterName).Target;

                    Visit(via.Inner, schema, target, location.Via(via.Navigation), rowLevel, parameterName);
                    return;

                case FilterTree.ExistsNode exists:
                    RequireRowLevel("An Exists", location, rowLevel, parameterName);
                    EntityDescriptor? searched = schema is null || root is null
                        ? null
                        : ResolveExists(schema, root, exists, location, parameterName).Entity;

                    Visit(exists.Inner, schema, searched, location.Exists(exists.Entity), rowLevel, parameterName);
                    return;

                default:
                    return;
            }
        }

        /// <summary>Checks every child of a connective.</summary>
        /// <param name="children">The children.</param>
        /// <param name="connective">The connective, as a location component.</param>
        /// <param name="schema">The schema, or null.</param>
        /// <param name="root">The current root, or null.</param>
        /// <param name="location">Where the connective sits.</param>
        /// <param name="rowLevel">Whether the clause filters rows.</param>
        /// <param name="parameterName">The caller's parameter name, for the exception.</param>
        private static void VisitChildren(
            IReadOnlyList<FilterTree> children,
            FilterConnective connective,
            QueryexSchema? schema,
            EntityDescriptor? root,
            DiagnosticLocation location,
            bool rowLevel,
            string parameterName)
        {
            for (int index = 0; index < children.Count; index++)
            {
                Visit(children[index], schema, root, location.Child(connective, index), rowLevel, parameterName);
            }
        }

        /// <summary>Refuses a row-level construct in a group-level predicate.</summary>
        /// <param name="construct">The construct's name, with its article.</param>
        /// <param name="location">Where it sits.</param>
        /// <param name="rowLevel">Whether the clause filters rows.</param>
        /// <param name="parameterName">The caller's parameter name, for the exception.</param>
        /// <exception cref="ArgumentException">The clause filters groups.</exception>
        /// <remarks>
        ///     A group has no row to follow a navigation from or to correlate a search with: each
        ///     construct reads the rows that grouping has already folded away. A criterion that
        ///     restricts rows belongs in the row-level filter, which also filters the rows that enter
        ///     the groups.
        /// </remarks>
        private static void RequireRowLevel(
            string construct,
            DiagnosticLocation location,
            bool rowLevel,
            string parameterName)
        {
            if (!rowLevel)
            {
                throw Refused(
                    construct + " is row-level and belongs in the row-level filter, not in a group-level predicate; one sits at "
                        + location.Text + ".",
                    parameterName);
            }
        }

        /// <summary>The exception for a node that does not fit.</summary>
        /// <param name="message">What is wrong.</param>
        /// <param name="parameterName">The caller's parameter name.</param>
        /// <returns>The exception.</returns>
        private static ArgumentException Refused(string message, string parameterName)
        {
            return new ArgumentException(message, parameterName);
        }
    }
}
