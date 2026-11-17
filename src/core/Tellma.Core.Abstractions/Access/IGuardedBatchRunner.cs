// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Abstractions.Access
{
    // The runner is spec 0014 §7.1 and §7.6.
    /// <summary>
    ///     Runs a batch on the scope's caller's behalf, under the connect prologue's guard: the only
    ///     way a service executes a batch for a caller. One per scope.
    /// </summary>
    /// <remarks>
    ///     A batch whose guard fails because the caller's permissions or, on a validation or persist
    ///     batch, the tenant's settings moved is recomposed once from the fresh state the failed round
    ///     trip returned; a persist whose in-transaction re-check finds a moved tag is recomposed once
    ///     after a cold re-connect. A second failure in one call surfaces as
    ///     <see cref="StaleContextException" />.
    /// </remarks>
    public interface IGuardedBatchRunner
    {
        /// <summary>Connects, composes a batch for the purpose, executes it, and reads its outcome.</summary>
        /// <typeparam name="TResult">What the call returns.</typeparam>
        /// <param name="purpose">The batch's purpose; never <see cref="BatchPurpose.Maintenance" />.</param>
        /// <param name="compose">
        ///     Appends the batch's statements for the connected user; it performs no I/O of its own and
        ///     may run twice, once per composition.
        /// </param>
        /// <param name="read">Reads the executed batch's results for the connected user it was composed for.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>What <paramref name="read" /> returned.</returns>
        /// <exception cref="TenantNotFoundException">The caller is no longer an active user of the tenant.</exception>
        /// <exception cref="StaleContextException">The context moved again after the batch was recomposed once.</exception>
        Task<TResult> RunAsync<TResult>(
            BatchPurpose purpose,
            Func<ConnectedUser, IDataBatch, ValueTask> compose,
            Func<ConnectedUser, BatchOutcome, TResult> read,
            CancellationToken cancellationToken = default);
    }
}
