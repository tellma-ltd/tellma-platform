// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Tenancy
{
    /// <summary>Which of the three writers wrote a membership hint; the metric's <c>source</c> tag.</summary>
    internal enum TenantMembershipWriteSource
    {
        /// <summary>A user save's effect after it committed: an invite, an activation or a deactivation.</summary>
        PostCommit,

        /// <summary>The periodic reconciliation, which rebuilds a tenant's hints from its users.</summary>
        Reconcile,

        /// <summary>The migrator, recording the bootstrap administrator's hint.</summary>
        Provision,
    }
}
