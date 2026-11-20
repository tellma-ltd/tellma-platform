// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Jobs
{
    /// <summary>One tenant's poll round trip; the worker's seam over <see cref="JobPoll" />.</summary>
    internal interface IJobPoll
    {
        /// <summary>Runs one poll of a tenant.</summary>
        /// <param name="plan">What the poll carries.</param>
        /// <param name="onExecuted">Takes what the round trip returned, before the follow-ups after it: the worker starts the claims' batches here.</param>
        /// <param name="cancellationToken">Cancels the scope and the round trip.</param>
        /// <returns>What the round trip returned, once its follow-ups ended.</returns>
        Task<JobPollResult> PollAsync(JobPollPlan plan, Action<JobPollResult> onExecuted, CancellationToken cancellationToken);
    }

    // Spec 0020 §6.3: one Maintenance batch in the system scope, each statement committing on its own,
    // every statement idempotent so the executor re-runs the whole round trip after a transient or
    // ambiguous failure (§6.8): the gap check and hold; one claim per key with a free slot, each with
    // its entity load; tick step 1; the per-minute backlog sample; the hourly orphan sample and
    // retired-key cancel; the heartbeat stamp, last. After the round trip: the claims' batches start,
    // job.changed goes to each claimed row's requester (§10), the gap notices go out in Persist
    // batches of their own under the system user (§6.10), the retired rows' confirmation notices are
    // withdrawn (§6.9), and tick step 2 runs. §9: the poll is one activity, and each claim a Client
    // span `receive <key>` under it.
    /// <summary>Runs one tenant's poll round trip and the work that follows it.</summary>
    internal sealed class JobPoll : IJobPoll
    {
        /// <summary>The most notification or withdrawal requests one follow-up batch carries, well under the batch's parameter cap.</summary>
        internal const int RequestsPerBatch = 100;

        /// <summary>The name of the activity each poll round trip runs under.</summary>
        internal const string PollActivityName = "poll tellma.jobs";

        /// <summary>The operation of a claim's span.</summary>
        internal const string ReceiveOperation = "receive";

        /// <summary>Opens the system scope the poll runs in.</summary>
        private readonly ITenantScopeFactory _scopes;

        /// <summary>The handlers and retired keys this instance knows.</summary>
        private readonly JobHandlerRegistry _registry;

        /// <summary>The scheduler's tick; null on a host that composes none.</summary>
        private readonly IScheduleTick? _tick;

        // The options are read at each poll: the host creates the worker, and this poll with it, before
        // the startup gate reports any invalid option.
        /// <summary>The job options: the gap threshold.</summary>
        private readonly IOptions<JobsOptions> _options;

        /// <summary>The notification options: how many recipients one request may name.</summary>
        private readonly IOptions<NotificationsOptions> _notifications;

        /// <summary>The job instruments.</summary>
        private readonly JobsMetrics _metrics;

        /// <summary>Times the round trip.</summary>
        private readonly TimeProvider _time;

        /// <summary>The job machinery's logger.</summary>
        private readonly ILogger _logger;

        /// <summary>Creates the poll.</summary>
        /// <param name="scopes">Opens the system scope the poll runs in.</param>
        /// <param name="registry">The handlers and retired keys this instance knows.</param>
        /// <param name="ticks">The scheduler's tick, when a feature composes one; at most one.</param>
        /// <param name="options">The job options.</param>
        /// <param name="notifications">The notification options.</param>
        /// <param name="metrics">The job instruments.</param>
        /// <param name="time">Times the round trip.</param>
        /// <param name="loggers">Creates the job machinery's logger.</param>
        public JobPoll(
            ITenantScopeFactory scopes,
            JobHandlerRegistry registry,
            IEnumerable<IScheduleTick> ticks,
            IOptions<JobsOptions> options,
            IOptions<NotificationsOptions> notifications,
            JobsMetrics metrics,
            TimeProvider time,
            ILoggerFactory loggers)
        {
            ArgumentNullException.ThrowIfNull(scopes);
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(ticks);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(notifications);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(time);
            ArgumentNullException.ThrowIfNull(loggers);
            _scopes = scopes;
            _registry = registry;
            _tick = ticks.SingleOrDefault();
            _options = options;
            _notifications = notifications;
            _metrics = metrics;
            _time = time;
            _logger = loggers.CreateLogger(JobsLog.Category);
        }

        /// <inheritdoc />
        public async Task<JobPollResult> PollAsync(JobPollPlan plan, Action<JobPollResult> onExecuted, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(onExecuted);

            // --- §9: the poll's own root activity, which the claims' spans and batches run under ---
            Activity? previous = Activity.Current;
            Activity.Current = null;
            using Activity? poll = JobsActivities.Source.StartActivity(PollActivityName, ActivityKind.Internal, parentContext: default);
            _ = poll?.SetTag(JobsActivities.SystemTag, JobsActivities.MessagingSystem);
            try
            {
                TenantScope scope = await _scopes.CreateScopeAsync(JobScopes.System(plan.TenantId), cancellationToken: cancellationToken).ConfigureAwait(false);
                await using (scope.ConfigureAwait(false))
                {
                    IServiceProvider services = scope.Services;
                    JobPollResult result = await RoundTripAsync(plan, services, cancellationToken).ConfigureAwait(false);
                    Observe(plan.TenantId, result);
                    onExecuted(result);
                    await FollowUpAsync(plan.TenantId, result, services, cancellationToken).ConfigureAwait(false);
                    return result;
                }
            }
            finally
            {
                Activity.Current = previous;
            }
        }

        /// <summary>Composes the round trip, sends it, and reads what each statement returned.</summary>
        /// <param name="plan">What the poll carries.</param>
        /// <param name="services">The system scope's services.</param>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <returns>What the round trip returned.</returns>
        private async Task<JobPollResult> RoundTripAsync(JobPollPlan plan, IServiceProvider services, CancellationToken cancellationToken)
        {
            var batch = (DataBatch)services.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Maintenance);
            batch.TransactionMode = TransactionMode.None;

            // --- 1. The gap check and hold ---
            BatchResult<JobGapCheck> gap = JobStatements.GapCheckAndHold(batch, _options.Value.GapThreshold);

            // --- 2. The claims, each under a fresh token, with its entity load ---
            List<(JobClaim Claim, JobSlotReservation? Reservation, Activity? Receive)> claims = new(plan.Claims.Count);
            Activity? poll = Activity.Current;
            foreach (JobPollClaimRequest request in plan.Claims)
            {
                Activity? receive = JobsActivities.Source.StartActivity(
                    $"{ReceiveOperation} {request.Handler.Key}",
                    ActivityKind.Client,
                    parentContext: default,
                    [
                        new(JobsActivities.SystemTag, JobsActivities.MessagingSystem),
                        new(JobsActivities.DestinationTag, request.Handler.Key),
                        new(JobsActivities.OperationTag, ReceiveOperation),
                    ]);

                // Each span is a sibling under the poll's activity, which stays current.
                Activity.Current = poll;
                claims.Add((JobRunner.AppendClaim(batch, request.Handler, Guid.NewGuid(), plan.Owner), request.Reservation, receive));
            }

            // --- 3. Tick step 1, unless the instance is stopping ---
            IScheduleTickRound? tick = _tick is not null && !plan.Stopping
                ? _tick.Append(batch, new ScheduleTickContext(plan.TenantId, plan.ReadOnly, [.. _registry.Handlers.Where(static handler => handler.Options.RunsWhenReadOnly).Select(static handler => handler.Key)]))
                : null;

            // --- 4. The backlog sample, once a minute ---
            BatchResult<IReadOnlyList<JobBacklogSample>>? backlog = plan.SampleBacklog && _registry.Handlers.Count > 0
                ? JobStatements.BacklogSample(batch, [.. _registry.Handlers.Select(static handler => handler.Key)])
                : null;

            // --- 5. The orphan sample and the retired-key cancel, once an hour ---
            BatchResult<long>? orphans = plan.SampleOrphans
                ? JobStatements.OrphanSample(batch, [.. _registry.Handlers.Select(static handler => handler.Key).Concat(_registry.RetiredKeys)])
                : null;
            BatchResult<IReadOnlyList<RetiredJobCancelled>>? retired = plan.SampleOrphans && _registry.RetiredKeys.Count > 0
                ? JobStatements.RetiredKeyCancel(batch, _registry.RetiredKeys)
                : null;

            // --- 6. The heartbeat stamp, last ---
            BatchResult<DateTimeOffset?> stamp = JobStatements.HeartbeatStamp(batch, plan.Owner);

            // --- The round trip, timed; each claim's span closes with it ---
            long sentAt = _time.GetTimestamp();
            try
            {
                _ = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                foreach ((_, _, Activity? receive) in claims)
                {
                    _ = receive?.SetStatus(ActivityStatusCode.Error, failure.GetType().Name);
                    receive?.Dispose();
                }

                throw;
            }
            finally
            {
                _metrics.RecordPollDuration(_time.GetElapsedTime(sentAt));
            }

            foreach ((JobClaim claim, _, Activity? receive) in claims)
            {
                _ = receive?.SetTag(JobsActivities.MessageCountTag, claim.Rows.Value.Count);
                receive?.Dispose();
            }

            return new JobPollResult(
                plan.TenantId,
                [.. claims.Select(static claim => new JobPolledClaim(claim.Claim, claim.Reservation))],
                gap.Value,
                tick,
                backlog?.Value,
                orphans?.Value,
                retired?.Value ?? [],
                stamp.Value);
        }

        /// <summary>Records what the round trip found on the instruments and in the log.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="result">What the round trip returned.</param>
        private void Observe(int tenantId, JobPollResult result)
        {
            // --- The claims and the samples ---
            foreach (JobPolledClaim polled in result.Claims)
            {
                _metrics.RecordClaim(polled.Claim.Handler, polled.Claim.Rows.Value);
            }

            foreach (JobBacklogSample sample in result.Backlog ?? [])
            {
                _metrics.RecordBacklogAge(sample.HandlerKey, sample.Age);
            }

            if (result.Orphaned is long orphaned)
            {
                _metrics.SetOrphaned(tenantId, orphaned);
            }

            // --- The log, in the tenant's scope ---
            using IDisposable? scope = _logger.BeginScope(new JobLogScope(tenantId, userId: null, [], originTraceParent: null));
            if (result.Gap.GapDetected)
            {
                _metrics.RecordGapDetected();
                JobsLog.SchedulerGapDetected(_logger, result.Gap.PreviousTickAt!.Value, result.Gap.Now, result.Gap.HeldCount);
            }

            if (result.Retired.Count > 0)
            {
                JobsLog.JobRetiredKeysCancelled(_logger, result.Retired.Count);
            }

            if (result.Stamped is DateTimeOffset stamped)
            {
                JobsLog.JobWorkerHeartbeat(_logger, stamped);
            }
        }

        /// <summary>
        ///     The work after the round trip: the claimed rows' change events, the gap notices, the
        ///     withdrawals of the retired rows' confirmation notices, then tick step 2. A failure of the
        ///     events or the notices is logged and leaves the poll's own work standing.
        /// </summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="result">What the round trip returned.</param>
        /// <param name="services">The system scope's services.</param>
        /// <param name="cancellationToken">Cancels the follow-ups.</param>
        /// <returns>A task that completes when the follow-ups ended.</returns>
        private async Task FollowUpAsync(int tenantId, JobPollResult result, IServiceProvider services, CancellationToken cancellationToken)
        {
            using IDisposable? scope = _logger.BeginScope(new JobLogScope(tenantId, userId: null, [], originTraceParent: null));

            // --- §10: job.changed after the claims, to each claimed row's requester ---
            try
            {
                IClientEventPublisher publisher = services.GetRequiredService<IClientEventPublisher>();
                foreach (Job job in result.Claims.SelectMany(static polled => polled.Claim.Rows.Value).Select(static row => row.Job))
                {
                    if (job.RequestedById is int requester)
                    {
                        await publisher.PublishAsync(JobNotices.Changed(job.Id, requester)).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                JobsLog.JobPollNoticesFailed(_logger, failure, "change events of the claimed jobs");
            }

            // --- §6.10: the gap notices, in persist batches of their own ---
            if (result.Gap.GapDetected)
            {
                try
                {
                    await NotifyGapAsync(result.Gap, services, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    JobsLog.JobPollNoticesFailed(_logger, failure, "gap notices");
                }
            }

            // --- §6.9: the confirmation notices of the retired rows that waited for one ---
            WithdrawRequest[] withdrawals = [.. result.Retired
                .Where(static row => row.PreviousStatus == JobStatus.PendingConfirmation)
                .Select(static row => new WithdrawRequest(JobNotices.PendingConfirmationKey(row.Id)))];
            if (withdrawals.Length > 0)
            {
                try
                {
                    INotifier notifier = services.GetRequiredService<INotifier>();
                    foreach (WithdrawRequest[] chunk in withdrawals.Chunk(RequestsPerBatch))
                    {
                        await notifier.WithdrawAsync(chunk, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    JobsLog.JobPollNoticesFailed(_logger, failure, "withdrawals of the retired jobs' confirmation notices");
                }
            }

            // --- §7.6: tick step 2, over what step 1 leased ---
            if (result.Tick is IScheduleTickRound tick)
            {
                await tick.RunStep2Async(cancellationToken).ConfigureAwait(false);
            }
        }

        // Spec 0020 §6.10: core.scheduler.gap to the administrators with the sum of every held row's
        // count, and core.job.held to each requester with theirs; rows with no requester are reported
        // by the gap notice alone. Both rely on the notifier's default Suppress, so a repeat while the
        // first is unread adds nothing. The requests are sent in as many persist batches as the
        // parameter cap requires.
        /// <summary>Raises the gap notices.</summary>
        /// <param name="gap">What the gap check found.</param>
        /// <param name="services">The system scope's services.</param>
        /// <param name="cancellationToken">Cancels the batches not yet sent.</param>
        /// <returns>A task that completes when every batch committed.</returns>
        private async Task NotifyGapAsync(JobGapCheck gap, IServiceProvider services, CancellationToken cancellationToken)
        {
            IReadOnlyList<int> administrators = await services.GetRequiredService<IAdministratorDirectory>().GetAdministratorIdsAsync(cancellationToken).ConfigureAwait(false);
            List<NotificationRequest> requests = [.. JobNotices.Gap(gap, administrators, _notifications.Value.MaxRecipientsPerRequest)];
            INotifier notifier = services.GetRequiredService<INotifier>();
            foreach (NotificationRequest[] chunk in requests.Chunk(RequestsPerBatch))
            {
                await notifier.NotifyAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>What one poll carries.</summary>
    /// <param name="TenantId">The tenant.</param>
    /// <param name="ReadOnly">Whether the tenant is read-only, so the tick leases only the schedules of keys that run then.</param>
    /// <param name="Stopping">Whether the instance is stopping, so the tick is left out.</param>
    /// <param name="Claims">The claims, one per key with a free slot.</param>
    /// <param name="SampleBacklog">Whether the poll carries the backlog sample.</param>
    /// <param name="SampleOrphans">Whether the poll carries the orphan sample and the retired-key cancel.</param>
    /// <param name="Owner">The lease owner the claims and the stamp name.</param>
    internal sealed record JobPollPlan(
        int TenantId, bool ReadOnly, bool Stopping, IReadOnlyList<JobPollClaimRequest> Claims, bool SampleBacklog, bool SampleOrphans, string Owner);

    /// <summary>One claim a poll carries.</summary>
    /// <param name="Handler">The key's handler.</param>
    /// <param name="Reservation">The slot the claim's batch runs in; null where no slot is reserved.</param>
    internal sealed record JobPollClaimRequest(JobHandlerRegistration Handler, JobSlotReservation? Reservation);

    /// <summary>One claim a poll's round trip returned.</summary>
    /// <param name="Claim">The claim, its rows read.</param>
    /// <param name="Reservation">The slot reserved for it; null where none was.</param>
    internal sealed record JobPolledClaim(JobClaim Claim, JobSlotReservation? Reservation);

    /// <summary>What one poll's round trip returned.</summary>
    /// <param name="TenantId">The tenant.</param>
    /// <param name="Claims">The claims, in plan order.</param>
    /// <param name="Gap">What the gap check found.</param>
    /// <param name="Tick">The tick's round; null when the poll carried no tick.</param>
    /// <param name="Backlog">The backlog sample; null when the poll carried none.</param>
    /// <param name="Orphaned">The orphan sample; null when the poll carried none.</param>
    /// <param name="Retired">The jobs the retired-key cancel ended.</param>
    /// <param name="Stamped">The heartbeat stamp written; null when the throttle left it.</param>
    internal sealed record JobPollResult(
        int TenantId,
        IReadOnlyList<JobPolledClaim> Claims,
        JobGapCheck Gap,
        IScheduleTickRound? Tick,
        IReadOnlyList<JobBacklogSample>? Backlog,
        long? Orphaned,
        IReadOnlyList<RetiredJobCancelled> Retired,
        DateTimeOffset? Stamped)
    {
        // Spec 0020 §6.4: a full claim for any key, or a full tick, polls again at once; anything
        // short of that resets the interval; nothing doubles it.
        /// <summary>What the poll found, which decides the wait before the next.</summary>
        public JobPollFinding Finding =>
            Claims.Any(static polled => polled.Claim.Rows.Value.Count >= polled.Claim.Handler.Options.BatchSize) || Tick?.Full == true
                ? JobPollFinding.Full
                : Claims.Any(static polled => polled.Claim.Rows.Value.Count > 0) || Tick?.Leased > 0
                    ? JobPollFinding.Some
                    : JobPollFinding.Nothing;
    }

    /// <summary>What a poll found, as the poll interval reads it.</summary>
    internal enum JobPollFinding
    {
        /// <summary>Nothing to claim and nothing due.</summary>
        Nothing,

        /// <summary>Work short of a full claim or a full tick.</summary>
        Some,

        /// <summary>A full claim for some key, or a full tick: more may be waiting.</summary>
        Full,
    }
}
