// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Jobs
{
    /// <summary>Renews the leases a tenant's claims hold on this instance.</summary>
    internal interface IJobRenewal
    {
        /// <summary>Renews the tenant's leases now, each by its own length, one renewal at a time per tenant.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="cancellationToken">Cancels the wait and the round trip.</param>
        /// <returns>A task that completes once the renewal's verdicts were applied.</returns>
        Task RenewAsync(int tenantId, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Renews the tenant's leases one last time before the host stops, every one by the same
        ///     length, which shortens a longer lease and lengthens a shorter one.
        /// </summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="leaseSeconds">The length every lease is set to, in seconds.</param>
        /// <param name="cancellationToken">Cancels the wait and the round trip.</param>
        /// <returns>The leases the renewal carried, once its verdicts were applied.</returns>
        Task<IReadOnlyList<JobLease>> RenewFinalAsync(int tenantId, int leaseSeconds, CancellationToken cancellationToken = default);
    }

    // Spec 0020 §5.2: one renewal per tenant, carrying every lease the tenant's claims hold on this
    // instance, but those whose rows are all completing and those abandoned, and their buffered
    // progress rows. A tracked row absent from what the renewal returned lost its claim's lease; a
    // row whose cancel was requested cancels its item; the carried rows leave the buffer, and
    // job.changed follows for those with a requester. A renewal that fails carries no verdict: its
    // failure reaches the caller and the leases wait for the next one. The final renewal of §6.7
    // binds every lease to the host's shutdown timeout instead of its own length, and from then on a
    // renewal a running handler's flush or jump asks for extends the lease only to that end.
    /// <summary>Renews the leases a tenant's claims hold on this instance, carrying their buffered progress.</summary>
    internal sealed class JobRenewal : IJobRenewal
    {
        /// <summary>Opens the system scope each renewal runs in.</summary>
        private readonly ITenantScopeFactory _scopes;

        /// <summary>The leases this instance holds.</summary>
        private readonly JobLeases _leases;

        /// <summary>Counts the leases found lost.</summary>
        private readonly JobsMetrics _metrics;

        /// <summary>The job machinery's logger.</summary>
        private readonly ILogger _logger;

        /// <summary>Creates the renewal.</summary>
        /// <param name="scopes">Opens the system scope each renewal runs in.</param>
        /// <param name="leases">The leases this instance holds.</param>
        /// <param name="metrics">Counts the leases found lost.</param>
        /// <param name="loggers">Creates the job machinery's logger.</param>
        public JobRenewal(ITenantScopeFactory scopes, JobLeases leases, JobsMetrics metrics, ILoggerFactory loggers)
        {
            ArgumentNullException.ThrowIfNull(scopes);
            ArgumentNullException.ThrowIfNull(leases);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(loggers);
            _scopes = scopes;
            _leases = leases;
            _metrics = metrics;
            _logger = loggers.CreateLogger(JobsLog.Category);
        }

        /// <inheritdoc />
        public async Task RenewAsync(int tenantId, CancellationToken cancellationToken = default)
        {
            _ = await GatedAsync(tenantId, leaseSeconds: null, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<JobLease>> RenewFinalAsync(int tenantId, int leaseSeconds, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(leaseSeconds, 1);
            return GatedAsync(tenantId, leaseSeconds, cancellationToken);
        }

        // Spec 0020 §5.2: a lease whose rows are all completing has nothing left to extend or judge,
        // and its completion's transaction holds those rows' locks until the handler's statements and
        // enlisted groups finish; carrying its token would make the renewal wait for them, and every
        // lease behind it with it. A completion that fails is meant to leave its lease to lapse. An
        // abandoned lease is left to lapse as well, so another claim runs its rows.
        /// <summary>The leases a renewal carries: those neither lost nor abandoned, with a row whose completion is not being sent.</summary>
        /// <param name="inFlight">The tenant's leases on this instance.</param>
        /// <returns>The leases to renew.</returns>
        internal static IReadOnlyList<JobLease> Carried(IEnumerable<JobLease> inFlight)
        {
            ArgumentNullException.ThrowIfNull(inFlight);
            return [.. inFlight.Where(static lease => !lease.IsLost && !lease.IsAbandoned && lease.Slots.Any(static slot => !slot.Completing))];
        }

        // Spec 0020 §5.2: a judged row the renewal did not return, or returned under another token,
        // was lost by its claim, so the whole lease is lost; a returned row whose cancel was requested
        // cancels its item alone. A row whose completion is being sent may already be done, so it is
        // no longer judged.
        /// <summary>Applies a renewal's verdicts to the leases it carried.</summary>
        /// <param name="leases">The leases the renewal carried.</param>
        /// <param name="renewed">The rows the renewal extended.</param>
        /// <returns>The leases found lost.</returns>
        internal static IReadOnlyList<JobLease> Judge(IReadOnlyList<JobLease> leases, IReadOnlyList<RenewedJob> renewed)
        {
            ArgumentNullException.ThrowIfNull(leases);
            ArgumentNullException.ThrowIfNull(renewed);
            var extended = renewed.ToDictionary(static row => row.Id);
            List<JobLease> lost = [];
            foreach (JobLease lease in leases)
            {
                List<JobSlot> judged = [.. lease.Slots.Where(static slot => !slot.Completing)];
                if (judged.Any(slot => !extended.TryGetValue(slot.JobId, out RenewedJob? row) || row.LeaseToken != lease.Token))
                {
                    lease.Lose();
                    lost.Add(lease);
                    continue;
                }

                foreach (JobSlot slot in judged.Where(slot => extended[slot.JobId].CancelRequestedAt is not null))
                {
                    lease.RequestCancel(slot.JobId);
                }
            }

            return lost;
        }

        /// <summary>Runs one renewal behind the tenant's gate.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="leaseSeconds">The length every lease is set to; null for each lease's own.</param>
        /// <param name="cancellationToken">Cancels the wait and the round trip.</param>
        /// <returns>The leases the renewal carried.</returns>
        private async Task<IReadOnlyList<JobLease>> GatedAsync(int tenantId, int? leaseSeconds, CancellationToken cancellationToken)
        {
            SemaphoreSlim gate = _leases.RenewalGate(tenantId);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await RenewOnceAsync(tenantId, leaseSeconds, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _ = gate.Release();
            }
        }

        /// <summary>Runs one renewal and applies its verdicts.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="leaseSeconds">The length every lease is set to; null for each lease's own.</param>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <returns>The leases the renewal carried.</returns>
        private async Task<IReadOnlyList<JobLease>> RenewOnceAsync(int tenantId, int? leaseSeconds, CancellationToken cancellationToken)
        {
            // --- What the renewal carries: the live leases with a row left to judge, and the buffered rows with the version each was taken at ---
            IReadOnlyList<JobLease> leases = Carried(_leases.InFlight(tenantId));
            if (leases.Count == 0)
            {
                return [];
            }

            // --- The final renewal binds each lease first, so a later renewal, or this one failing, never
            // lengthens it past the end the host's stop sets ---
            long sentAt = _leases.Time.GetTimestamp();
            if (leaseSeconds is int final)
            {
                foreach (JobLease lease in leases)
                {
                    lease.BindToStop(sentAt, final);
                }
            }

            List<JobLeaseList> tokens = [.. leases.Select(static lease => new JobLeaseList { Id = lease.Token, LeaseSeconds = lease.RenewalSeconds })];
            List<JobProgressList> rows = [];
            List<(JobSlot Slot, long Version, byte? Percent)> carried = [];
            foreach (JobSlot slot in leases.SelectMany(static lease => lease.Slots))
            {
                if (slot.Buffered() is (JobProgressList row, long version))
                {
                    rows.Add(row);
                    carried.Add((slot, version, row.ProgressPercent));
                }
            }

            // --- The round trip, in the system scope, committing each statement on its own; a lease
            // extended counts from before it was sent, so the next renewal is never late ---
            await using TenantScope scope = await _scopes.CreateScopeAsync(JobScopes.System(tenantId), cancellationToken: cancellationToken).ConfigureAwait(false);
            var batch = (DataBatch)scope.Services.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Maintenance);
            batch.TransactionMode = TransactionMode.None;
            BatchResult<IReadOnlyList<RenewedJob>> renewed = JobStatements.Renew(batch, tokens, rows);
            _ = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            // --- The verdicts; each lease lost is logged with the jobs it held and counted once ---
            IReadOnlyList<JobLease> lost = Judge(leases, renewed.Value);
            foreach (JobLease lease in leases.Except(lost))
            {
                lease.Renewed(sentAt);
            }

            foreach (JobLease lease in lost)
            {
                ReportLoss(_logger, _metrics, tenantId, lease);
            }

            // --- The carried rows leave the buffer, each only as far as the version it was taken at;
            // their requesters learn of the change ---
            IClientEventPublisher publisher = scope.Services.GetRequiredService<IClientEventPublisher>();
            foreach ((JobSlot slot, long version, byte? percent) in carried)
            {
                slot.Carried(version, percent, sentAt);
                if (slot.RequestedById is int requester)
                {
                    await publisher.PublishAsync(JobNotices.Changed(slot.JobId, requester)).ConfigureAwait(false);
                }
            }

            return leases;
        }

        /// <summary>Logs a lost lease with the jobs it held and counts it, once per lease however many paths find it lost.</summary>
        /// <param name="logger">The job machinery's logger.</param>
        /// <param name="metrics">The job instruments.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="lease">The lost lease.</param>
        internal static void ReportLoss(ILogger logger, JobsMetrics metrics, int tenantId, JobLease lease)
        {
            if (!lease.TryReportLoss())
            {
                return;
            }

            metrics.RecordLeaseLost(lease.Handler.Key);
            using (logger.BeginScope(new JobLogScope(tenantId, userId: null, lease.Slots.Select(static slot => slot.JobId), originTraceParent: null)))
            {
                JobsLog.JobLeaseLost(logger, lease.Handler.Key);
            }
        }
    }
}
