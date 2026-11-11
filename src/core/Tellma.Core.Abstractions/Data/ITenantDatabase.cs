// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     The tenant database of the current unit of work: the one entry point to the data layer. A
    ///     scoped service, one per request or job scope, bound to the scope's tenant.
    /// </summary>
    /// <remarks>
    ///     <see cref="Schema" /> and <see cref="Context" /> are read from the scope's request context
    ///     when they are asked for, not when the scope is created: the statements that load the tenant
    ///     settings run on this database before the settings exist, and need neither.
    /// </remarks>
    public interface ITenantDatabase
    {
        /// <summary>The query schema for the tenant's multilingual shape.</summary>
        /// <exception cref="InvalidOperationException">
        ///     The tenant's settings are not loaded yet, or no schema provider is composed.
        /// </exception>
        QueryexSchema Schema { get; }

        /// <summary>The context values queries bind: today, now, the user and the tenant's zone.</summary>
        /// <exception cref="InvalidOperationException">The tenant's settings are not loaded yet.</exception>
        QueryexContextValues Context { get; }

        /// <summary>Creates an empty batch, which executes in one round trip.</summary>
        /// <param name="purpose">What the batch is for.</param>
        /// <returns>The batch.</returns>
        IDataBatch CreateBatch(BatchPurpose purpose);
    }
}
