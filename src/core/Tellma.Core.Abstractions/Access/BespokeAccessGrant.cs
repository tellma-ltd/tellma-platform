// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>A bespoke criterion that contributed to a filtered decision.</summary>
    /// <param name="Resource">The decided securable's resource.</param>
    /// <param name="Action">The criterion's action: the decided action, or the wildcard.</param>
    /// <param name="Filter">The criterion's filter, rendered as readable text for the "why" answer; never parsed back.</param>
    /// <param name="Reason">The reason key the criterion's provider chose, such as <c>self</c>.</param>
    public sealed record BespokeAccessGrant(string Resource, string Action, string? Filter, string Reason)
        : AccessGrant(Resource, Action)
    {
        /// <summary>The reason key the criterion's provider chose.</summary>
        public string Reason { get; } = Reason ?? throw new ArgumentNullException(nameof(Reason));
    }
}
