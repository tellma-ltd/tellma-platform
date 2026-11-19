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
    // Spec 0020 §5.2: one renewal per tenant, carrying every lease the tenant's claims hold on this
    // instance, but those whose rows are all completing, and their buffered progress rows. A tracked
    // row absent from what the renewal returned
    // lost its claim's lease; a row whose cancel was requested cancels its item; the carried rows
    // leave the buffer, and job.changed follows for those with a requester. A renewal that fails
    // carries no verdict: its failure reaches the caller and the leases wait for the next one.
    /// <summary>Renews the leases a tenant's claims hold on this instance, carrying their buffered progress.</summary>
    internal sealed class JobRenewal
    {
        /// <summary>Opens the system scope each renewal runs in.</summary>
        private readonly ITenantScopeFactory _scopes;

        /// <summary>The leases this instance holds.</summary>
        private readonly JobLeases _leases;

        /// <summary>The job machinery's logger.</summary>
        private readonly ILogger _logger;

        /// <summary>Creates the renewal.</summary>
        /// <param name="scopes">Opens the system scope each renewal runs in.</param>
        /// <param name="leases">The leases this instance holds.</param>
        /// <param name="loggers">Creates the job machinery's logger.</param>
        public JobRenewal(ITenantScopeFactory scopes, JobLeases leases, ILoggerFactory loggers)
        {
            ArgumentNullException.ThrowIfNull(scopes);
            ArgumentNullException.ThrowIfNull(leases);
            ArgumentNullException.ThrowIfNull(loggers);
            _scopes = scopes;
            _leases = leases;
            _logger = loggers.CreateLogger(JobsLog.Category);
        }

        /// <summary>Renews the tenant's leases now, one renewal at a time per tenant.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="cancellationToken">Cancels the wait and the round trip.</param>
        /// <returns>A task that completes once the renewal's verdicts were applied.</returns>
        public async Task RenewAsync(int tenantId, CancellationToken cancellationToken = default)
        {
            SemaphoreSlim gate = _leases.RenewalGate(tenantId);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await RenewOnceAsync(tenantId, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _ = gate.Release();
            }
        }

        /// <summary>Runs one renewal and applies its verdicts.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <returns>A task that completes once the verdicts were applied.</returns>
        private async Task RenewOnceAsync(int tenantId, CancellationToken cancellationToken)
        {
            // --- What the renewal carries: the live leases with a row left to judge, and the buffered rows with the version each was taken at ---
            IReadOnlyList<JobLease> leases = Carried(_leases.InFlight(tenantId));
            if (leases.Count == 0)
            {
                return;
            }

            List<JobLeaseList> tokens = [.. leases.Select(static lease => new JobLeaseList { Id = lease.Token, LeaseSeconds = lease.LeaseSeconds })];
            List<JobProgressList> rows = [];
            List<(JobSlot Slot, long Version)> carried = [];
            foreach (JobSlot slot in leases.SelectMany(static lease => lease.Slots))
            {
                if (slot.Buffered() is (JobProgressList row, long version))
                {
                    rows.Add(row);
                    carried.Add((slot, version));
                }
            }

            // --- The round trip, in the system scope, committing each statement on its own ---
            await using TenantScope scope = await _scopes.CreateScopeAsync(JobScopes.System(tenantId), cancellationToken: cancellationToken).ConfigureAwait(false);
            var batch = (DataBatch)scope.Services.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Maintenance);
            batch.TransactionMode = TransactionMode.None;
            BatchResult<IReadOnlyList<RenewedJob>> renewed = JobStatements.Renew(batch, tokens, rows);
            _ = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            // --- The verdicts; each lease lost is logged with the jobs it held ---
            foreach (JobLease lost in Judge(leases, renewed.Value))
            {
                using (_logger.BeginScope(new JobLogScope(tenantId, userId: null, lost.Slots.Select(static slot => slot.JobId), originTraceParent: null)))
                {
                    JobsLog.JobLeaseLost(_logger, lost.Handler.Key);
                }
            }

            // --- The carried rows leave the buffer, each only as far as the version it was taken at;
            // their requesters learn of the change ---
            IClientEventPublisher publisher = scope.Services.GetRequiredService<IClientEventPublisher>();
            foreach ((JobSlot slot, long version) in carried)
            {
                slot.Carried(version);
                if (slot.RequestedById is int requester)
                {
                    await publisher.PublishAsync(JobNotices.Changed(slot.JobId, requester)).ConfigureAwait(false);
                }
            }
        }

        // Spec 0020 §5.2: a lease whose rows are all completing has nothing left to extend or judge,
        // and its completion's transaction holds those rows' locks until the handler's statements and
        // enlisted groups finish; carrying its token would make the renewal wait for them, and every
        // lease behind it with it. A completion that fails is meant to leave its lease to lapse.
        /// <summary>The leases a renewal carries: those not lost with a row whose completion is not being sent.</summary>
        /// <param name="inFlight">The tenant's leases on this instance.</param>
        /// <returns>The leases to renew.</returns>
        internal static IReadOnlyList<JobLease> Carried(IEnumerable<JobLease> inFlight)
        {
            ArgumentNullException.ThrowIfNull(inFlight);
            return [.. inFlight.Where(static lease => !lease.IsLost && lease.Slots.Any(static slot => !slot.Completing))];
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
    }
}
