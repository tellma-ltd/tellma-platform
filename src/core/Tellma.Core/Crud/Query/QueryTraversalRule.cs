// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Queryex;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Crud.Query
{
    // The rule is spec 0015 §5.2 step 4; the child surfaces' owner path is spec 0014 §5.5; the
    // step-up of a sensitive read is spec 0014 §4.3, held to a traversal as to a direct read.
    /// <summary>
    ///     The navigation-traversal rule: a path into an entity other than the root reads every
    ///     column of it only under an unrestricted read decision, and otherwise only its related
    ///     projection. From a child surface's root without a grant of its own, the owner reached
    ///     through the owner navigation counts as the root, since every row already belongs to an
    ///     owner row the caller may read. When no path is refused, a path that reads a sensitive
    ///     entity beyond its projection asks for the step-up a direct read of it asks for, so no
    ///     consumer of the rule can skip it. Scoped, over the tenant's schema.
    /// </summary>
    /// <remarks>
    ///     Only the caller's text is checked: the discovery it reads covers the caller's clauses,
    ///     never the trusted trees beside them (the named filters, the search, the access filter).
    ///     The caller's text cannot write a semi-join; only a trusted tree holds one.
    /// </remarks>
    /// <param name="database">The tenant's database.</param>
    /// <param name="surfaces">The surfaces and the traversal facts.</param>
    /// <param name="securables">The registered securables, which say which reads are sensitive.</param>
    /// <param name="accessor">The scope's request context, whose assurance a sensitive read is held to.</param>
    /// <param name="stepUp">The assurance bar of the sensitive securables.</param>
    /// <param name="time">The clock the assurance's age is measured by.</param>
    internal sealed class QueryTraversalRule(
        ITenantDatabase database,
        QuerySurfaces surfaces,
        ISecurableRegistry securables,
        IRequestContextAccessor accessor,
        StepUpBar stepUp,
        TimeProvider time) : IQueryTraversalRule
    {
        /// <inheritdoc />
        public IReadOnlyList<TraversalRefusal> Check(string root, DiscoveryResult discovery, Func<string, AccessDecision> decisions)
        {
            return Check(database.Schema, root, discovery, decisions);
        }

        /// <summary>
        ///     Checks every path a query's text traverses, in a given schema, then, when none is
        ///     refused, asks for the step-up of each sensitive entity a path reads beyond its
        ///     projection.
        /// </summary>
        /// <param name="schema">The schema the paths resolve in.</param>
        /// <param name="root">The query's root entity name.</param>
        /// <param name="discovery">What discovering the query's text found.</param>
        /// <param name="decisions">The caller's read decision per entity name.</param>
        /// <returns>One refusal per path the decisions do not admit, in discovery order.</returns>
        /// <exception cref="ArgumentException">The schema has no entity named <paramref name="root" />.</exception>
        /// <exception cref="StepUpRequiredException">No path is refused, and one reads a sensitive entity beyond its projection below the step-up bar.</exception>
        /// <exception cref="Abstractions.Errors.IndividualRequiredException">The same, for a service account, which cannot step up.</exception>
        internal IReadOnlyList<TraversalRefusal> Check(QueryexSchema schema, string root, DiscoveryResult discovery, Func<string, AccessDecision> decisions)
        {
            ArgumentNullException.ThrowIfNull(schema);
            ArgumentException.ThrowIfNullOrWhiteSpace(root);
            ArgumentNullException.ThrowIfNull(discovery);
            ArgumentNullException.ThrowIfNull(decisions);
            EntityDescriptor entity = schema.FindEntity(root)
                ?? throw new ArgumentException($"The tenant's schema has no entity named '{root}'.", nameof(root));

            // --- Every path walked: its refusal, or the entities it reads beyond their projection ---
            List<TraversalRefusal> refusals = [];
            HashSet<string> fullReads = new(StringComparer.OrdinalIgnoreCase);
            foreach (PathUse path in discovery.Paths)
            {
                if (Refuser(entity, path.Segments, decisions, fullReads) is string refuser)
                {
                    refusals.Add(new TraversalRefusal(refuser, path));
                }
            }

            // --- The step-up, as a direct read asks for it: only once nothing is refused, and never of the platform's own work ---
            RequestContext context = accessor.Current;
            if (refusals.Count == 0 && !context.IsSystem)
            {
                foreach (string read in fullReads)
                {
                    if (securables.Find(read, AccessActions.Read) is { IsSensitive: true })
                    {
                        AssuranceCheck.Ensure(context, stepUp.Requirement, time.GetUtcNow());
                    }
                }
            }

            return refusals;
        }

        /// <summary>
        ///     Walks one path from the root: each segment read past the root must be in its entity's
        ///     related projection, or be read on a root-like owner, or be admitted by the caller's
        ///     unrestricted decision on its entity.
        /// </summary>
        /// <param name="root">The root.</param>
        /// <param name="segments">The path's segments, from the root.</param>
        /// <param name="decisions">The caller's read decision per entity name.</param>
        /// <param name="fullReads">Receives each entity past the root read beyond its projection.</param>
        /// <returns>The name of the entity that refuses the path; null when the path passes.</returns>
        private string? Refuser(EntityDescriptor root, IReadOnlyList<string> segments, Func<string, AccessDecision> decisions, HashSet<string> fullReads)
        {
            EntityDescriptor at = root;
            bool rootLike = true;
            for (int index = 0; index < segments.Count; index++)
            {
                // A segment is a hop when a navigation has its name; the last one is usually a column.
                NavigationDescriptor? navigation = SchemaLookup.Navigation(at, segments[index]);

                // --- Past the root, beyond the projection: the owner of a root-like child, or the entity's own unrestricted read ---
                if (index > 0 && !Projects(at, segments[index], navigation))
                {
                    if (!rootLike && decisions(at.Name).Outcome != AccessOutcome.Unrestricted)
                    {
                        return at.Name;
                    }

                    _ = fullReads.Add(at.Name);
                }

                if (navigation is null)
                {
                    break;
                }

                // The owner of a root-like child surface without a grant of its own is root-like too.
                rootLike = rootLike
                    && Owner(at, decisions) is string owner
                    && string.Equals(owner, navigation.Name, StringComparison.OrdinalIgnoreCase);
                at = navigation.Target;
            }

            return null;
        }

        /// <summary>
        ///     Whether a segment is in an entity's related projection, which a path may read whatever
        ///     the caller's decision on the entity.
        /// </summary>
        /// <param name="entity">The entity the segment is read on.</param>
        /// <param name="segment">The segment.</param>
        /// <param name="navigation">The navigation the segment names; null for a column.</param>
        /// <returns>Whether the projection holds the segment.</returns>
        private bool Projects(EntityDescriptor entity, string segment, NavigationDescriptor? navigation)
        {
            // A navigation in the projection is admitted by its own name or by its foreign key's.
            IReadOnlySet<string>? projection = surfaces.Facts(entity.Name)?.RelatedSelect;
            return projection is not null
                && (projection.Contains(segment) || (navigation is not null && projection.Contains(navigation.ForeignKey.Name)));
        }

        /// <summary>
        ///     The owner navigation of a child surface whose read decision carries no grant of its
        ///     own: a wildcard grant or one on the child itself is its own, while a grant inherited
        ///     from the owner is not.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <param name="decisions">The caller's read decision per entity name.</param>
        /// <returns>The owner navigation's name; null when the entity is no child surface or has a grant of its own.</returns>
        private string? Owner(EntityDescriptor entity, Func<string, AccessDecision> decisions)
        {
            if (surfaces.Facts(entity.Name)?.OwnerNavigation is not string owner)
            {
                return null;
            }

            bool direct = decisions(entity.Name).Grants.Any(grant =>
                string.Equals(grant.Resource, entity.Name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(grant.Resource, CoreResources.Wildcard, StringComparison.Ordinal));
            return direct ? null : owner;
        }
    }
}
