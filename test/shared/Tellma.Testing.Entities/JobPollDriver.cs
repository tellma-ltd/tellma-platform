// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data.Batches;
using Tellma.Core.Jobs;

namespace Tellma.Testing.Entities
{
    /// <summary>How one job polled by <see cref="JobPollDriver" /> ended its attempt on this instance.</summary>
    public enum PolledJobEnd
    {
        /// <summary>A completion recorded the job's outcome.</summary>
        Completed,

        /// <summary>The lease was lost, and the outcome was discarded.</summary>
        LostLease,

        /// <summary>Nothing could be recorded; the lease is left to lapse.</summary>
        Lapsed,
    }

    /// <summary>One job a poll claimed, and how its attempt ended.</summary>
    /// <param name="JobId">The job.</param>
    /// <param name="HandlerKey">The job's handler key.</param>
    /// <param name="End">How the attempt ended.</param>
    /// <param name="Status">The status the completion recorded; null unless completed.</param>
    /// <param name="ErrorCode">The error code the completion recorded; null when none.</param>
    public sealed record PolledJob(int JobId, string HandlerKey, PolledJobEnd End, JobStatus? Status, string? ErrorCode);

    /// <summary>
    ///     Runs one poll of a host's job machinery, the way the worker's poll does, for suites that
    ///     drive jobs from enqueue to completion: one maintenance batch claims every handler's due jobs
    ///     (each claim with its entity load), then each claim's jobs run and complete. Nothing polls
    ///     on its own, no renewal loop runs, and nothing is claimed again within the call.
    /// </summary>
    public static class JobPollDriver
    {
        /// <summary>Polls a fixture host's tenant once.</summary>
        /// <param name="tenant">The fixture host.</param>
        /// <param name="keys">The handler keys to claim; null for every registered handler.</param>
        /// <param name="hostToken">Stands in for the host's stopping token: once cancelled, the running items stop and the partitions not started are released.</param>
        /// <param name="cancellationToken">Cancels the poll's scope and round trip.</param>
        /// <returns>Each claimed job and how its attempt ended, by handler in registration order, then in claim order.</returns>
        public static Task<IReadOnlyList<PolledJob>> PollOnceAsync(
            FixtureTenant tenant, IReadOnlyCollection<string>? keys = null, CancellationToken hostToken = default, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(tenant);
            return PollOnceAsync(tenant.Services, FixtureTenant.TenantId, keys, hostToken, cancellationToken);
        }

        /// <summary>Polls a tenant once on a host's job machinery.</summary>
        /// <param name="services">The host's root services.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="keys">The handler keys to claim; null for every registered handler.</param>
        /// <param name="hostToken">Stands in for the host's stopping token.</param>
        /// <param name="cancellationToken">Cancels the poll's scope and round trip.</param>
        /// <returns>Each claimed job and how its attempt ended.</returns>
        public static async Task<IReadOnlyList<PolledJob>> PollOnceAsync(
            IServiceProvider services, int tenantId, IReadOnlyCollection<string>? keys = null, CancellationToken hostToken = default, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(services);
            JobRunner runner = services.GetRequiredService<JobRunner>();
            JobHandlerRegistration[] handlers = [.. services.GetRequiredService<JobHandlerRegistry>().Handlers.Where(handler => keys is null || keys.Contains(handler.Key))];
            if (handlers.Length == 0)
            {
                return [];
            }

            // --- The poll's round trip: every claim in one maintenance batch of the system scope,
            // each statement committing on its own, each claim under a fresh token ---
            string owner = string.Create(CultureInfo.InvariantCulture, $"{Environment.MachineName}/{Environment.ProcessId}");
            List<JobClaim> claims = [];
            TenantScope scope = await services.GetRequiredService<ITenantScopeFactory>()
                .CreateScopeAsync(JobScopes.System(tenantId), cancellationToken: cancellationToken).ConfigureAwait(false);
            await using (scope.ConfigureAwait(false))
            {
                var batch = (DataBatch)scope.Services.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Maintenance);
                batch.TransactionMode = TransactionMode.None;
                foreach (JobHandlerRegistration handler in handlers)
                {
                    claims.Add(JobRunner.AppendClaim(batch, handler, Guid.NewGuid(), owner));
                }

                _ = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }

            // --- Each claim's jobs run and complete, one claim after another ---
            List<PolledJob> polled = [];
            foreach (JobClaim claim in claims)
            {
                var keyOf = claim.Rows.Value.ToDictionary(static row => row.Job.Id, static row => row.Job.HandlerKey);
                foreach (JobSettled settled in await runner.RunAsync(tenantId, claim, hostToken).ConfigureAwait(false))
                {
                    PolledJobEnd end = settled.Settlement switch
                    {
                        JobSettlement.Completed => PolledJobEnd.Completed,
                        JobSettlement.LostLease => PolledJobEnd.LostLease,
                        JobSettlement.Lapsed => PolledJobEnd.Lapsed,
                        _ => throw new InvalidOperationException($"The settlement {settled.Settlement} is not defined."),
                    };
                    polled.Add(new PolledJob(settled.JobId, keyOf[settled.JobId], end, settled.Outcome?.Status, settled.Outcome?.ErrorCode));
                }
            }

            return polled;
        }
    }
}
