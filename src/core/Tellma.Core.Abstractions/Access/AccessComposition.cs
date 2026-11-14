// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Runtime.CompilerServices;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Access
{
    // The composition contract is spec 0014 §5.2 (steps 2 to 5) and §5.5 (owners); the implied read
    // of a wildcard-resource grant is narrowed to the resources that register its action.
    /// <summary>
    ///     How grants compose into a decision on one securable. <see cref="UserAccess.Decide" />
    ///     composes one caller's set through it, and "who holds this" composes each user's grants
    ///     through it, so "who holds this" and "can they" never disagree.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         For a securable <c>(R, A)</c>, a grant is a candidate when its resource is <c>R</c> or
    ///         the wildcard and its action is <c>A</c>, the wildcard, or, when <c>A</c> is
    ///         <c>Read</c>, another action: a grant of another action implies reading with the same
    ///         filter, never a wider one. On the wildcard resource it implies reading only the
    ///         resources that register that action, so a grant holding an action on one resource
    ///         never reads every other. No other implication exists. Names compare ignoring case.
    ///     </para>
    ///     <para>
    ///         A securable without a filter root keeps only its unfiltered candidates: a filtered grant
    ///         never grants an action that takes no filter, and neither does a bespoke criterion or an
    ///         owner's filter. Any unfiltered candidate makes the decision unrestricted, absorbing the
    ///         filtered ones. Otherwise the filters union: one leaf per distinct filter text and
    ///         stamp, sorted ordinally by text and then by stamp, so two callers with the same grants
    ///         compile the same SQL, followed by the filter of every criterion of the action or the
    ///         wildcard, and the owner's filter rebased through the owner's navigation. Nothing to
    ///         union is a denial.
    ///     </para>
    ///     <para>
    ///         For a child securable, the owner's decision on the same action is taken first and
    ///         passed in: either side unrestricted makes the decision unrestricted, both denied
    ///         deny, and anything else is filtered by the union of the direct leaves and the owner's
    ///         filter seen through the owner's navigation. The grants and problems of both sides are
    ///         listed.
    ///     </para>
    /// </remarks>
    public static class AccessComposition
    {
        /// <summary>The rendered text of each criterion's filter, kept for as long as the tree lives.</summary>
        private static readonly ConditionalWeakTable<FilterTree, string> RenderedFilters = [];

        /// <summary>Composes a decision from grants already narrowed to the decision's time.</summary>
        /// <param name="securable">The registered securable decided; never a wildcard.</param>
        /// <param name="grants">The grants whose window contains the decision time; others are not looked at for their window.</param>
        /// <param name="problems">The broken permissions; those whose pair would have matched are listed.</param>
        /// <param name="criteria">The bespoke criteria of the securable's resource.</param>
        /// <param name="owner">
        ///     For a securable with an owner, the decision on the owner's resource and the securable's
        ///     action, taken first; null for any other securable.
        /// </param>
        /// <param name="actionResources">
        ///     The resources that register each action, from the securable registry, as a caller's set
        ///     carries them in <see cref="UserAccess.ActionResources" />.
        /// </param>
        /// <returns>The decision.</returns>
        /// <exception cref="ArgumentNullException">An argument other than the owner decision is null.</exception>
        /// <exception cref="ArgumentException">
        ///     The securable names a wildcard, a list holds a null item, or the owner decision is
        ///     missing, superfluous, or for another pair than the securable's owner and action.
        /// </exception>
        public static AccessDecision Compose(
            SecurableDescriptor securable,
            IReadOnlyList<RoleAccessGrant> grants,
            IReadOnlyList<AccessProblem> problems,
            IReadOnlyList<AccessCriterion> criteria,
            AccessDecision? owner,
            ActionResourceMap actionResources)
        {
            RequireArguments(securable, criteria, owner);
            ArgumentNullException.ThrowIfNull(actionResources);
            IReadOnlyList<RoleAccessGrant> checkedGrants = AccessLists.Copy(grants, nameof(grants));
            IReadOnlyList<AccessProblem> checkedProblems = AccessLists.Copy(problems, nameof(problems));

            return Finish(securable, Direct(securable, checkedGrants, checkedProblems, actionResources), criteria, owner);
        }

        /// <summary>Checks the arguments every composition takes.</summary>
        /// <param name="securable">The securable.</param>
        /// <param name="criteria">The criteria.</param>
        /// <param name="owner">The owner decision.</param>
        /// <exception cref="ArgumentNullException">The securable or the criteria are null.</exception>
        /// <exception cref="ArgumentException">See <see cref="Compose" />.</exception>
        internal static void RequireArguments(SecurableDescriptor securable, IReadOnlyList<AccessCriterion> criteria, AccessDecision? owner)
        {
            ArgumentNullException.ThrowIfNull(securable);
            ArgumentNullException.ThrowIfNull(criteria);

            // --- A decision is for one registered pair; a wildcard names every pair at once ---
            if (string.IsNullOrEmpty(securable.Resource) || string.IsNullOrEmpty(securable.Action)
                || securable.Resource == CoreResources.Wildcard || securable.Action == AccessActions.Wildcard)
            {
                throw new ArgumentException("A decision is taken on a registered securable, never on a wildcard.", nameof(securable));
            }

            foreach (AccessCriterion criterion in criteria)
            {
                if (criterion is null)
                {
                    throw new ArgumentException("The criteria must not contain a null item.", nameof(criteria));
                }
            }

            // --- The owner decision comes with an owner, for the owner's resource and the same action ---
            if (securable.Owner is not SecurableOwner declared)
            {
                if (owner is not null)
                {
                    throw new ArgumentException($"The securable '{securable}' has no owner, so no owner decision composes with it.", nameof(owner));
                }

                return;
            }

            if (owner is null)
            {
                throw new ArgumentException(
                    $"The securable '{securable}' has the owner '{declared.Resource}', whose decision on the same action is taken first and passed in.",
                    nameof(owner));
            }

            if (!string.Equals(owner.Resource, declared.Resource, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(owner.Action, securable.Action, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"The owner decision of '{securable}' is the decision on '{declared.Resource}:{securable.Action}', not on '{owner.Resource}:{owner.Action}'.",
                    nameof(owner));
            }
        }

        /// <summary>Whether a grant or a problem naming a pair reaches a securable.</summary>
        /// <param name="resource">The pair's resource, which may be the wildcard.</param>
        /// <param name="action">The pair's action, which may be the wildcard.</param>
        /// <param name="securable">The securable.</param>
        /// <param name="actionResources">The resources that register each action.</param>
        /// <returns>
        ///     True when the resource is the securable's or the wildcard and the action is the
        ///     securable's or the wildcard; or, when the securable's action is <c>Read</c>, when the
        ///     pair names another action on the securable's resource, or on the wildcard resource
        ///     and the securable's resource registers that action.
        /// </returns>
        internal static bool Matches(string resource, string action, SecurableDescriptor securable, ActionResourceMap actionResources)
        {
            bool wildcardResource = resource == CoreResources.Wildcard;
            if (!wildcardResource && !string.Equals(resource, securable.Resource, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // --- The action itself, or every action ---
            if (action == AccessActions.Wildcard || string.Equals(action, securable.Action, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // --- The implied read: a concrete resource registers its own actions, since a row
            // naming any other drifts; on the wildcard resource, only where the action is registered ---
            return string.Equals(securable.Action, AccessActions.Read, StringComparison.OrdinalIgnoreCase)
                && (!wildcardResource || actionResources.Registers(securable.Resource, action));
        }

        /// <summary>The system set's decision: unrestricted, through one system grant.</summary>
        /// <param name="securable">The securable.</param>
        /// <returns>The decision.</returns>
        internal static AccessDecision SystemDecision(SecurableDescriptor securable)
        {
            return new AccessDecision(
                securable.Resource,
                securable.Action,
                AccessOutcome.Unrestricted,
                null,
                [new SystemAccessGrant(securable.Resource, securable.Action)],
                []);
        }

        /// <summary>
        ///     The direct side of a decision: the candidates among the grants (steps 2 and 3), whether
        ///     one is unfiltered, their sorted distinct leaves, the problems that would have matched,
        ///     and the decision they make on their own.
        /// </summary>
        /// <param name="securable">The securable.</param>
        /// <param name="grants">The grants current at the decision time.</param>
        /// <param name="problems">The broken permissions.</param>
        /// <param name="actionResources">The resources that register each action.</param>
        /// <returns>The direct side.</returns>
        internal static DirectSide Direct(
            SecurableDescriptor securable,
            IReadOnlyList<RoleAccessGrant> grants,
            IReadOnlyList<AccessProblem> problems,
            ActionResourceMap actionResources)
        {
            bool filterable = securable.FilterRoot is not null;

            // --- Steps 2 and 3: the matching grants; a filtered one only where a filter binds ---
            List<RoleAccessGrant> candidates = [];
            bool unrestricted = false;
            foreach (RoleAccessGrant grant in grants)
            {
                if (!Matches(grant.Resource, grant.Action, securable, actionResources))
                {
                    continue;
                }

                if (grant.Filter is null)
                {
                    unrestricted = true;
                    candidates.Add(grant);
                }
                else if (filterable)
                {
                    candidates.Add(grant);
                }
            }

            // --- The broken permissions that would have matched, listed whatever the outcome ---
            List<AccessProblem> matched = [];
            foreach (AccessProblem problem in problems)
            {
                if (Matches(problem.Resource, problem.Action, securable, actionResources))
                {
                    matched.Add(problem);
                }
            }

            // --- Step 4: an unfiltered candidate absorbs every filtered one ---
            if (unrestricted)
            {
                return new DirectSide(
                    candidates,
                    true,
                    [],
                    matched,
                    new AccessDecision(securable.Resource, securable.Action, AccessOutcome.Unrestricted, null, candidates, matched));
            }

            // --- Step 5: one leaf per distinct filter and stamp, in a fixed order; none is a denial ---
            FilterTree[] leaves = Leaves(candidates);
            AccessDecision decision = leaves.Length == 0
                ? new AccessDecision(securable.Resource, securable.Action, AccessOutcome.Denied, null, [], matched)
                : new AccessDecision(securable.Resource, securable.Action, AccessOutcome.Filtered, FilterTree.Or(leaves), candidates, matched);
            return new DirectSide(candidates, false, leaves, matched, decision);
        }

        /// <summary>
        ///     Completes a decision from its direct side: the bespoke criteria of the action and the
        ///     owner's decision join it (step 5 and the owner rule).
        /// </summary>
        /// <param name="securable">The securable.</param>
        /// <param name="direct">The direct side.</param>
        /// <param name="criteria">The criteria of the securable's resource.</param>
        /// <param name="owner">The owner decision, for a securable with an owner.</param>
        /// <returns>The decision.</returns>
        internal static AccessDecision Finish(SecurableDescriptor securable, DirectSide direct, IReadOnlyList<AccessCriterion> criteria, AccessDecision? owner)
        {
            bool filterable = securable.FilterRoot is not null;

            // --- The criteria of the action or the wildcard; none binds where no filter does ---
            List<AccessCriterion> contributing = [];
            if (filterable)
            {
                foreach (AccessCriterion criterion in criteria)
                {
                    if (criterion.Action == AccessActions.Wildcard
                        || string.Equals(criterion.Action, securable.Action, StringComparison.OrdinalIgnoreCase))
                    {
                        contributing.Add(criterion);
                    }
                }
            }

            // --- Nothing beyond the direct side: its decision stands as it was composed ---
            if (owner is null && contributing.Count == 0)
            {
                return direct.Decision;
            }

            List<AccessProblem> problems = [.. direct.Problems];
            if (owner is not null)
            {
                problems.AddRange(owner.Problems);
            }

            problems = [.. problems.Distinct()];

            // --- Either side unrestricted makes the decision unrestricted, absorbing every filter ---
            if (direct.IsUnrestricted || owner is { Outcome: AccessOutcome.Unrestricted })
            {
                IEnumerable<AccessGrant> everyGrant = owner is null ? direct.Candidates : direct.Candidates.Concat(owner.Grants);
                return new AccessDecision(securable.Resource, securable.Action, AccessOutcome.Unrestricted, null, [.. everyGrant.Distinct()], problems);
            }

            // --- The union: the role leaves, then each contributing criterion, then the owner's filter ---
            List<FilterTree> leaves = [.. direct.Leaves];
            List<AccessGrant> grants = [.. direct.Candidates];
            foreach (AccessCriterion criterion in contributing)
            {
                leaves.Add(criterion.Filter);
                grants.Add(new BespokeAccessGrant(
                    securable.Resource,
                    criterion.Action,
                    RenderedFilters.GetValue(criterion.Filter, FilterTreeText.Render),
                    criterion.Reason));
            }

            if (filterable && owner is { Outcome: AccessOutcome.Filtered, Filter: FilterTree ownerFilter } && securable.Owner is SecurableOwner declared)
            {
                // The owner's filter is written against the owner's root; the navigation rebases it.
                leaves.Add(FilterTree.Via(declared.Navigation, ownerFilter));
                grants.AddRange(owner.Grants);
            }

            return leaves.Count == 0
                ? new AccessDecision(securable.Resource, securable.Action, AccessOutcome.Denied, null, [], problems)
                : new AccessDecision(securable.Resource, securable.Action, AccessOutcome.Filtered, FilterTree.Or(leaves), [.. grants.Distinct()], problems);
        }

        /// <summary>The leaves of filtered candidates: one per distinct filter text and stamp, sorted ordinally by text, then by stamp.</summary>
        /// <param name="candidates">The candidates, every one filtered.</param>
        /// <returns>The leaves, each stamped with its grant's version.</returns>
        private static FilterTree[] Leaves(List<RoleAccessGrant> candidates)
        {
            // Identical texts under one stamp across roles are one leaf, and the order never depends
            // on the order the grants arrived in, so equal grant shapes compile to identical SQL.
            HashSet<(string Text, int Version)> seen = [];
            List<(string Text, int Version)> distinct = [];
            foreach (RoleAccessGrant candidate in candidates)
            {
                // A filtered grant always carries its stamp; the grant refuses construction otherwise.
                (string Text, int Version) key = (candidate.Filter!, candidate.FilterQueryexVersion!.Value);
                if (seen.Add(key))
                {
                    distinct.Add(key);
                }
            }

            distinct.Sort(CompareLeaves);
            var leaves = new FilterTree[distinct.Count];
            for (int index = 0; index < leaves.Length; index++)
            {
                leaves[index] = FilterTree.Leaf(distinct[index].Text, distinct[index].Version);
            }

            return leaves;
        }

        /// <summary>Orders two leaves ordinally by text, then by stamp.</summary>
        /// <param name="left">One leaf's text and stamp.</param>
        /// <param name="right">The other's.</param>
        /// <returns>The order.</returns>
        private static int CompareLeaves((string Text, int Version) left, (string Text, int Version) right)
        {
            int byText = string.CompareOrdinal(left.Text, right.Text);
            return byText != 0 ? byText : left.Version.CompareTo(right.Version);
        }

        /// <summary>The direct side of a decision on one securable, which a caller's set memoises until its next window boundary.</summary>
        /// <param name="candidates">The matching grants.</param>
        /// <param name="isUnrestricted">Whether a candidate is unfiltered.</param>
        /// <param name="leaves">The sorted distinct leaves; empty when unrestricted.</param>
        /// <param name="problems">The problems that would have matched.</param>
        /// <param name="decision">The decision the direct side makes on its own.</param>
        internal sealed class DirectSide(
            IReadOnlyList<RoleAccessGrant> candidates,
            bool isUnrestricted,
            IReadOnlyList<FilterTree> leaves,
            IReadOnlyList<AccessProblem> problems,
            AccessDecision decision)
        {
            /// <summary>The matching grants.</summary>
            public IReadOnlyList<RoleAccessGrant> Candidates { get; } = candidates;

            /// <summary>Whether a candidate is unfiltered.</summary>
            public bool IsUnrestricted { get; } = isUnrestricted;

            /// <summary>The sorted distinct leaves; empty when unrestricted.</summary>
            public IReadOnlyList<FilterTree> Leaves { get; } = leaves;

            /// <summary>The problems that would have matched.</summary>
            public IReadOnlyList<AccessProblem> Problems { get; } = problems;

            /// <summary>The decision the direct side makes on its own.</summary>
            public AccessDecision Decision { get; } = decision;
        }
    }
}
