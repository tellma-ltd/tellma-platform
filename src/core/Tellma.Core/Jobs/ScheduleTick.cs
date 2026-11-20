// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Cron = Cronos.CronExpression;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §7.4 to §7.7: step 1 rides the poll's round trip under a fresh token; step 2 runs
    // after it, in the poll's system scope, over what step 1 leased. Step 2 computes each schedule's
    // firing in C# from the database's clock, then sends one persist batch: the fenced fire-and-advance
    // statement, the pauses, their notices, and the nudge of this instance's poller once it commits.
    // A failure is logged and leaves the leased states unchanged, so the lease lapses and the next
    // tick on any instance computes them again: at least once, the overlap predicate keeping a Skip
    // schedule from firing twice.
    /// <summary>The scheduler's tick: step 1 on the poll's batch, step 2 after its round trip.</summary>
    internal sealed class ScheduleTick : IScheduleTick
    {
        /// <summary>How long a schedule this host cannot evaluate waits before a tick looks at it again.</summary>
        internal static readonly TimeSpan UnusableRetry = TimeSpan.FromHours(1);

        /// <summary>The handlers and retired keys this instance knows.</summary>
        private readonly JobHandlerRegistry _registry;

        // The options are read at each tick: the host creates the worker, and this tick with it,
        // before the startup gate reports any invalid option.
        /// <summary>The job options: the gap threshold and the longest poll interval.</summary>
        private readonly IOptions<JobsOptions> _options;

        /// <summary>The notification options: how many recipients one request may name.</summary>
        private readonly IOptions<NotificationsOptions> _notifications;

        /// <summary>Wakes this instance's poller once a tick fired jobs.</summary>
        private readonly TenantPollerWake _wake;

        /// <summary>The job instruments.</summary>
        private readonly JobsMetrics _metrics;

        /// <summary>The job machinery's logger.</summary>
        private readonly ILogger _logger;

        /// <summary>Creates the tick.</summary>
        /// <param name="registry">The handlers and retired keys this instance knows.</param>
        /// <param name="options">The job options.</param>
        /// <param name="notifications">The notification options.</param>
        /// <param name="wake">Wakes this instance's poller.</param>
        /// <param name="metrics">The job instruments.</param>
        /// <param name="loggers">Creates the job machinery's logger.</param>
        public ScheduleTick(
            JobHandlerRegistry registry,
            IOptions<JobsOptions> options,
            IOptions<NotificationsOptions> notifications,
            TenantPollerWake wake,
            JobsMetrics metrics,
            ILoggerFactory loggers)
        {
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(notifications);
            ArgumentNullException.ThrowIfNull(wake);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(loggers);
            _registry = registry;
            _options = options;
            _notifications = notifications;
            _wake = wake;
            _metrics = metrics;
            _logger = loggers.CreateLogger(JobsLog.Category);
        }

        /// <inheritdoc />
        public IScheduleTickRound Append(DataBatch batch, ScheduleTickContext context)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(context);

            // A fresh token per round trip: a re-run of the same round trip reuses it (spec 0020 §6.8).
            var token = Guid.NewGuid();
            BatchResult<IReadOnlyList<ScheduleTickRow>> rows = JobStatements.TickStep1(batch, token, context.ReadOnly ? context.ReadOnlyKeys : null);
            return new Round(this, context.TenantId, token, rows);
        }

        /// <summary>Runs step 2 over what step 1 leased; a failure is logged, and the leased states are left to their lease.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="token">The tick's token.</param>
        /// <param name="rows">The schedules step 1 leased.</param>
        /// <param name="services">The poll's system scope's services.</param>
        /// <param name="cancellationToken">Cancels the reads before the persist; the persist, once sent, completes regardless.</param>
        /// <returns>A task that completes when the step ended.</returns>
        private async Task RunStep2Async(int tenantId, Guid token, IReadOnlyList<ScheduleTickRow> rows, IServiceProvider services, CancellationToken cancellationToken)
        {
            try
            {
                await FireAsync(tenantId, token, rows, services, cancellationToken).ConfigureAwait(false);
            }
            catch (BatchAssertionFailedException lost) when (lost is { Number: TellmaSqlErrors.Invariant, Code: JobErrorCodes.TickLeaseLost })
            {
                JobsLog.SchedulerTickLeaseLost(_logger, rows.Count);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The instance is stopping; the leases lapse and another instance ticks.
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                JobsLog.SchedulerTickFailed(_logger, failure, rows.Count);
            }
        }

        /// <summary>Computes each leased schedule's firing and sends the persist batch that fires and advances them.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="token">The tick's token.</param>
        /// <param name="rows">The schedules step 1 leased.</param>
        /// <param name="services">The poll's system scope's services.</param>
        /// <param name="cancellationToken">Cancels the reads before the persist.</param>
        /// <returns>A task that completes when the batch committed.</returns>
        private async Task FireAsync(int tenantId, Guid token, IReadOnlyList<ScheduleTickRow> rows, IServiceProvider services, CancellationToken cancellationToken)
        {
            // --- §7.5: each schedule's firing, from the database's clock and its zone ---
            JobsOptions options = _options.Value;
            TimeZoneInfo tenantZone = ScheduleCron.TenantZone(services.GetRequiredService<IRequestContextAccessor>().Current);
            List<(ScheduleTickRow Row, ScheduleFiring Firing)> computed = new(rows.Count);
            foreach (ScheduleTickRow row in rows)
            {
                // A schedule this host cannot evaluate fires nothing and is looked at again an hour
                // later, so it neither holds the tick's lease rows nor logs at every poll.
                ScheduleFiring firing = Evaluate(row, tenantZone) is (Cron cron, TimeZoneInfo zone)
                    ? ScheduleOccurrences.Compute(row, cron, zone, _registry.IsRetired(row.HandlerKey), options.GapThreshold, options.MaxPollInterval)
                    : new ScheduleFiring(Pause: null, Occurrences: [], Missed: 0, Next: row.Now + UnusableRetry);
                computed.Add((row, firing));
            }

            // --- §7.5 step 6: one job row per occurrence fired, each Id its 1-based ordinal; one state row per schedule ---
            List<Job> jobs = [];
            List<ScheduleNextList> next = new(computed.Count);
            foreach ((ScheduleTickRow row, ScheduleFiring firing) in computed)
            {
                foreach (DateTimeOffset occurrence in firing.Occurrences)
                {
                    jobs.Add(new Job
                    {
                        Id = jobs.Count + 1,
                        HandlerKey = row.HandlerKey,
                        DueAt = row.Now,
                        ArgumentsJson = row.ArgumentsJson,
                        RunAsUserId = row.RunAsUserId,
                        RequestedById = row.IsBuiltIn ? null : row.RunAsUserId,
                        ScheduleId = row.Id,
                        ScheduledFor = occurrence,
                        CreatedAt = row.Now,
                    });
                }

                next.Add(new ScheduleNextList { ScheduleId = row.Id, NextDueAt = firing.Next, LastScheduledFor = firing.LastScheduledFor });
                if (firing.Occurrences.Count > 0 && !_registry.TryFind(row.HandlerKey, out _) && !_registry.IsRetired(row.HandlerKey))
                {
                    JobsLog.SchedulerHandlerUnknown(_logger, row.Id, row.HandlerKey);
                }
            }

            // --- §7.7: the pauses by reason, and their notices, whose recipients may be the administrators ---
            Dictionary<SchedulePausedReason, List<int>> pauses = [];
            List<NotificationRequest> notices = [];
            IReadOnlyList<int>? administrators = null;
            int maxRecipients = _notifications.Value.MaxRecipientsPerRequest;
            foreach ((ScheduleTickRow row, ScheduleFiring firing) in computed)
            {
                if (firing.Pause is not SchedulePausedReason reason)
                {
                    continue;
                }

                if (!pauses.TryGetValue(reason, out List<int>? ids))
                {
                    pauses[reason] = ids = [];
                }

                ids.Add(row.Id);
                administrators ??= await services.GetRequiredService<IAdministratorDirectory>().GetAdministratorIdsAsync(cancellationToken).ConfigureAwait(false);
                notices.AddRange(JobNotices.SchedulePaused(row.Id, row.Name, row.RunAsUserId, reason, administrators, maxRecipients));
            }

            // --- §7.6: one persist batch in the system scope; the fence first, then the pauses and their notices ---
            IGuardedBatchRunner runner = services.GetRequiredService<IGuardedBatchRunner>();
            INotifier notifier = services.GetRequiredService<INotifier>();
            BatchResult<IReadOnlyList<ScheduleFired>>? fired = null;
            try
            {
                _ = await runner.RunAsync(
                    BatchPurpose.Persist,
                    (user, batch) =>
                    {
                        var data = (DataBatch)batch;
                        fired = JobStatements.TickStep2(data, token, jobs, next);
                        foreach ((SchedulePausedReason reason, List<int> ids) in pauses)
                        {
                            JobStatements.Pause(data, reason, ids);
                        }

                        if (notices.Count > 0)
                        {
                            notifier.Notify(data, notices);
                        }

                        if (jobs.Count > 0)
                        {
                            data.OnCommitted(_ => _wake.Wake(tenantId));
                        }

                        return ValueTask.CompletedTask;
                    },
                    static (_, outcome) => outcome,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (DataAccessAmbiguousException ambiguous) when (ambiguous.Verdict == CommitVerdict.Committed)
            {
                // The probe found the batch committed; its response, and the fired jobs, were lost with
                // the connection, so nothing is counted.
                fired = null;
            }

            // --- §7.6 and §13: what fired, what the overlap rule skipped, what the policies dropped ---
            Observe(computed, fired is { IsCompleted: true } result ? result.Value : null);
        }

        /// <summary>The parsed expression and resolved zone of a leased schedule; null, logged, when either cannot be evaluated here.</summary>
        /// <param name="row">The schedule.</param>
        /// <param name="tenantZone">The tenant's zone, which a schedule without its own follows.</param>
        /// <returns>The expression and zone, or null.</returns>
        private (Cron Cron, TimeZoneInfo Zone)? Evaluate(ScheduleTickRow row, TimeZoneInfo tenantZone)
        {
            if (!ScheduleCron.TryParse(row.CronExpression, out Cron? cron))
            {
                JobsLog.SchedulerScheduleUnusable(_logger, row.Id, $"its cron expression '{row.CronExpression}' does not parse as five-field Unix cron.");
                return null;
            }

            if (ScheduleCron.TryZone(row.TimeZoneId, tenantZone, out TimeZoneInfo? zone))
            {
                return (cron, zone);
            }

            JobsLog.SchedulerScheduleUnusable(_logger, row.Id, $"this host's time-zone data does not resolve its zone '{row.TimeZoneId}'.");
            return null;
        }

        /// <summary>Records the tick on the instruments and in the log.</summary>
        /// <param name="computed">Each schedule the step carried, with its firing.</param>
        /// <param name="fired">The jobs the statement fired; null when the batch committed but its result was lost.</param>
        private void Observe(List<(ScheduleTickRow Row, ScheduleFiring Firing)> computed, IReadOnlyList<ScheduleFired>? fired)
        {
            ILookup<int, ScheduleFired>? byScheduleId = fired?.ToLookup(static job => job.ScheduleId);
            foreach ((ScheduleTickRow row, ScheduleFiring firing) in computed)
            {
                _metrics.RecordSchedulesMissed(row.MissedPolicy, firing.Missed);
                if (byScheduleId is not null && firing.Occurrences.Count > 0)
                {
                    int count = byScheduleId[row.Id].Count();
                    _metrics.RecordSchedulesFired(row.MissedPolicy, count);
                    if (count == 0)
                    {
                        _metrics.RecordSchedulesOverlapSkipped(1);
                    }
                }

                if (firing.Pause is SchedulePausedReason reason)
                {
                    JobsLog.SchedulePaused(_logger, row.Id, reason);
                }
            }
        }

        /// <summary>One tick that rode a poll's round trip.</summary>
        /// <param name="tick">The tick.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="token">The tick's token.</param>
        /// <param name="rows">What step 1 returns, once the batch executed.</param>
        private sealed class Round(ScheduleTick tick, int tenantId, Guid token, BatchResult<IReadOnlyList<ScheduleTickRow>> rows) : IScheduleTickRound
        {
            /// <inheritdoc />
            public int Leased => rows.Value.Count;

            /// <inheritdoc />
            public bool Full => rows.Value.Count >= JobStatements.TickLeaseRows;

            /// <inheritdoc />
            public Task RunStep2Async(IServiceProvider services, CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(services);
                return rows.Value.Count == 0 ? Task.CompletedTask : tick.RunStep2Async(tenantId, token, rows.Value, services, cancellationToken);
            }
        }
    }
}
