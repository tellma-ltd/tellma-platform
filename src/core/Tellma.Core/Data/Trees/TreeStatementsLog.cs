// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Data.Trees
{
    /// <summary>The tree statements' log messages.</summary>
    internal static partial class TreeStatementsLog
    {
        /// <summary>
        ///     The verify statement repaired rows whose subtree counts drifted: a write reached the
        ///     table around the statements that keep the counts exact.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="table">The tree table.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="repaired">How many rows were repaired.</param>
        [LoggerMessage(
            EventName = "TreeVerify.Repaired",
            Level = LogLevel.Warning,
            Message = "The tree verify of {Table} in tenant {TenantId} repaired the subtree counts of {Repaired} rows: a write reached the table around the statements that keep them exact.")]
        public static partial void Repaired(ILogger logger, string table, int tenantId, int repaired);
    }
}
