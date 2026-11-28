// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Queryex;

namespace Tellma.Core.Jobs
{
    // The self-scope is spec 0020 §11.2: a criterion alone makes the decision a filtered one (spec
    // 0014's composition rule), so every member reads, cancels and confirms the jobs they requested
    // with no role grant, and a stored grant widens each by the decision's disjunction. Retry and
    // resume are never bespoke: they act on the machinery's recovery, which a role grants.
    /// <summary>
    ///     The jobs' bespoke criteria: every user reads, cancels and confirms the jobs they requested,
    ///     so "my jobs" needs no role.
    /// </summary>
    internal sealed class JobAccessCriteria : IAccessCriteriaProvider
    {
        /// <summary>The reason key the criteria answer a "why" with.</summary>
        public const string SelfReason = "self";

        /// <summary>The filter of the jobs the caller requested, which <c>me()</c> binds.</summary>
        private static readonly FilterTree Requested = FilterTree.Leaf("RequestedById = me()");

        /// <summary>The criteria, built once: the same trees for every caller.</summary>
        private static readonly AccessCriterion[] SelfScope =
        [
            new(AccessActions.Read, Requested, SelfReason),
            new(AccessActions.Cancel, Requested, SelfReason),
            new(JobService.Confirm, Requested, SelfReason),
        ];

        /// <inheritdoc />
        public string Resource => CoreResources.Job;

        /// <inheritdoc />
        public IReadOnlyList<AccessCriterion> Criteria => SelfScope;
    }
}
