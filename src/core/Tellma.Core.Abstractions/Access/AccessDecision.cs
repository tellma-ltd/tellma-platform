// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     What one caller may do with one securable, and why: the outcome, the filter of a filtered
    ///     outcome, the grants behind it and the broken permissions that would have reached it.
    /// </summary>
    /// <remarks>
    ///     Construction holds the decision to its invariants, which no <c>with</c> expression can
    ///     break: <see cref="Filter" /> is non-null exactly when <see cref="Outcome" /> is
    ///     <see cref="AccessOutcome.Filtered" />, and a denial lists no grant. The lists are copied.
    /// </remarks>
    /// <param name="Resource">The decided securable's resource.</param>
    /// <param name="Action">The decided securable's action.</param>
    /// <param name="Outcome">The outcome.</param>
    /// <param name="Filter">The rows a filtered outcome admits; null for any other outcome.</param>
    /// <param name="Grants">The grants behind the outcome; empty for a denial.</param>
    /// <param name="Problems">The broken permissions that would have reached the securable.</param>
    public sealed record AccessDecision(
        string Resource,
        string Action,
        AccessOutcome Outcome,
        FilterTree? Filter,
        IReadOnlyList<AccessGrant> Grants,
        IReadOnlyList<AccessProblem> Problems)
    {
        /// <summary>The decided securable's resource.</summary>
        public string Resource { get; } = Resource ?? throw new ArgumentNullException(nameof(Resource));

        /// <summary>The decided securable's action.</summary>
        public string Action { get; } = Action ?? throw new ArgumentNullException(nameof(Action));

        /// <summary>The outcome.</summary>
        public AccessOutcome Outcome { get; } = Enum.IsDefined(Outcome)
            ? Outcome
            : throw new ArgumentOutOfRangeException(nameof(Outcome), Outcome, "An access outcome is Denied, Filtered or Unrestricted.");

        /// <summary>The rows a filtered outcome admits; null for any other outcome.</summary>
        public FilterTree? Filter { get; } = (Outcome, Filter) switch
        {
            (AccessOutcome.Filtered, null) => throw new ArgumentException("A filtered decision carries the filter its rows must satisfy.", nameof(Filter)),
            (AccessOutcome.Filtered, _) => Filter,
            (_, null) => null,
            _ => throw new ArgumentException("Only a filtered decision carries a filter.", nameof(Filter)),
        };

        /// <summary>The grants behind the outcome; empty for a denial.</summary>
        public IReadOnlyList<AccessGrant> Grants { get; } = Outcome == AccessOutcome.Denied && Grants is { Count: > 0 }
            ? throw new ArgumentException("A denial lists no grant.", nameof(Grants))
            : AccessLists.Copy(Grants, nameof(Grants));

        /// <summary>The broken permissions that would have reached the securable.</summary>
        public IReadOnlyList<AccessProblem> Problems { get; } = AccessLists.Copy(Problems, nameof(Problems));

        /// <summary>Whether the caller may exercise the securable at all: the outcome is not <see cref="AccessOutcome.Denied" />.</summary>
        public bool IsAllowed => Outcome != AccessOutcome.Denied;

        /// <summary>A denial with no grant and no problem, as for a caller without a user or a pair the registry does not hold.</summary>
        /// <param name="resource">The securable's resource.</param>
        /// <param name="action">The securable's action.</param>
        /// <returns>The denial.</returns>
        public static AccessDecision Deny(string resource, string action)
        {
            return new AccessDecision(resource, action, AccessOutcome.Denied, null, [], []);
        }
    }
}
