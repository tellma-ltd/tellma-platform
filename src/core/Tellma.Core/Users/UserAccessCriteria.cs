// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Queryex;

namespace Tellma.Core.Users
{
    // The criterion is spec 0018 §3.2's own-row read. No criterion covers Save: the admin save is
    // administrative, and self-service writes have routes of their own.
    /// <summary>
    ///     The users' bespoke criteria: every connected member reads their own user row, with no role
    ///     grant. The evaluator adds it to the stored grants' disjunction.
    /// </summary>
    public sealed class UserAccessCriteria : IAccessCriteriaProvider
    {
        /// <summary>The reason key the criterion answers "why" with.</summary>
        public const string SelfReason = "self";

        /// <summary>The one criterion, built once: it depends on the caller only through <c>me()</c>.</summary>
        private static readonly IReadOnlyList<AccessCriterion> OwnRow =
            [new AccessCriterion(AccessActions.Read, FilterTree.Leaf("Id = me()"), SelfReason)];

        /// <inheritdoc />
        public string Resource => CoreResources.User;

        /// <inheritdoc />
        public IReadOnlyList<AccessCriterion> Criteria => OwnRow;
    }
}
