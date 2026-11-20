// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Crud.Enlistment;
using Tellma.Core.Crud.Save;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Jobs
{
    /// <summary>How a claimed job's attempt ended on this instance.</summary>
    internal enum JobSettlement
    {
        /// <summary>A completion recorded the job's outcome.</summary>
        Completed,

        /// <summary>The lease was lost: another instance owns the row, and the outcome was discarded.</summary>
        LostLease,

        /// <summary>Nothing could be recorded: the lease is left to lapse, and the job runs again once it does.</summary>
        Lapsed,
    }

    /// <summary>How one claimed job's attempt ended.</summary>
    /// <param name="JobId">The job.</param>
    /// <param name="Settlement">How the attempt ended.</param>
    /// <param name="Outcome">The outcome recorded; null unless completed.</param>
    internal sealed record JobSettled(int JobId, JobSettlement Settlement, JobOutcome? Outcome);

    /// <summary>Runs what a claim leased; the worker's seam over <see cref="JobRunner" />.</summary>
    internal interface IJobClaimRunner
    {
        /// <summary>Runs what a claim leased, completing each of its jobs.</summary>
        /// <param name="claim">The claim, its batch executed.</param>
        /// <param name="hostToken">The host's token: once cancelled, the items stop and partitions not started are released.</param>
        /// <returns>How each claimed job's attempt ended, in claim order.</returns>
        Task<IReadOnlyList<JobSettled>> RunAsync(JobClaim claim, CancellationToken hostToken);
    }

    /// <summary>One job and the outcome a completion records on it.</summary>
    /// <param name="Job">The job, as its claim returned it.</param>
    /// <param name="Outcome">The outcome.</param>
    internal sealed record JobCompletion(Job Job, JobOutcome Outcome);

    // Spec 0020 §3.4–§3.6, §5.1, §5.4–§5.6, §8 and §9: a claim's rows run here. The rows no handler
    // may run (a requested cancel, an attempt past the last, a missing entity) complete at once in
    // the system scope. The others are partitioned by run-as user, in claim order, and each partition
    // runs in its own scope, under its own `process <key>` root, with its own completion batch: the
    // batch is composed before the handler runs, the handler's statements and the job frame's groups
    // follow the completion statement, and the worker's notices follow them. The batch is then sent,
    // with every failure path of §5.6 answered.
    /// <summary>Claims jobs into a poll's batch and runs what a claim leased.</summary>
    internal sealed class JobRunner : IJobClaimRunner
    {
        /// <summary>What follows a completion failure answered by a completion of the outcomes alone.</summary>
        private const string FreshNext = "the handler's statements and enlisted writes are discarded and the outcomes are recorded alone.";

        /// <summary>What follows a completion failure that leaves the lease to lapse.</summary>
        private const string LapseNext = "the lease is left to lapse and the jobs run again once it does.";

        /// <summary>What follows a failure of the post-commit work after a committed completion.</summary>
        private const string PostCommitNext = "the jobs completed, but the post-commit work of a write they enlisted did not finish.";

        /// <summary>Why a handler may not execute its invocation's completion batch.</summary>
        private const string CompletionBatchReserved =
            "The job's batch is sent by the worker once the handler returned, after the statement that records the outcomes and only while the lease holds. Compose onto it, and execute only the batches the handler created itself.";

        /// <summary>The generic claim runner, closed per item type.</summary>
        private static readonly MethodInfo RunClaimMethod = typeof(JobRunner).GetMethod(nameof(RunClaimAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

        /// <summary>The generic entity-handler invoker, closed per entity type.</summary>
        private static readonly MethodInfo InvokeEntityMethod = typeof(JobRunner).GetMethod(nameof(InvokeEntityAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

        /// <summary>The claim runners, per item type.</summary>
        private static readonly ConcurrentDictionary<Type, Func<JobRunner, int, JobClaim, CancellationToken, Task<IReadOnlyList<JobSettled>>>> ClaimRunners = new();

        /// <summary>The entity-handler invokers, per entity type.</summary>
        private static readonly ConcurrentDictionary<Type, Func<object, object, Task>> EntityInvokers = new();

        /// <summary>Opens the job and system scopes.</summary>
        private readonly ITenantScopeFactory _scopes;

        /// <summary>The leases this instance holds.</summary>
        private readonly JobLeases _leases;

        /// <summary>Runs the renewal a handler's flush or jump asks for.</summary>
        private readonly IJobRenewal _renewal;

        /// <summary>Renders progress and error keys for the log.</summary>
        private readonly JobLogText _text;

        /// <summary>Wakes this instance's pollers when a completion queues a job again at once.</summary>
        private readonly TenantPollerWake _wake;

        /// <summary>Times each attempt from its claim to its completion.</summary>
        private readonly TimeProvider _time;

        /// <summary>The job instruments: completions, invocation durations and lost leases.</summary>
        private readonly JobsMetrics _metrics;

        /// <summary>The job machinery's logger.</summary>
        private readonly ILogger _logger;

        /// <summary>Creates the runner.</summary>
        /// <param name="scopes">Opens the job and system scopes.</param>
        /// <param name="leases">The leases this instance holds.</param>
        /// <param name="renewal">Runs the renewal a handler's flush or jump asks for.</param>
        /// <param name="text">Renders progress and error keys for the log.</param>
        /// <param name="wake">Wakes this instance's pollers.</param>
        /// <param name="time">The clock.</param>
        /// <param name="metrics">The job instruments.</param>
        /// <param name="loggers">Creates the job machinery's logger.</param>
        public JobRunner(
            ITenantScopeFactory scopes, JobLeases leases, IJobRenewal renewal, JobLogText text, TenantPollerWake wake, TimeProvider time, JobsMetrics metrics, ILoggerFactory loggers)
        {
            ArgumentNullException.ThrowIfNull(scopes);
            ArgumentNullException.ThrowIfNull(leases);
            ArgumentNullException.ThrowIfNull(renewal);
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(wake);
            ArgumentNullException.ThrowIfNull(time);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(loggers);
            _scopes = scopes;
            _leases = leases;
            _renewal = renewal;
            _text = text;
            _wake = wake;
            _time = time;
            _metrics = metrics;
            _logger = loggers.CreateLogger(JobsLog.Category);
        }

        /// <summary>
        ///     Appends one claim of a handler's key to a poll's batch, followed by the entity load of an
        ///     entity-backed handler, so both ride the poll's round trip.
        /// </summary>
        /// <param name="batch">The poll's batch: a maintenance batch that commits each statement on its own.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="token">The claim's fresh fencing token.</param>
        /// <param name="owner">The instance and process the lease names.</param>
        /// <returns>The claim, whose rows are read once the batch executed.</returns>
        public static JobClaim AppendClaim(DataBatch batch, JobHandlerRegistration handler, Guid token, string owner)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(handler);
            ClaimStatement claim = JobStatements.Claim(batch, handler.Key, handler.Options.BatchSize, token, handler.Options.LeaseSeconds, owner);
            IJobEntityLoad? entities = handler.EntityType is Type leaf
                ? (IJobEntityLoad)Activator.CreateInstance(typeof(JobEntityLoad<>).MakeGenericType(leaf), batch, claim.ClaimedTable)!
                : null;
            return new JobClaim(batch.Connection.TenantId, handler, token, claim.Rows, entities);
        }

        /// <summary>
        ///     Runs what a claim leased: completes the rows no handler may run, then invokes the handler
        ///     once per run-as partition and completes each.
        /// </summary>
        /// <param name="claim">The claim, its batch executed; its jobs run in the tenant it leased them from.</param>
        /// <param name="hostToken">The host's token: once cancelled, the items stop and partitions not started are released.</param>
        /// <returns>How each claimed job's attempt ended, in claim order.</returns>
        public Task<IReadOnlyList<JobSettled>> RunAsync(JobClaim claim, CancellationToken hostToken)
        {
            ArgumentNullException.ThrowIfNull(claim);
            Func<JobRunner, int, JobClaim, CancellationToken, Task<IReadOnlyList<JobSettled>>> run = ClaimRunners.GetOrAdd(
                claim.Handler.ItemType,
                static type => RunClaimMethod.MakeGenericMethod(type).CreateDelegate<Func<JobRunner, int, JobClaim, CancellationToken, Task<IReadOnlyList<JobSettled>>>>());
            return run(this, claim.TenantId, claim, hostToken);
        }

        // Spec 0020 §3.5, with the consent the row carries as a second key: one scope's confirmations
        // are seeded once, so jobs that carry different confirmed keys never share a scope, where one
        // job's consent would pass another's warning.
        /// <summary>Partitions claimed rows by run-as user and confirmed keys, keeping claim order within and across partitions.</summary>
        /// <param name="rows">The rows, in claim order.</param>
        /// <returns>The partitions, in the order of their first row.</returns>
        internal static IReadOnlyList<IReadOnlyList<ClaimedJob>> Partition(IReadOnlyList<ClaimedJob> rows)
        {
            ArgumentNullException.ThrowIfNull(rows);
            Dictionary<(int RunAs, string Keys), List<ClaimedJob>> partitions = [];
            List<IReadOnlyList<ClaimedJob>> ordered = [];
            foreach (ClaimedJob row in rows)
            {
                (int, string) key = (row.Job.RunAsUserId, NormalizedKeys(row.ConfirmedKeys));
                if (!partitions.TryGetValue(key, out List<ClaimedJob>? partition))
                {
                    partition = [];
                    partitions.Add(key, partition);
                    ordered.Add(partition);
                }

                partition.Add(row);
            }

            return ordered;
        }

        /// <summary>A row's confirmed keys in one spelling: distinct, ordered, comma-separated.</summary>
        /// <param name="confirmedKeys">The stored keys.</param>
        /// <returns>The normalised keys; empty when none.</returns>
        private static string NormalizedKeys(string? confirmedKeys)
        {
            return string.Join(PendingConfirmations.KeySeparator, PendingConfirmations.Split(confirmedKeys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        }

        /// <summary>Runs a claim whose handler's batches hold <typeparamref name="TItem" />.</summary>
        /// <typeparam name="TItem">The items' type.</typeparam>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="claim">The claim.</param>
        /// <param name="hostToken">The host's token.</param>
        /// <returns>How each claimed job's attempt ended.</returns>
        private async Task<IReadOnlyList<JobSettled>> RunClaimAsync<TItem>(int tenantId, JobClaim claim, CancellationToken hostToken)
            where TItem : class
        {
            IReadOnlyList<ClaimedJob> rows = claim.Rows.Value;
            if (rows.Count == 0)
            {
                return [];
            }

            long claimedAt = _time.GetTimestamp();
            JobHandlerRegistration handler = claim.Handler;
            IReadOnlyDictionary<int, object> entities = claim.Entities?.ByJobId() ?? new Dictionary<int, object>();
            using (_logger.BeginScope(new JobLogScope(tenantId, userId: null, rows.Select(static row => row.Job.Id), originTraceParent: null)))
            {
                JobsLog.JobClaimed(_logger, handler.Key, rows.Count);
            }

            // --- §5.1: the rows completed without the handler, the first rule that applies deciding.
            // Their errors record the trace of the activity the claim ran under ---
            string? claimTraceId = JobsActivities.CurrentTraceId();
            List<JobCompletion> withoutHandler = [];
            List<ClaimedJob> invoked = [];
            List<int> preCancelled = [];
            foreach (ClaimedJob row in rows)
            {
                Job job = row.Job;
                bool loaded = handler.EntityType is null || entities.ContainsKey(job.Id);
                if (job.CancelRequestedAt is not null && !(handler.Options.CommitsInSteps && loaded))
                {
                    withoutHandler.Add(new JobCompletion(job, JobOutcome.Cancelled(new JobOutcomeError(JobErrorCodes.Cancelled, null, claimTraceId), null)));
                }
                else if (job.CancelRequestedAt is not null)
                {
                    // A handler that commits in steps records the stop on its owning row itself.
                    invoked.Add(row);
                    preCancelled.Add(job.Id);
                }
                else if (job.Attempts > handler.Options.MaxAttempts)
                {
                    JobOutcomeError error = job.ErrorCode is string code
                        ? new JobOutcomeError(code, job.ErrorArgumentsJson, job.ErrorTraceId)
                        : new JobOutcomeError(JobErrorCodes.AttemptsExhausted, null, claimTraceId);
                    withoutHandler.Add(new JobCompletion(job, JobOutcome.Failed(error, null)));
                }
                else if (!loaded)
                {
                    withoutHandler.Add(new JobCompletion(job, JobOutcome.Cancelled(new JobOutcomeError(JobErrorCodes.EntityMissing, null, claimTraceId), null)));
                }
                else
                {
                    invoked.Add(row);
                }
            }

            List<JobSettled> settled = [];
            if (withoutHandler.Count > 0)
            {
                settled.AddRange(await CompleteAsSystemAsync(tenantId, handler, claim.Token, lease: null, withoutHandler, claimedAt).ConfigureAwait(false));
            }

            if (invoked.Count == 0)
            {
                return InClaimOrder(rows, settled);
            }

            // --- §3.5: the rows the handler runs, tracked under the claim's lease and run one partition after another ---
            using JobLease lease = _leases.Track(tenantId, handler, claim.Token, invoked, hostToken);
            foreach (int jobId in preCancelled)
            {
                lease.Slot(jobId)?.Cancel(JobCancellationReason.CancelRequested);
            }

            foreach (IReadOnlyList<ClaimedJob> partition in Partition(invoked))
            {
                settled.AddRange(await RunPartitionAsync<TItem>(tenantId, lease, partition, entities, claimedAt, hostToken).ConfigureAwait(false));
            }

            return InClaimOrder(rows, settled);
        }

        /// <summary>Orders how the claimed jobs ended as the claim returned their rows.</summary>
        /// <param name="rows">The claim's rows, in claim order.</param>
        /// <param name="settled">How each job ended: the rows completed without the handler first, then each partition's.</param>
        /// <returns>How each job ended, in claim order.</returns>
        private static IReadOnlyList<JobSettled> InClaimOrder(IReadOnlyList<ClaimedJob> rows, List<JobSettled> settled)
        {
            Dictionary<int, int> positions = new(rows.Count);
            for (int index = 0; index < rows.Count; index++)
            {
                positions[rows[index].Job.Id] = index;
            }

            return [.. settled.OrderBy(job => positions.GetValueOrDefault(job.JobId, int.MaxValue))];
        }

        /// <summary>Runs one partition: unless the lease was lost or the host stopped first, under its own <c>process</c> root and log scope.</summary>
        /// <typeparam name="TItem">The items' type.</typeparam>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="lease">The claim's lease.</param>
        /// <param name="rows">The partition's rows, in claim order.</param>
        /// <param name="entities">The claim's entity rows by job id.</param>
        /// <param name="claimedAt">When the claim's rows arrived.</param>
        /// <param name="hostToken">The host's token.</param>
        /// <returns>How each of the partition's jobs ended.</returns>
        private async Task<IReadOnlyList<JobSettled>> RunPartitionAsync<TItem>(
            int tenantId, JobLease lease, IReadOnlyList<ClaimedJob> rows, IReadOnlyDictionary<int, object> entities, long claimedAt, CancellationToken hostToken)
            where TItem : class
        {
            JobHandlerRegistration handler = lease.Handler;
            Job[] jobs = [.. rows.Select(static row => row.Job)];
            Activity? previous = Activity.Current;
            Activity? process = null;
            try
            {
                // --- A partition no handler started: a lost lease is another instance's; a stop releases it (§6.7) ---
                if (lease.IsLost)
                {
                    return Settled(jobs, JobSettlement.LostLease);
                }

                if (hostToken.IsCancellationRequested)
                {
                    BeginCompleting(lease, jobs);
                    return await CompleteAsSystemAsync(tenantId, handler, lease.Token, lease, [.. jobs.Select(static job => new JobCompletion(job, JobOutcome.Released()))], claimedAt)
                        .ConfigureAwait(false);
                }

                // --- §9: the invocation's own root, the jobs' ids and trace parent on every log line ---
                string? traceParent = jobs.Length == 1 ? jobs[0].TraceParent : null;
                process = JobsActivities.StartProcess(handler.Key, jobs);
                using (_logger.BeginScope(new JobLogScope(tenantId, jobs[0].RunAsUserId, jobs.Select(static job => job.Id), traceParent)))
                {
                    return await InvokeAsync<TItem>(new JobPartition(tenantId, lease, rows, JobsActivities.CurrentTraceId(), claimedAt), entities, traceParent, hostToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                // Whatever the attempt's end, the rows leave the lease, so no renewal extends a row nobody runs.
                lease.Leave(jobs.Select(static job => job.Id));
                process?.Dispose();
                Activity.Current = previous;
            }
        }

        // Spec 0020 §8: the job scope, opened inside `process <key>`. A tenant that left the states
        // served leaves the lease to lapse; a run-as user who is no longer an active member takes the
        // inactive path; a lease lost or a host stopping while the scope connects ends the attempt as
        // a lost lease or a release.
        /// <summary>Opens the partition's scope and runs the handler in it.</summary>
        /// <typeparam name="TItem">The items' type.</typeparam>
        /// <param name="partition">The partition.</param>
        /// <param name="entities">The claim's entity rows by job id.</param>
        /// <param name="traceParent">The job's trace parent when the partition holds one job.</param>
        /// <param name="hostToken">The host's token.</param>
        /// <returns>How each of the partition's jobs ended.</returns>
        private async Task<IReadOnlyList<JobSettled>> InvokeAsync<TItem>(
            JobPartition partition, IReadOnlyDictionary<int, object> entities, string? traceParent, CancellationToken hostToken)
            where TItem : class
        {
            JobLease lease = partition.Lease;
            TenantScope scope;
            try
            {
                scope = await _scopes.CreateScopeAsync(JobScopes.RunAs(partition.TenantId, partition.Jobs[0].RunAsUserId, traceParent), cancellationToken: lease.ScopeToken)
                    .ConfigureAwait(false);
            }
            catch (TenantNotFoundException)
            {
                return await InactiveAsync(partition, facts: null).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (lease.ScopeToken.IsCancellationRequested)
            {
                if (lease.IsLost)
                {
                    return Settled(partition.Jobs, JobSettlement.LostLease);
                }

                BeginCompleting(lease, partition.Jobs);
                return await CompleteAsSystemAsync(partition.TenantId, lease.Handler, lease.Token, lease, [.. partition.Jobs.Select(static job => new JobCompletion(job, JobOutcome.Released()))], partition.ClaimedAt)
                    .ConfigureAwait(false);
            }
            catch (Exception refused) when (refused is not OutOfMemoryException)
            {
                JobsLog.JobScopeUnavailable(_logger, refused, lease.Handler.Key);
                return Settled(partition.Jobs, JobSettlement.Lapsed);
            }

            await using (scope.ConfigureAwait(false))
            {
                // --- The scope's cultures, its operation name, and the consent the jobs carry ---
                IServiceProvider services = scope.Services;
                services.GetRequiredService<IRequestContextAccessor>().Current.ApplyCultures();
                services.GetRequiredService<DataAccessScope>().Operation = $"job:{lease.Handler.Key}";
                services.GetRequiredService<Confirmations>().Seed(PendingConfirmations.Split(partition.Rows[0].ConfirmedKeys));
                return await RunInScopeAsync<TItem>(partition, services, entities, hostToken).ConfigureAwait(false);
            }
        }

        /// <summary>Composes the completion batch, invokes the handler in the job frame, and completes the partition.</summary>
        /// <typeparam name="TItem">The items' type.</typeparam>
        /// <param name="partition">The partition.</param>
        /// <param name="services">The job scope's services.</param>
        /// <param name="entities">The claim's entity rows by job id.</param>
        /// <param name="hostToken">The host's token.</param>
        /// <returns>How each of the partition's jobs ended.</returns>
        private async Task<IReadOnlyList<JobSettled>> RunInScopeAsync<TItem>(
            JobPartition partition, IServiceProvider services, IReadOnlyDictionary<int, object> entities, CancellationToken hostToken)
            where TItem : class
        {
            JobLease lease = partition.Lease;
            JobHandlerRegistration handler = lease.Handler;
            IScopeConnection connection = services.GetRequiredService<IScopeConnection>();
            DataCancellation cancellation = services.GetRequiredService<DataCancellation>();
            IClientEventPublisher publisher = services.GetRequiredService<IClientEventPublisher>();

            // --- §5.4: the completion batch, composed before the handler runs: the prologue, then the
            // completion statement, whose outcomes are bound once known. It is created before the scope
            // token becomes the scope's, so it executes uncancelled (§6.6) ---
            var batch = (DataBatch)services.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            batch.ReserveExecution(CompletionBatchReserved);
            ConnectedUser user = await connection.CurrentAsync(CancellationToken.None).ConfigureAwait(false);
            BatchResult<ConnectResult> prologue = connection.Contribute(batch, connection.PremisesFor(user));
            List<JobOutcomeList> bound = [];
            _ = JobStatements.Complete(batch, lease.Token, bound);

            // --- §3.3: the items, in claim order, each bound to its slot's token, reason and progress ---
            List<JobItem<TItem>> items = new(partition.Rows.Count);
            List<JobSlot> slots = new(partition.Rows.Count);
            foreach (ClaimedJob row in partition.Rows)
            {
                JobSlot slot = lease.Slot(row.Job.Id) ?? throw new InvalidOperationException($"The job {row.Job.Id} left its lease before its handler ran.");
                JobItem<TItem>? item = null;
                item = new JobItem<TItem>
                {
                    Job = row.Job,
                    Item = (TItem)(entities.TryGetValue(row.Job.Id, out object? entity) ? entity : row.Job),
                    CancellationToken = slot.Token,
                    Progress = new JobItemProgress(lease, slot, batch, state => item!.StateJson = state, _renewal, _time, publisher, _text, _logger),
                    StateJson = row.StateJson,
                };
                slot.Bind(item.TrySetCancellationReason);
                items.Add(item);
                slots.Add(slot);
            }

            // --- §8: the invocation, in the job frame bound to the completion batch, its round trips
            // under the scope token; the cultures are applied again once it returned (an async callee
            // cannot change its caller's) ---
            var frame = OpenWriteFrame.ForJob(
                services.GetRequiredService<SaveOperation>(), handler.Key, handler.HandlerType, handler.Options.RunsWhenReadOnly, batch, lease.ScopeToken);
            JobBatch<TItem> jobBatch = new() { Items = items, Batch = batch };
            Exception? thrown = null;
            cancellation.Token = lease.ScopeToken;
            long invokedAt = _time.GetTimestamp();
            try
            {
                object instance = services.GetRequiredService(handler.HandlerType);
                _ = await frame.RunAsync(async () =>
                {
                    await InvokeAsync(instance, jobBatch).ConfigureAwait(false);
                    return true;
                }).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                thrown = failure;
            }
            finally
            {
                cancellation.Token = CancellationToken.None;
                services.GetRequiredService<IRequestContextAccessor>().Current.ApplyCultures();
                _metrics.RecordDuration(handler.Key, _time.GetElapsedTime(invokedAt));
            }

            // --- §3.4: the ending, then each item's outcome ---
            bool cancelled = lease.ScopeToken.IsCancellationRequested || items.Any(static item => item.CancellationToken.IsCancellationRequested);
            var ending = JobEnding.Of(thrown, cancelled, hostToken.IsCancellationRequested);
            if (ending.Kind == JobEndingKind.Threw)
            {
                JobsLog.JobHandlerFailed(_logger, thrown!, handler.Key);
            }

            List<JobItemFacts> facts = [.. items.Select((item, index) => new JobItemFacts(
                item.Job.Attempts, partition.Rows[index].ConfirmedKeys, item.CancellationReason, item.Mark, slots[index].Observed, item.StateJson))];
            IReadOnlyList<JobOutcome>? outcomes = JobOutcomeRules.Decide(facts, ending, partition.Policy, partition.TraceId, Random.Shared);
            if (outcomes is null)
            {
                frame.EndCompletion(new OperationCanceledException("The job's lease was lost.", thrown));
                LeaseLost(partition.TenantId, handler, lease);
                return Settled(partition.Jobs, JobSettlement.LostLease);
            }

            // --- §5.6: the completion, under `settle <key>`; the rows are no longer judged by a renewal ---
            foreach (JobSlot slot in slots)
            {
                slot.BeginCompleting();
            }

            List<JobCompletion> completions = [.. partition.Jobs.Select((job, index) => new JobCompletion(job, outcomes[index]))];
            IReadOnlyList<IReadOnlyList<NotificationRequest>> successNotices = [.. items.Select(static item => item.SuccessNotifications)];
            JobMainCompletion main = new(services, frame, batch, prologue, user, bound, facts, thrown, ending);
            using Activity? settle = JobsActivities.StartSettle(handler.Key, partition.Jobs);
            (IReadOnlyList<JobSettled> settled, Exception? end) = await SendAsync(partition, main, completions, successNotices).ConfigureAwait(false);
            frame.EndCompletion(end);
            return settled;
        }

        // Spec 0020 §5.6: the composed batch is sent as it stands, unless the frame's groups could not
        // be composed onto it or the worker's own statements could not follow them; then the handler's
        // work is discarded and the outcomes are recorded alone, mapped as a failure of the invocation.
        /// <summary>Completes a partition with its composed batch, answering every failure path.</summary>
        /// <param name="partition">The partition.</param>
        /// <param name="main">The composed batch and what its failures need.</param>
        /// <param name="completions">The outcomes, in claim order.</param>
        /// <param name="successNotices">Each item's notifications on success, in claim order.</param>
        /// <returns>How each job ended, and how the composed batch failed: null when it committed.</returns>
        private async Task<(IReadOnlyList<JobSettled> Settled, Exception? Failure)> SendAsync(
            JobPartition partition, JobMainCompletion main, IReadOnlyList<JobCompletion> completions, IReadOnlyList<IReadOnlyList<NotificationRequest>> successNotices)
        {
            JobHandlerRegistration handler = partition.Lease.Handler;
            IServiceProvider services = main.Services;
            IScopeConnection connection = services.GetRequiredService<IScopeConnection>();

            // --- The worker's statements after the handler's: the outcomes, the notices, the events, the nudge ---
            Exception? unsent = main.Frame.CompletionBroken || !main.Batch.IsComposing
                ? main.Thrown ?? new InvalidOperationException("The job's completion batch was left unfit to send by the work composed onto it.")
                : null;
            if (unsent is null)
            {
                try
                {
                    IReadOnlyList<int> administrators = await AdministratorsAsync(services, handler, completions).ConfigureAwait(false);
                    main.Bound.AddRange(Rows(completions));
                    AppendWorkerStatements(main.Batch, partition.TenantId, completions, Notices(completions, handler.Options, administrators, successNotices), services);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    unsent = failure;
                }
            }

            if (unsent is not null)
            {
                JobsLog.JobCompletionFailed(_logger, unsent, handler.Key, FreshNext);
                return (await FreshAsync(partition, services, Remap(partition, main.Facts, JobEnding.OfUnsent(main.Ending, unsent))).ConfigureAwait(false), unsent);
            }

            // --- The round trip, and each failure path of §5.6; only the worker sends the batch ---
            main.Batch.ReleaseExecution();
            try
            {
                BatchOutcome outcome = await main.Batch.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
                ConnectResult result = main.Prologue.Value;

                // Applied whether or not the guard passed; a run-as user found inactive refuses here.
                _ = connection.Apply(result, main.User);
                if (result.GuardPassed)
                {
                    await AfterCommitAsync(main.Frame, outcome, handler).ConfigureAwait(false);
                    Completed(partition.TenantId, handler, completions, partition.ClaimedAt);
                    return (Settled(completions), null);
                }

                // A guard that failed at the prologue: the body never ran.
                return (await GuardFailedAsync(partition, services, main.Facts).ConfigureAwait(false), new StaleContextException([]));
            }
            catch (TenantNotFoundException inactive)
            {
                return (await InactiveAsync(partition, main.Facts).ConfigureAwait(false), inactive);
            }
            catch (BatchAssertionFailedException lost) when (JobCompletionFailures.IsLeaseLost(lost))
            {
                LeaseLost(partition.TenantId, handler, partition.Lease);
                return (Settled(partition.Jobs, JobSettlement.LostLease), lost);
            }
            catch (BatchAssertionFailedException refused) when (refused.Number == TellmaSqlErrors.CallerInvalid)
            {
                // The run-as user was deactivated between the prologue and the transaction's re-check.
                connection.Drop(main.User);
                return (await InactiveAsync(partition, main.Facts).ConfigureAwait(false), refused);
            }
            catch (BatchAssertionFailedException stale) when (stale.Number == TellmaSqlErrors.StaleVersionTag)
            {
                // The re-check refused the persist after the guard passed: what the prologue read is
                // applied, then the work runs again under it.
                try
                {
                    if (connection.ReadOf(main.Prologue) is ConnectResult read)
                    {
                        _ = connection.Apply(read, main.User);
                    }
                }
                catch (TenantNotFoundException)
                {
                    return (await InactiveAsync(partition, main.Facts).ConfigureAwait(false), stale);
                }

                return (await GuardFailedAsync(partition, services, main.Facts).ConfigureAwait(false), stale);
            }
            catch (DataAccessAmbiguousException ambiguous) when (ambiguous.Verdict == CommitVerdict.Committed)
            {
                // The probe found the batch committed; its response was lost with the connection.
                await AfterCommitAsync(main.Frame, committed: null, handler).ConfigureAwait(false);
                Completed(partition.TenantId, handler, completions, partition.ClaimedAt);
                return (Settled(completions), null);
            }
            catch (DataAccessAmbiguousException ambiguous)
            {
                JobsLog.JobCompletionFailed(_logger, ambiguous, handler.Key, LapseNext);
                return (Settled(partition.Jobs, JobSettlement.Lapsed), ambiguous);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                // A final failure: a group's error is the frame's to translate, a handler statement's
                // §14.2's at the root.
                Exception translated = failure is DataAccessException data
                    ? main.Frame.TranslateCompletionFailure(data) ?? JobCompletionFailures.Translate(data) ?? failure
                    : failure;
                JobsLog.JobCompletionFailed(_logger, failure, handler.Key, FreshNext);
                return (await FreshAsync(partition, services, Remap(partition, main.Facts, JobEnding.OfFailure(translated))).ConfigureAwait(false), failure);
            }
        }

        /// <summary>The outcomes after the composed batch failed: the work was discarded, so each item follows the failure as an ending of the invocation.</summary>
        /// <param name="partition">The partition.</param>
        /// <param name="facts">The items' facts, in claim order.</param>
        /// <param name="failure">The ending the failure stands for.</param>
        /// <returns>The completions.</returns>
        private static List<JobCompletion> Remap(JobPartition partition, IReadOnlyList<JobItemFacts> facts, JobEnding failure)
        {
            return [.. partition.Jobs.Select((job, index) => new JobCompletion(
                job, JobOutcomeRules.AfterFailedCompletion(facts[index], failure, partition.Policy, partition.TraceId, Random.Shared)))];
        }

        /// <summary>Answers a failed guard: the work runs again at once under the fresh permissions, in a completion of the outcomes alone.</summary>
        /// <param name="partition">The partition.</param>
        /// <param name="services">The job scope's services.</param>
        /// <param name="facts">The items' facts, in claim order.</param>
        /// <returns>How each job ended.</returns>
        private Task<IReadOnlyList<JobSettled>> GuardFailedAsync(JobPartition partition, IServiceProvider services, IReadOnlyList<JobItemFacts> facts)
        {
            List<JobCompletion> retries = [.. partition.Jobs.Select((job, index) => new JobCompletion(
                job, JobOutcomeRules.AfterGuardFailure(facts[index], partition.Policy, partition.TraceId)))];
            return FreshAsync(partition, services, retries);
        }

        /// <summary>
        ///     Completes a partition with a fresh batch of the outcomes and the worker's notices alone, in
        ///     the job scope; an inactive run-as user takes the inactive path, and any other failure
        ///     leaves the lease to lapse.
        /// </summary>
        /// <param name="partition">The partition.</param>
        /// <param name="services">The job scope's services.</param>
        /// <param name="completions">The outcomes, in claim order.</param>
        /// <returns>How each job ended.</returns>
        private async Task<IReadOnlyList<JobSettled>> FreshAsync(JobPartition partition, IServiceProvider services, IReadOnlyList<JobCompletion> completions)
        {
            JobHandlerRegistration handler = partition.Lease.Handler;
            try
            {
                return await CompleteAsync(services, partition.TenantId, handler, partition.Lease.Token, completions, partition.ClaimedAt).ConfigureAwait(false);
            }
            catch (TenantNotFoundException)
            {
                return await InactiveAsync(partition, facts: null).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                return Unsettled(partition.TenantId, handler, partition.Lease, partition.Jobs, failure);
            }
        }

        /// <summary>
        ///     The inactive path: the run-as user is no longer an active member, so every job of
        ///     the partition fails with <see cref="JobErrorCodes.UserInactive" />, completed in the
        ///     system scope with the trace of the invocation's activity.
        /// </summary>
        /// <param name="partition">The partition.</param>
        /// <param name="facts">The items' facts once the handler ran, whose checkpoints are kept; null when it never ran.</param>
        /// <returns>How each job ended.</returns>
        private Task<IReadOnlyList<JobSettled>> InactiveAsync(JobPartition partition, IReadOnlyList<JobItemFacts>? facts)
        {
            JobOutcomeError error = new(JobErrorCodes.UserInactive, null, partition.TraceId);
            List<JobCompletion> failed = [.. partition.Jobs.Select((job, index) => new JobCompletion(job, JobOutcome.Failed(error, facts?[index].StateJson)))];
            BeginCompleting(partition.Lease, partition.Jobs);
            return CompleteAsSystemAsync(partition.TenantId, partition.Lease.Handler, partition.Lease.Token, partition.Lease, failed, partition.ClaimedAt);
        }

        // Spec 0020 §5.2: a renewal no longer judges a row whose completion is being sent, which may
        // already have ended it; every completion of a tracked partition marks its slots first, so a
        // renewal racing it does not take the row's absence for a lost lease.
        /// <summary>Marks the jobs' slots as completing, so no renewal judges a row its completion may already have ended.</summary>
        /// <param name="lease">The claim's lease.</param>
        /// <param name="jobs">The jobs being completed.</param>
        private static void BeginCompleting(JobLease lease, IEnumerable<Job> jobs)
        {
            foreach (Job job in jobs)
            {
                lease.Slot(job.Id)?.BeginCompleting();
            }
        }

        /// <summary>
        ///     Completes jobs in the system scope: the rows no handler may run, the partitions a
        ///     stop released, and the jobs of an inactive run-as user. Under its own <c>settle</c> span;
        ///     a failure leaves the lease to lapse.
        /// </summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="handler">The jobs' handler.</param>
        /// <param name="token">The claim's token.</param>
        /// <param name="lease">The claim's lease; null for rows no handler ran, which were never tracked.</param>
        /// <param name="completions">The outcomes, in claim order.</param>
        /// <param name="claimedAt">When the claim's rows arrived.</param>
        /// <returns>How each job ended.</returns>
        private async Task<IReadOnlyList<JobSettled>> CompleteAsSystemAsync(
            int tenantId, JobHandlerRegistration handler, Guid token, JobLease? lease, IReadOnlyList<JobCompletion> completions, long claimedAt)
        {
            Job[] jobs = [.. completions.Select(static completion => completion.Job)];
            using Activity? settle = JobsActivities.StartSettle(handler.Key, jobs);
            using IDisposable? logScope = _logger.BeginScope(new JobLogScope(tenantId, userId: null, jobs.Select(static job => job.Id), originTraceParent: null));
            try
            {
                TenantScope scope = await _scopes.CreateScopeAsync(JobScopes.System(tenantId)).ConfigureAwait(false);
                await using (scope.ConfigureAwait(false))
                {
                    scope.Services.GetRequiredService<IRequestContextAccessor>().Current.ApplyCultures();
                    return await CompleteAsync(scope.Services, tenantId, handler, token, completions, claimedAt).ConfigureAwait(false);
                }
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                return Unsettled(tenantId, handler, lease, jobs, failure);
            }
        }

        /// <summary>
        ///     Sends a completion of the outcomes and the worker's notices alone through the scope's
        ///     guarded runner: the connect prologue for the scope's user heads it, and a failed guard
        ///     recomposes it once.
        /// </summary>
        /// <param name="services">The scope's services.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="handler">The jobs' handler.</param>
        /// <param name="token">The claim's token.</param>
        /// <param name="completions">The outcomes, in claim order.</param>
        /// <param name="claimedAt">When the claim's rows arrived.</param>
        /// <returns>How each job ended: completed.</returns>
        private async Task<IReadOnlyList<JobSettled>> CompleteAsync(
            IServiceProvider services, int tenantId, JobHandlerRegistration handler, Guid token, IReadOnlyList<JobCompletion> completions, long claimedAt)
        {
            IReadOnlyList<int> administrators = await AdministratorsAsync(services, handler, completions).ConfigureAwait(false);
            List<NotificationRequest> notices = Notices(completions, handler.Options, administrators, successNotices: null);
            IGuardedBatchRunner runner = services.GetRequiredService<IGuardedBatchRunner>();
            try
            {
                _ = await runner.RunAsync(
                    BatchPurpose.Persist,
                    (user, batch) =>
                    {
                        var data = (DataBatch)batch;
                        _ = JobStatements.Complete(data, token, Rows(completions));
                        AppendWorkerStatements(data, tenantId, completions, notices, services);
                        return ValueTask.CompletedTask;
                    },
                    static (_, outcome) => outcome,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (DataAccessAmbiguousException ambiguous) when (ambiguous.Verdict == CommitVerdict.Committed)
            {
                // The probe found the batch committed; its response was lost with the connection.
            }

            Completed(tenantId, handler, completions, claimedAt);
            return Settled(completions);
        }

        /// <summary>
        ///     Appends what follows the completion statement and the handler's work: the notices, then
        ///     <c>job.changed</c> to each requester and the nudge of a job queued again at once, both on
        ///     the commit.
        /// </summary>
        /// <param name="batch">The completion batch.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="completions">The outcomes.</param>
        /// <param name="notices">The notices.</param>
        /// <param name="services">The scope's services.</param>
        private void AppendWorkerStatements(DataBatch batch, int tenantId, IReadOnlyList<JobCompletion> completions, List<NotificationRequest> notices, IServiceProvider services)
        {
            if (notices.Count > 0)
            {
                services.GetRequiredService<INotifier>().Notify(batch, notices);
            }

            IClientEventPublisher publisher = services.GetRequiredService<IClientEventPublisher>();
            foreach (JobCompletion completion in completions)
            {
                if (completion.Job.RequestedById is int requester)
                {
                    publisher.Publish(batch, JobNotices.Changed(completion.Job.Id, requester));
                }
            }

            if (completions.Any(static completion => completion.Outcome.RunsAgainNow))
            {
                batch.OnCommitted(_ => _wake.Wake(tenantId));
            }
        }

        /// <summary>The notices of the outcomes, and on the composed batch the success notifications of each job that succeeded.</summary>
        /// <param name="completions">The outcomes, in claim order.</param>
        /// <param name="options">The handler's options.</param>
        /// <param name="administrators">The tenant's administrators.</param>
        /// <param name="successNotices">Each job's notifications on success, in claim order; null for a completion of the outcomes alone.</param>
        /// <returns>The requests.</returns>
        private static List<NotificationRequest> Notices(
            IReadOnlyList<JobCompletion> completions, JobHandlerAttribute options, IReadOnlyList<int> administrators, IReadOnlyList<IReadOnlyList<NotificationRequest>>? successNotices)
        {
            List<NotificationRequest> requests = [];
            for (int index = 0; index < completions.Count; index++)
            {
                JobCompletion completion = completions[index];
                if (JobNotices.Of(completion.Job, completion.Outcome, options, administrators) is NotificationRequest notice)
                {
                    requests.Add(notice);
                }

                if (successNotices is not null && completion.Outcome.Kind == JobOutcomeKind.Succeeded)
                {
                    requests.AddRange(successNotices[index]);
                }
            }

            return requests;
        }

        /// <summary>The tenant's administrators, read only when a notice has no requester to go to; none when they cannot be read.</summary>
        /// <param name="services">The scope's services.</param>
        /// <param name="handler">The jobs' handler.</param>
        /// <param name="completions">The outcomes.</param>
        /// <returns>The administrators' ids.</returns>
        private async Task<IReadOnlyList<int>> AdministratorsAsync(IServiceProvider services, JobHandlerRegistration handler, IReadOnlyList<JobCompletion> completions)
        {
            if (!completions.Any(static completion => completion.Job.RequestedById is null && JobNotices.Raises(completion.Outcome)))
            {
                return [];
            }

            try
            {
                return await services.GetRequiredService<IAdministratorDirectory>().GetAdministratorIdsAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                // The outcomes still complete; only the notices without a recipient are lost.
                JobsLog.JobNoticeRecipientsFailed(_logger, failure, handler.Key);
                return [];
            }
        }

        /// <summary>Runs the post-commit work of the writes the handler enlisted; its failure is logged, the commit stands.</summary>
        /// <param name="frame">The job frame.</param>
        /// <param name="committed">The batch's outcome; null when the commit was proven but its response lost.</param>
        /// <param name="handler">The jobs' handler.</param>
        /// <returns>A task that completes when the post-commit work ended.</returns>
        private async Task AfterCommitAsync(OpenWriteFrame frame, BatchOutcome? committed, JobHandlerRegistration handler)
        {
            if (!frame.AppendedToCompletion)
            {
                return;
            }

            try
            {
                await frame.AfterCompletionAsync(committed).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                JobsLog.JobCompletionFailed(_logger, failure, handler.Key, PostCommitNext);
            }
        }

        /// <summary>Counts a committed completion's outcomes, and logs each in its own scope, its error rendered in English.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="handler">The jobs' handler.</param>
        /// <param name="completions">The outcomes.</param>
        /// <param name="claimedAt">When the claim's rows arrived.</param>
        private void Completed(int tenantId, JobHandlerRegistration handler, IReadOnlyList<JobCompletion> completions, long claimedAt)
        {
            _metrics.RecordCompleted(handler.Key, completions.Select(static completion => completion.Outcome.Kind));
            if (!_logger.IsEnabled(LogLevel.Information))
            {
                return;
            }

            double durationMs = _time.GetElapsedTime(claimedAt).TotalMilliseconds;
            foreach (JobCompletion completion in completions)
            {
                JobOutcome outcome = completion.Outcome;
                string? error = outcome.ErrorCode is string code && outcome.Kind is JobOutcomeKind.Failed or JobOutcomeKind.Retry
                    ? _text.Render(handler.HandlerType, code, outcome.ErrorArgumentsJson)
                    : null;
                string kind = outcome.Kind.ToString();
                using (_logger.BeginScope(new JobLogScope(tenantId, completion.Job.RunAsUserId, [completion.Job.Id], completion.Job.TraceParent)))
                {
                    JobsLog.JobCompleted(_logger, handler.Key, kind, durationMs, error);
                }
            }
        }

        /// <summary>The end of a completion that recorded nothing: a lost lease, else a lapse, each logged.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="handler">The jobs' handler.</param>
        /// <param name="lease">The claim's lease; null for rows that were never tracked.</param>
        /// <param name="jobs">The jobs.</param>
        /// <param name="failure">The failure.</param>
        /// <returns>How each job ended.</returns>
        private List<JobSettled> Unsettled(int tenantId, JobHandlerRegistration handler, JobLease? lease, IReadOnlyList<Job> jobs, Exception failure)
        {
            if (JobCompletionFailures.IsLeaseLost(failure))
            {
                LeaseLost(tenantId, handler, lease);
                return Settled(jobs, JobSettlement.LostLease);
            }

            JobsLog.JobCompletionFailed(_logger, failure, handler.Key, LapseNext);
            return Settled(jobs, JobSettlement.Lapsed);
        }

        /// <summary>Logs and counts a lost lease, once per lease however many paths find it lost; an untracked claim's loss each time.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="handler">The jobs' handler.</param>
        /// <param name="lease">The claim's lease; null for rows that were never tracked.</param>
        private void LeaseLost(int tenantId, JobHandlerRegistration handler, JobLease? lease)
        {
            if (lease is not null)
            {
                JobRenewal.ReportLoss(_logger, _metrics, tenantId, lease);
                return;
            }

            _metrics.RecordLeaseLost(handler.Key);
            JobsLog.JobLeaseLost(_logger, handler.Key);
        }

        /// <summary>The completion statement's rows.</summary>
        /// <param name="completions">The outcomes.</param>
        /// <returns>The rows.</returns>
        private static List<JobOutcomeList> Rows(IReadOnlyList<JobCompletion> completions)
        {
            return [.. completions.Select(static completion => completion.Outcome.ToRow(completion.Job.Id))];
        }

        /// <summary>The settlements of completed jobs.</summary>
        /// <param name="completions">The outcomes.</param>
        /// <returns>The settlements.</returns>
        private static List<JobSettled> Settled(IReadOnlyList<JobCompletion> completions)
        {
            return [.. completions.Select(static completion => new JobSettled(completion.Job.Id, JobSettlement.Completed, completion.Outcome))];
        }

        /// <summary>The settlements of jobs whose attempt recorded nothing.</summary>
        /// <param name="jobs">The jobs.</param>
        /// <param name="settlement">How the attempts ended.</param>
        /// <returns>The settlements.</returns>
        private static List<JobSettled> Settled(IReadOnlyList<Job> jobs, JobSettlement settlement)
        {
            return [.. jobs.Select(job => new JobSettled(job.Id, settlement, null))];
        }

        /// <summary>Invokes a handler with a batch of its items.</summary>
        /// <typeparam name="TItem">The items' type.</typeparam>
        /// <param name="handler">The handler.</param>
        /// <param name="batch">The batch.</param>
        /// <returns>The handler's task.</returns>
        private static Task InvokeAsync<TItem>(object handler, JobBatch<TItem> batch)
            where TItem : class
        {
            if (handler is IJobHandler jobs)
            {
                return jobs.ExecuteAsync((JobBatch<Job>)(object)batch);
            }

            Func<object, object, Task> invoke = EntityInvokers.GetOrAdd(
                typeof(TItem),
                static type => InvokeEntityMethod.MakeGenericMethod(type).CreateDelegate<Func<object, object, Task>>());
            return invoke(handler, batch);
        }

        /// <summary>Invokes an entity-backed handler.</summary>
        /// <typeparam name="TEntity">The entity type its interface names.</typeparam>
        /// <param name="handler">The handler.</param>
        /// <param name="batch">The batch.</param>
        /// <returns>The handler's task.</returns>
        private static Task InvokeEntityAsync<TEntity>(object handler, object batch)
            where TEntity : class, IJobEntity
        {
            return ((IEntityJobHandler<TEntity>)handler).ExecuteAsync((JobBatch<TEntity>)batch);
        }
    }

    /// <summary>One partition of a claim as its completion paths read it.</summary>
    /// <param name="TenantId">The tenant.</param>
    /// <param name="Lease">The claim's lease.</param>
    /// <param name="Rows">The partition's rows, in claim order.</param>
    /// <param name="TraceId">The trace id of the invocation's activity; null when none was recorded.</param>
    /// <param name="ClaimedAt">When the claim's rows arrived, as a timestamp of the runner's clock.</param>
    internal sealed record JobPartition(int TenantId, JobLease Lease, IReadOnlyList<ClaimedJob> Rows, string? TraceId, long ClaimedAt)
    {
        /// <summary>The partition's jobs, in claim order.</summary>
        public IReadOnlyList<Job> Jobs { get; } = [.. Rows.Select(static row => row.Job)];

        /// <summary>The handler's retry policy.</summary>
        public JobRetryPolicy Policy { get; } = JobRetryPolicy.Of(Lease.Handler.Options);
    }

    /// <summary>The composed completion batch of a partition and what its failure paths need.</summary>
    /// <param name="Services">The job scope's services.</param>
    /// <param name="Frame">The job frame bound to the batch.</param>
    /// <param name="Batch">The batch.</param>
    /// <param name="Prologue">The batch's connect prologue.</param>
    /// <param name="User">The user the prologue was composed for.</param>
    /// <param name="Bound">The outcome rows the completion statement binds, filled once known.</param>
    /// <param name="Facts">The items' facts, in claim order.</param>
    /// <param name="Thrown">What the handler threw; null when it returned.</param>
    /// <param name="Ending">How the invocation ended, as its outcomes were decided.</param>
    internal sealed record JobMainCompletion(
        IServiceProvider Services,
        OpenWriteFrame Frame,
        DataBatch Batch,
        BatchResult<ConnectResult> Prologue,
        ConnectedUser User,
        List<JobOutcomeList> Bound,
        IReadOnlyList<JobItemFacts> Facts,
        Exception? Thrown,
        JobEnding Ending);
}
