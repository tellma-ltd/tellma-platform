// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     One job item's progress and checkpoint: what the job's pages show while it runs, and the state
    ///     a re-run resumes from.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each call carries a percent (0 to <see cref="JobLimits.MaxProgressPercent" />), a message
    ///         and a state, any of which may be null to keep the job's current one. The state is
    ///         serialised under the platform's JSON rules and read back through
    ///         <see cref="JobItem{TItem}.State{TState}" />.
    ///     </para>
    ///     <para>
    ///         Every member throws <see cref="ArgumentOutOfRangeException" /> for a percent above
    ///         <see cref="JobLimits.MaxProgressPercent" />, and <see cref="ArgumentException" /> for a
    ///         message whose key is longer than <see cref="JobLimits.MaxKeyLength" /> characters or whose
    ///         arguments take more than <see cref="JobLimits.MaxArgumentsLength" /> once serialised.
    ///     </para>
    /// </remarks>
    public interface IJobProgress
    {
        /// <summary>
        ///     Buffers the item's progress. The next lease renewal writes it, or an immediate one when the
        ///     percent jumped; a later report replaces an unwritten one. The state becomes current once
        ///     written.
        /// </summary>
        /// <param name="percent">The progress in percent; null to keep the current one.</param>
        /// <param name="message">The progress message; null to keep the current one.</param>
        /// <param name="state">The checkpoint; null to keep the current one.</param>
        void Report(byte? percent, JobMessage? message, object? state);

        /// <summary>
        ///     Writes every buffered report of the tenant's running jobs on this instance now, in one
        ///     lease renewal: a checkpoint before a step that must not be repeated.
        /// </summary>
        /// <param name="cancellationToken">Cancels the wait for the renewal.</param>
        /// <returns>A task that completes once the renewal ran.</returns>
        Task FlushAsync(CancellationToken cancellationToken = default);

        /// <summary>
        ///     Appends the item's progress and checkpoint to a batch the handler owns, such as one chunk's
        ///     persist batch, fenced by the job's lease: it commits with the chunk or not at all, and a
        ///     lost lease rolls the chunk back. The state becomes current only once that batch commits.
        /// </summary>
        /// <param name="batch">The handler's batch; it must still be composing.</param>
        /// <param name="percent">The progress in percent; null to keep the current one.</param>
        /// <param name="message">The progress message; null to keep the current one.</param>
        /// <param name="state">The checkpoint; null to keep the current one.</param>
        void Append(IDataBatch batch, byte? percent, JobMessage? message, object? state);
    }
}
