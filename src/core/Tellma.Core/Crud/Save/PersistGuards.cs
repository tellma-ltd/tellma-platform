// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Crud.Save
{
    // Spec 0015 §6.7 step 4 and §13.3: the access guards join a persist whose writes meet a security
    // table, whoever composed the write: the stack's own statements, a group enlisted with it at any
    // depth (its save or delete declares its target's tables), or a participant's or the caller's
    // raw statement. So the decision is taken at the invariants step, once the batch is composed,
    // over the composing stack's tables and every statement's declared writes, and before the
    // post-check. The lock is a transaction-head statement, so taking it late changes nothing of
    // its position (spec 0014 §8.3).
    /// <summary>Joins the access guards to a composed persist batch that writes a security table.</summary>
    internal static class PersistGuards
    {
        /// <summary>
        ///     Contributes the access guards' lock and then their invariants to a composed persist batch
        ///     whose writes meet a security table, outside the platform's own scope. Called once per
        ///     composition, after every write and before the post-check.
        /// </summary>
        /// <param name="guards">The scope's access guards.</param>
        /// <param name="context">The scope's request context.</param>
        /// <param name="batch">The persist batch, composed up to its post-check.</param>
        /// <param name="tables">The composing stack's tables; none for a frame that owns no stack.</param>
        /// <returns>True when the guards joined the batch.</returns>
        public static bool Contribute(IAccessGuards guards, RequestContext context, IDataBatch batch, IEnumerable<TableName> tables)
        {
            ArgumentNullException.ThrowIfNull(guards);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(tables);

            // --- The platform's own scope writes the security tables unguarded ---
            if (context.IsSystem || !StackTableSet.WritesSecurityTable(batch.WrittenTables.Concat(tables)))
            {
                return false;
            }

            // --- The lock, which the executor takes first in the transaction; then the invariants
            // after every write ---
            guards.ContributeLock(batch);
            guards.ContributeInvariants(batch);
            return true;
        }
    }
}
