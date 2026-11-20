// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Data.Batches;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §6.3 and §7.4: tick step 1 rides the poll's round trip after the claims, when the
    // worker runs and the instance is not stopping; its leased schedules go to step 2 once the round
    // trip returned. On a read-only tenant it leases only the schedules of the keys whose handlers run
    // when the tenant is read-only. A tick that returned its full lease of rows polls again at once.
    /// <summary>The scheduler's tick, as the poll round trip carries it.</summary>
    internal interface IScheduleTick
    {
        /// <summary>Appends tick step 1 to a poll's batch.</summary>
        /// <param name="batch">The poll's batch: a maintenance batch that commits each statement on its own.</param>
        /// <param name="context">The tenant and what the tick may lease.</param>
        /// <returns>The tick's round, read once the batch executed.</returns>
        IScheduleTickRound Append(DataBatch batch, ScheduleTickContext context);
    }

    /// <summary>One tick that rode a poll's round trip.</summary>
    internal interface IScheduleTickRound
    {
        /// <summary>The schedules step 1 leased; read once the batch executed.</summary>
        int Leased { get; }

        /// <summary>Whether step 1 leased as many schedules as it may, so more may be due.</summary>
        bool Full { get; }

        /// <summary>
        ///     Runs step 2 over what step 1 leased, after the poll's round trip, in the poll's system
        ///     scope; a failure is the step's own to log, and never fails the poll.
        /// </summary>
        /// <param name="services">The poll's system scope's services.</param>
        /// <param name="cancellationToken">Cancels the step.</param>
        /// <returns>A task that completes when the step ended.</returns>
        Task RunStep2Async(IServiceProvider services, CancellationToken cancellationToken);
    }

    /// <summary>What one tick may lease.</summary>
    /// <param name="TenantId">The tenant.</param>
    /// <param name="ReadOnly">Whether the tenant is read-only, so the tick leases only the schedules of <paramref name="ReadOnlyKeys" />.</param>
    /// <param name="ReadOnlyKeys">The keys whose handlers run when the tenant is read-only.</param>
    internal sealed record ScheduleTickContext(int TenantId, bool ReadOnly, IReadOnlyList<string> ReadOnlyKeys);
}
