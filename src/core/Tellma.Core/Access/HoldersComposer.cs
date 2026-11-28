// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Queryex;

namespace Tellma.Core.Access
{
    // The composition is spec 0014 §5.1's IAccessHoldersResolver row: each candidate filter validated
    // as the set build does, each user's grants composed through Compose, the owner's holders added
    // through §5.5's chain, and the public roles and criteria reported once as Everyone.
    /// <summary>
    ///     Composes "who holds this securable" from what the holders' round trip read: the matching
    ///     stored permissions of active roles, the current memberships of their members, and the
    ///     public roles' audience. Pure: no I/O and no clock, so it is tested without a database.
    /// </summary>
    /// <remarks>
    ///     Every user's grants compose through <see cref="AccessComposition.Compose" />, the
    ///     composition every caller's own decision takes, along the same owner chain; so a user is a
    ///     holder exactly when a decision over the grants of the user's own memberships allows the
    ///     securable, and a user holds it at all exactly when the user is a holder or everyone does.
    /// </remarks>
    /// <param name="registry">The securable registry, which resolves each owner on the chain.</param>
    /// <param name="classifier">Classifies each stored permission, as the set build does.</param>
    internal sealed class HoldersComposer(ISecurableRegistry registry, PermissionClassifier classifier)
    {
        /// <summary>Composes the holders of a securable.</summary>
        /// <param name="securable">The registered securable.</param>
        /// <param name="rows">The matching stored permissions of active roles, public ones included, each without a window.</param>
        /// <param name="memberships">The current memberships, of the roles behind the rows, of active users other than the system user.</param>
        /// <param name="audience">The active users other than the system user, whom every public role reaches.</param>
        /// <param name="schema">The tenant's current query schema, which each filter is validated under.</param>
        /// <param name="criteriaOf">The bespoke criteria of a registered resource.</param>
        /// <returns>The composition: every holder, listed or not.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The audience is negative.</exception>
        public HoldersComposition Compose(
            SecurableDescriptor securable,
            IReadOnlyList<PermissionRow> rows,
            IReadOnlyList<HolderMembership> memberships,
            int audience,
            QueryexSchema schema,
            Func<string, IReadOnlyList<AccessCriterion>> criteriaOf)
        {
            ArgumentNullException.ThrowIfNull(securable);
            ArgumentNullException.ThrowIfNull(rows);
            ArgumentNullException.ThrowIfNull(memberships);
            ArgumentOutOfRangeException.ThrowIfNegative(audience);
            ArgumentNullException.ThrowIfNull(schema);
            ArgumentNullException.ThrowIfNull(criteriaOf);

            Chain chain = new(Levels(securable), criteriaOf, classifier.ActionResources);

            // --- Each row classified as the set build classifies it; a row that grants nothing is a problem ---
            (List<RoleAccessGrant> grants, List<AccessProblem> problems) = Classify(rows, schema, chain);

            // --- Everything any role brings: the grants and problems a user of every role would see ---
            AccessDecision every = chain.Decide(grants, problems, withCriteria: false);
            List<RoleAccessGrant> listed = [.. every.Grants.OfType<RoleAccessGrant>().Distinct()];
            listed.Sort(static (left, right) => left.RoleId != right.RoleId ? left.RoleId.CompareTo(right.RoleId) : left.PermissionId.CompareTo(right.PermissionId));
            List<AccessProblem> found = [.. every.Problems.Distinct()];
            found.Sort(static (left, right) => left.PermissionId != right.PermissionId ? left.PermissionId.CompareTo(right.PermissionId) : left.Code.CompareTo(right.Code));

            // --- Everyone: the public roles and the criteria, which reach every user alike ---
            List<RoleAccessGrant> publicGrants = [.. grants.Where(static grant => grant.IsPublic)];
            AccessOutcome everyone = chain.Decide(publicGrants, [], withCriteria: true).Outcome;
            List<BespokeAccessGrant> criteria = [.. chain.Decide([], [], withCriteria: true).Grants.OfType<BespokeAccessGrant>().Distinct()];

            // --- Each user's own memberships, composed once per distinct set of roles that grant ---
            List<AccessHolder> holders = Holders(chain, grants, memberships);

            // --- The members of each role behind the grants and the problems; a public role reaches everyone ---
            Dictionary<int, int> membersByRole = [];
            Dictionary<int, HashSet<int>> usersByRole = [];
            foreach (HolderMembership membership in memberships)
            {
                if (!usersByRole.TryGetValue(membership.RoleId, out HashSet<int>? users))
                {
                    usersByRole[membership.RoleId] = users = [];
                }

                _ = users.Add(membership.UserId);
            }

            HashSet<int> publicRoles = [.. publicGrants.Select(static grant => grant.RoleId), .. rows.Where(static row => row.IsPublic).Select(static row => row.RoleId)];
            foreach (int roleId in listed.Select(static grant => grant.RoleId).Concat(found.Select(static problem => problem.RoleId)))
            {
                membersByRole[roleId] = publicRoles.Contains(roleId) ? audience : usersByRole.GetValueOrDefault(roleId)?.Count ?? 0;
            }

            return new HoldersComposition(everyone, listed, membersByRole, holders, criteria, found);
        }

        /// <summary>The holders among the members: each user whose own memberships' grants allow the securable.</summary>
        /// <param name="chain">The securable's owner chain.</param>
        /// <param name="grants">The classified grants.</param>
        /// <param name="memberships">The current memberships.</param>
        /// <returns>The holders, by user id.</returns>
        private static List<AccessHolder> Holders(Chain chain, List<RoleAccessGrant> grants, IReadOnlyList<HolderMembership> memberships)
        {
            // --- The grants of each role that is not public; only those roles can make a holder ---
            Dictionary<int, List<RoleAccessGrant>> grantsByRole = [];
            foreach (RoleAccessGrant grant in grants)
            {
                if (!grant.IsPublic)
                {
                    if (!grantsByRole.TryGetValue(grant.RoleId, out List<RoleAccessGrant>? held))
                    {
                        grantsByRole[grant.RoleId] = held = [];
                    }

                    held.Add(grant);
                }
            }

            // --- Each user's current memberships of those roles ---
            Dictionary<int, List<HolderMembership>> byUser = [];
            foreach (HolderMembership membership in memberships)
            {
                if (grantsByRole.ContainsKey(membership.RoleId))
                {
                    if (!byUser.TryGetValue(membership.UserId, out List<HolderMembership>? own))
                    {
                        byUser[membership.UserId] = own = [];
                    }

                    own.Add(membership);
                }
            }

            // --- One composition per distinct set of roles, shared by every user who has exactly it ---
            Dictionary<string, AccessDecision> byRoles = new(StringComparer.Ordinal);
            List<AccessHolder> holders = [];
            foreach ((int userId, List<HolderMembership> own) in byUser.OrderBy(static pair => pair.Key))
            {
                int[] roles = [.. own.Select(static membership => membership.RoleId).Distinct().Order()];
                string key = string.Join(',', roles);
                if (!byRoles.TryGetValue(key, out AccessDecision? decision))
                {
                    decision = chain.Decide([.. roles.SelectMany(role => grantsByRole[role])], [], withCriteria: false);
                    byRoles[key] = decision;
                }

                if (!decision.IsAllowed)
                {
                    continue;
                }

                // --- The permissions behind the outcome, and the latest end of the memberships that bring them ---
                List<RoleAccessGrant> behind = [.. decision.Grants.OfType<RoleAccessGrant>()];
                int[] permissionIds = [.. behind.Select(static grant => grant.PermissionId).Distinct().Order()];
                HashSet<int> rolesBehind = [.. behind.Select(static grant => grant.RoleId)];
                List<DateTimeOffset?> ends = [.. own.Where(membership => rolesBehind.Contains(membership.RoleId)).Select(static membership => membership.EndsAt)];
                DateTimeOffset? endsAt = ends.Count == 0 || ends.Any(static end => end is null) ? null : ends.Max();

                holders.Add(new AccessHolder(userId, decision.Outcome, permissionIds, endsAt));
            }

            return holders;
        }

        /// <summary>
        ///     Classifies the rows that reach the chain: a grant without a window for each one that
        ///     grants, a problem for each one that grants nothing.
        /// </summary>
        /// <param name="rows">The rows.</param>
        /// <param name="schema">The tenant's query schema.</param>
        /// <param name="chain">The owner chain, whose levels a row must reach.</param>
        /// <returns>The grants and the problems, each once per permission.</returns>
        private (List<RoleAccessGrant> Grants, List<AccessProblem> Problems) Classify(IReadOnlyList<PermissionRow> rows, QueryexSchema schema, Chain chain)
        {
            Dictionary<(string Root, string Text, int Version), PermissionClassifier.Validation> validations = [];
            Dictionary<int, RoleAccessGrant> grants = [];
            Dictionary<(int PermissionId, AccessProblemCode Code), AccessProblem> problems = [];
            foreach (PermissionRow row in rows)
            {
                ArgumentNullException.ThrowIfNull(row, nameof(rows));
                PermissionClassification classification = classifier.Classify(row.Resource, row.Action, row.Filter, row.FilterQueryexVersion, row.IsPublic, schema, validations);
                if (!chain.Reaches(classification.Resource, classification.Action))
                {
                    continue;
                }

                if (classification.Problem is AccessProblemCode code)
                {
                    _ = problems.TryAdd(
                        (row.PermissionId, code),
                        new AccessProblem(row.PermissionId, row.RoleId, classification.Resource, classification.Action, code, classification.Diagnostics));
                    continue;
                }

                // The window belongs to a membership, which the users' composition reads on its own.
                _ = grants.TryAdd(row.PermissionId, new RoleAccessGrant(
                    classification.Resource,
                    classification.Action,
                    row.PermissionId,
                    row.RoleId,
                    row.RoleName ?? string.Empty,
                    row.IsPublic,
                    classification.Filter,
                    classification.FilterQueryexVersion,
                    StartsAt: null,
                    EndsAt: null));
            }

            return ([.. grants.Values], [.. problems.Values]);
        }

        /// <summary>
        ///     The securable and its owners on the same action, nearest first; the chain ends at an
        ///     owner the registry does not hold, which denies.
        /// </summary>
        /// <param name="securable">The securable.</param>
        /// <returns>The levels.</returns>
        private List<SecurableDescriptor> Levels(SecurableDescriptor securable)
        {
            // The registry validated at startup that every owner chain ends.
            List<SecurableDescriptor> levels = [securable];
            while (levels[^1].Owner is SecurableOwner owner && registry.Find(owner.Resource, securable.Action) is SecurableDescriptor next)
            {
                levels.Add(next);
            }

            return levels;
        }

        /// <summary>A securable's owner chain, and the composition of grants along it.</summary>
        /// <param name="levels">The securable and its registered owners, nearest first.</param>
        /// <param name="criteriaOf">The bespoke criteria of a registered resource.</param>
        /// <param name="actionResources">The resources that register each action.</param>
        private sealed class Chain(
            IReadOnlyList<SecurableDescriptor> levels,
            Func<string, IReadOnlyList<AccessCriterion>> criteriaOf,
            ActionResourceMap actionResources)
        {
            /// <summary>Whether a pair reaches some level of the chain, as a grant's would.</summary>
            /// <param name="resource">The pair's resource, as classified.</param>
            /// <param name="action">The pair's action, as classified.</param>
            /// <returns>True when it does.</returns>
            public bool Reaches(string resource, string action)
            {
                return levels.Any(level => AccessComposition.Matches(resource, action, level, actionResources));
            }

            /// <summary>Composes grants along the chain, each owner first, as a caller's own decision composes.</summary>
            /// <param name="grants">The grants, windows already applied.</param>
            /// <param name="problems">The broken permissions.</param>
            /// <param name="withCriteria">Whether every level's bespoke criteria join.</param>
            /// <returns>The decision on the securable.</returns>
            public AccessDecision Decide(IReadOnlyList<RoleAccessGrant> grants, IReadOnlyList<AccessProblem> problems, bool withCriteria)
            {
                return Decide(0, grants, problems, withCriteria);
            }

            /// <summary>Composes grants at one level of the chain, its owner first.</summary>
            /// <param name="level">The level.</param>
            /// <param name="grants">The grants.</param>
            /// <param name="problems">The broken permissions.</param>
            /// <param name="withCriteria">Whether the criteria join.</param>
            /// <returns>The decision at the level.</returns>
            private AccessDecision Decide(int level, IReadOnlyList<RoleAccessGrant> grants, IReadOnlyList<AccessProblem> problems, bool withCriteria)
            {
                SecurableDescriptor securable = levels[level];
                AccessDecision? owner = securable.Owner is not SecurableOwner declared
                    ? null
                    : level + 1 < levels.Count
                        ? Decide(level + 1, grants, problems, withCriteria)
                        : AccessDecision.Deny(declared.Resource, securable.Action);
                return AccessComposition.Compose(securable, grants, problems, withCriteria ? criteriaOf(securable.Resource) : [], owner, actionResources);
            }
        }
    }

    /// <summary>One current membership of a holder candidate.</summary>
    /// <param name="UserId">The member, an active user other than the system user.</param>
    /// <param name="RoleId">The role, active and not public.</param>
    /// <param name="EndsAt">When the membership ends, exclusive; null for no end.</param>
    internal sealed record HolderMembership(int UserId, int RoleId, DateTimeOffset? EndsAt);

    /// <summary>What the holders composition found, before the caller's visibility and the cap apply.</summary>
    /// <param name="Everyone">What the public roles and the criteria allow every user.</param>
    /// <param name="Grants">The grants any role brings, by role and permission.</param>
    /// <param name="MembersByRole">The current members of each role behind the grants and the problems.</param>
    /// <param name="Holders">Every holder, by user id.</param>
    /// <param name="Criteria">The bespoke criteria that reach the securable.</param>
    /// <param name="Problems">The stored permissions that would have matched but grant nothing, by permission and code.</param>
    internal sealed record HoldersComposition(
        AccessOutcome Everyone,
        IReadOnlyList<RoleAccessGrant> Grants,
        IReadOnlyDictionary<int, int> MembersByRole,
        IReadOnlyList<AccessHolder> Holders,
        IReadOnlyList<BespokeAccessGrant> Criteria,
        IReadOnlyList<AccessProblem> Problems);
}
