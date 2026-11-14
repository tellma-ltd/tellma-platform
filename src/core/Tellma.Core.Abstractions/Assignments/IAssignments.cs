// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Assignments
{
    /// <summary>
    ///     Assigns rows of assignable stacks to users, completes and withdraws assignments. A scoped
    ///     service.
    /// </summary>
    /// <remarks>
    ///     The batch forms are synchronous and append-only: each call appends one statement to the
    ///     caller's batch and returns, and an empty list appends nothing. Hooks and entity actions use
    ///     their context's assignments, a job handler or an API action passes its own batch, and code
    ///     with no batch uses the asynchronous forms. A request that breaks a rule fails the call with
    ///     <see cref="InvalidOperationException" /> before anything is appended: requests are composed by
    ///     code, so a violation is a programming error.
    /// </remarks>
    public interface IAssignments
    {
        /// <summary>Appends the assignments to the caller's batch.</summary>
        /// <param name="batch">The caller's persist batch.</param>
        /// <param name="requests">The assignments.</param>
        /// <exception cref="ArgumentNullException">An argument or a request is null.</exception>
        /// <exception cref="InvalidOperationException">A request breaks a rule.</exception>
        void Assign(IDataBatch batch, IReadOnlyList<AssignmentRequest> requests);

        /// <summary>Appends the completion of the open assignments of rows to the caller's batch.</summary>
        /// <param name="batch">The caller's persist batch.</param>
        /// <param name="resource">The rows' entity name.</param>
        /// <param name="targetIds">The rows' ids.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="InvalidOperationException">The call breaks a rule.</exception>
        void Complete(IDataBatch batch, string resource, IReadOnlyList<long> targetIds);

        /// <summary>Appends the withdrawal of the open assignments of rows to the caller's batch.</summary>
        /// <param name="batch">The caller's persist batch.</param>
        /// <param name="resource">The rows' entity name.</param>
        /// <param name="targetIds">The rows' ids.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="InvalidOperationException">The call breaks a rule.</exception>
        void Withdraw(IDataBatch batch, string resource, IReadOnlyList<long> targetIds);

        /// <summary>Assigns in a persist batch of its own on the current scope's tenant database.</summary>
        /// <param name="requests">The assignments.</param>
        /// <param name="cancellationToken">Cancels the call before the batch is sent.</param>
        /// <returns>A task that completes once the batch committed.</returns>
        /// <exception cref="ArgumentNullException">An argument or a request is null.</exception>
        /// <exception cref="InvalidOperationException">A request breaks a rule.</exception>
        Task AssignAsync(IReadOnlyList<AssignmentRequest> requests, CancellationToken cancellationToken = default);

        /// <summary>Completes the open assignments of rows in a persist batch of its own.</summary>
        /// <param name="resource">The rows' entity name.</param>
        /// <param name="targetIds">The rows' ids.</param>
        /// <param name="cancellationToken">Cancels the call before the batch is sent.</param>
        /// <returns>A task that completes once the batch committed.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="InvalidOperationException">The call breaks a rule.</exception>
        Task CompleteAsync(string resource, IReadOnlyList<long> targetIds, CancellationToken cancellationToken = default);

        /// <summary>Withdraws the open assignments of rows in a persist batch of its own.</summary>
        /// <param name="resource">The rows' entity name.</param>
        /// <param name="targetIds">The rows' ids.</param>
        /// <param name="cancellationToken">Cancels the call before the batch is sent.</param>
        /// <returns>A task that completes once the batch committed.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="InvalidOperationException">The call breaks a rule.</exception>
        Task WithdrawAsync(string resource, IReadOnlyList<long> targetIds, CancellationToken cancellationToken = default);
    }
}
