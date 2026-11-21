// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Queryex;

namespace Tellma.Core.Notifications
{
    // The self-scope is spec 0021 §2.2: a criterion alone makes the decision a filtered one (spec
    // 0014's composition rule), so every member reads their own rows with no role grant, and a stored
    // grant on core.Notification × Read widens it by the decision's disjunction.
    /// <summary>
    ///     The notifications' one bespoke criterion: every user reads the rows of their own inbox, so
    ///     the notification stack's reads return the caller's rows and nobody else's.
    /// </summary>
    internal sealed class NotificationAccessCriteria : IAccessCriteriaProvider
    {
        /// <summary>The reason key the criterion answers a "why" with.</summary>
        public const string SelfReason = "self";

        /// <summary>The criterion, built once: the same tree for every caller, which <c>me()</c> binds.</summary>
        private static readonly AccessCriterion[] SelfScope = [new(AccessActions.Read, FilterTree.Leaf("UserId = me()"), SelfReason)];

        /// <inheritdoc />
        public string Resource => CoreResources.Notification;

        /// <inheritdoc />
        public IReadOnlyList<AccessCriterion> Criteria => SelfScope;
    }
}
