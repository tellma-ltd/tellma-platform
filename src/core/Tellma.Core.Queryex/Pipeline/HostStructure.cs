// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Diagnostics;
using static System.FormattableString;

namespace Tellma.Core.Queryex.Pipeline
{
    /// <summary>A key-set restriction, resolved against the schema it compiles with.</summary>
    /// <param name="Navigations">The navigations from the root to the restricted property.</param>
    /// <param name="Property">The restricted property.</param>
    /// <param name="TableSource">The host's table of keys.</param>
    internal sealed record ResolvedRestriction(
        ImmutableArray<NavigationDescriptor> Navigations,
        PropertyDescriptor Property,
        string TableSource);

    /// <summary>
    ///     Checks the parts of a query that a host builds rather than a user writes.
    /// </summary>
    /// <remarks>
    ///     A restriction's path and an undeclared column's name are host structure: no user text
    ///     reaches them, so a mistake in either is a bug in the host and is refused as a caller
    ///     error at the call that made it, rather than reported as a diagnostic a user could do
    ///     nothing about.
    /// </remarks>
    internal static class HostStructure
    {
        /// <summary>Resolves every restriction of a query.</summary>
        /// <param name="spec">The query.</param>
        /// <returns>The resolved restrictions, in the order supplied.</returns>
        /// <exception cref="ArgumentException">A restriction does not fit the schema.</exception>
        internal static ImmutableArray<ResolvedRestriction> ResolveRestrictions(QuerySpec spec)
        {
            ImmutableArray<ResolvedRestriction>.Builder resolved =
                ImmutableArray.CreateBuilder<ResolvedRestriction>(spec.Restrictions.Count);

            for (int index = 0; index < spec.Restrictions.Count; index++)
            {
                resolved.Add(Resolve(spec.Root, spec.Restrictions[index], index, nameof(spec)));
            }

            return resolved.MoveToImmutable();
        }

        /// <summary>Checks the rebases and semi-joins of a query's two predicates.</summary>
        /// <param name="spec">The query.</param>
        /// <param name="schema">The schema it compiles against.</param>
        /// <exception cref="ArgumentException">
        ///     A rebase or a semi-join does not fit the schema, or sits in the group-level predicate.
        /// </exception>
        internal static void RequireFilters(QuerySpec spec, QueryexSchema schema)
        {
            FilterStructure.Require(
                spec.Filter,
                schema,
                spec.Root,
                DiagnosticLocation.Clause(QueryexClause.Filter),
                rowLevel: true,
                nameof(spec));

            FilterStructure.Require(
                spec.Having,
                schema,
                spec.Root,
                DiagnosticLocation.Clause(QueryexClause.Having),
                rowLevel: false,
                nameof(spec));
        }

        /// <summary>Checks the rebases and semi-joins of a query under authoring.</summary>
        /// <param name="spec">The clauses written so far.</param>
        /// <param name="schema">The schema, or null when discovery runs without one.</param>
        /// <param name="root">The root, or null when discovery runs without a schema.</param>
        /// <exception cref="ArgumentException">
        ///     A rebase or a semi-join does not fit the schema, or sits in the group-level predicate.
        /// </exception>
        /// <remarks>
        ///     Without a schema nothing can be resolved, so only the rule that needs none is checked:
        ///     neither construct belongs in a group-level predicate.
        /// </remarks>
        internal static void RequireFilters(
            QueryDiscoverySpec spec,
            QueryexSchema? schema,
            EntityDescriptor? root)
        {
            FilterStructure.Require(
                spec.Filter,
                schema,
                root,
                DiagnosticLocation.Clause(QueryexClause.Filter),
                rowLevel: true,
                nameof(spec));

            FilterStructure.Require(
                spec.Having,
                schema,
                root,
                DiagnosticLocation.Clause(QueryexClause.Having),
                rowLevel: false,
                nameof(spec));
        }

        /// <summary>Checks a query's undeclared columns.</summary>
        /// <param name="spec">The query.</param>
        /// <exception cref="ArgumentException">
        ///     A name is not of the accepted form or is listed twice, or the query groups.
        /// </exception>
        internal static void RequireUndeclaredColumns(QuerySpec spec)
        {
            if (spec.UndeclaredColumns.Count == 0)
            {
                return;
            }

            if (spec.Aggregate)
            {
                // A column read as it is stored is neither a grouping key nor a measure, so a
                // grouped statement has nowhere to put it.
                throw new ArgumentException(
                    "Undeclared columns are read row by row and cannot be part of a query that groups.",
                    nameof(spec));
            }

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < spec.UndeclaredColumns.Count; index++)
            {
                string name = spec.UndeclaredColumns[index];
                if (!IsIdentifier(name))
                {
                    throw new ArgumentException(
                        Invariant($"UndeclaredColumns[{index}] must be a letter or underscore followed by letters, digits or underscores."),
                        nameof(spec));
                }

                // Ignoring case, because the backend resolves column names that way under the
                // platform's collation, and two names differing only in case would read one column
                // twice.
                if (!seen.Add(name))
                {
                    throw new ArgumentException(
                        Invariant($"UndeclaredColumns[{index}] names a column already listed."),
                        nameof(spec));
                }
            }
        }

        /// <summary>Resolves one restriction.</summary>
        /// <param name="root">The query's root.</param>
        /// <param name="restriction">The restriction.</param>
        /// <param name="index">Its position, for the exception.</param>
        /// <param name="parameterName">The caller's parameter name, for the exception.</param>
        /// <returns>The resolved restriction.</returns>
        /// <exception cref="ArgumentException">The restriction does not fit the schema.</exception>
        /// <remarks>
        ///     Resolved like a path in a select list — each segment by logical name, ignoring case —
        ///     because a host writes the same names a user would.
        /// </remarks>
        private static ResolvedRestriction Resolve(
            EntityDescriptor root,
            KeySetRestriction restriction,
            int index,
            string parameterName)
        {
            string[] segments = restriction.Path.Split('.');
            ImmutableArray<NavigationDescriptor>.Builder navigations =
                ImmutableArray.CreateBuilder<NavigationDescriptor>(segments.Length - 1);

            // Every segment but the last steps along a navigation.
            EntityDescriptor entity = root;
            for (int segment = 0; segment < segments.Length - 1; segment++)
            {
                NavigationDescriptor navigation = entity.FindNavigation(segments[segment])
                    ?? throw Unresolved(index, "does not resolve: '" + segments[segment] + "' is not a navigation of " + entity.Name, parameterName);

                navigations.Add(navigation);
                entity = navigation.Target;
            }

            // The last names the property whose values the keys are compared with.
            string last = segments[^1];
            PropertyDescriptor property = entity.FindProperty(last)
                ?? throw (entity.FindNavigation(last) is not null
                    ? Unresolved(index, "ends at a navigation rather than at a property", parameterName)
                    : Unresolved(index, "does not resolve: '" + last + "' is not a property of " + entity.Name, parameterName));

            if (!QueryexTypeFacts.IsEquatable(property.Type))
            {
                // A key set is tested for membership, which asks for equality.
                throw Unresolved(index, "ends at a property whose values cannot be compared for equality", parameterName);
            }

            return new ResolvedRestriction(navigations.MoveToImmutable(), property, restriction.TableSource);
        }

        /// <summary>The exception for a restriction that does not fit the schema.</summary>
        /// <param name="index">The restriction's position.</param>
        /// <param name="problem">What is wrong with its path.</param>
        /// <param name="parameterName">The caller's parameter name.</param>
        /// <returns>The exception.</returns>
        private static ArgumentException Unresolved(int index, string problem, string parameterName)
        {
            return new ArgumentException(
                Invariant($"The path of Restrictions[{index}] {problem}."),
                parameterName);
        }

        /// <summary>Whether a name is an identifier the backend reads without quoting rules of its own.</summary>
        /// <param name="name">The name.</param>
        /// <returns>True for a letter or underscore followed by letters, digits or underscores.</returns>
        private static bool IsIdentifier(string name)
        {
            if (name.Length == 0 || !(char.IsAsciiLetter(name[0]) || name[0] == '_'))
            {
                return false;
            }

            foreach (char character in name)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character != '_')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
