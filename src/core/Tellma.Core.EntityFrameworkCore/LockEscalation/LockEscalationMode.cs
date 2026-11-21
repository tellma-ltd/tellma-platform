// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.EntityFrameworkCore.LockEscalation
{
    /// <summary>
    ///     A table's SQL Server <c>LOCK_ESCALATION</c> option: whether, and to what, the engine may
    ///     escalate the many row or page locks one statement takes on the table.
    /// </summary>
    public enum LockEscalationMode
    {
        /// <summary>Escalates to a table lock, SQL Server's default (<c>TABLE</c>).</summary>
        Table,

        /// <summary>Escalates to a partition lock on a partitioned table, else to a table lock (<c>AUTO</c>).</summary>
        Auto,

        /// <summary>
        ///     Never escalates (<c>DISABLE</c>), so a statement touching many rows of a hot table keeps
        ///     row locks and never blocks the statements working on other rows.
        /// </summary>
        Disable,
    }
}
