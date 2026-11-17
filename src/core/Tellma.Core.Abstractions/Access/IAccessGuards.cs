// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Access
{
    // The guards are spec 0014 §8.3.
    /// <summary>
    ///     The in-transaction invariants of the security tables. The pipeline calls both members on
    ///     every persist batch that writes users, roles, memberships or permissions, except in the
    ///     platform's own scope. One per scope.
    /// </summary>
    public interface IAccessGuards
    {
        /// <summary>
        ///     Marks the batch so the executor takes the tenant's security lock right after the
        ///     transaction begins, which serialises every security write and keeps the invariants
        ///     race-free.
        /// </summary>
        /// <param name="batch">The persist batch, still composing.</param>
        void ContributeLock(IDataBatch batch);

        /// <summary>
        ///     Appends the invariants' assertions after every write of the batch and before the
        ///     row-level post-check: the last administrator, the public roles' members and the
        ///     administrator role itself.
        /// </summary>
        /// <param name="batch">The persist batch, still composing.</param>
        void ContributeInvariants(IDataBatch batch);
    }
}
