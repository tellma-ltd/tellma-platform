// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §13: the worker's instruments on the core meter. Tags are bounded: the handler key
    // (bounded by the registry) and the outcome; the tenant, the job and the user never tag an
    // instrument. The orphaned gauge reports the sum of this instance's latest sample of each tenant
    // it polls, which the poller drops when it retires.
    /// <summary>The job worker's and the scheduler's instruments.</summary>
    internal sealed class JobsMetrics
    {
        /// <summary>The outcome value of a job that succeeded.</summary>
        public const string Succeeded = "succeeded";

        /// <summary>The outcome value of a job queued again with a backoff.</summary>
        public const string Retry = "retry";

        /// <summary>The outcome value of a job that failed.</summary>
        public const string Failed = "failed";

        /// <summary>The outcome value of a job queued again untouched, its attempt not counted.</summary>
        public const string Released = "released";

        /// <summary>The outcome value of a job that was cancelled.</summary>
        public const string Cancelled = "cancelled";

        /// <summary>The outcome value of a job waiting for its initiator to confirm warnings.</summary>
        public const string PendingConfirmation = "pending_confirmation";

        /// <summary>Jobs claimed.</summary>
        private readonly Counter<long> _claimed;

        /// <summary>Jobs completed, by outcome.</summary>
        private readonly Counter<long> _completed;

        /// <summary>Handler invocations' durations.</summary>
        private readonly Histogram<double> _duration;

        /// <summary>The wait from due to first claim.</summary>
        private readonly Histogram<double> _queueLatency;

        /// <summary>Each claim's rows over the handler's batch size.</summary>
        private readonly Histogram<double> _batchFill;

        /// <summary>Jobs running on this instance.</summary>
        private readonly UpDownCounter<long> _inFlight;

        /// <summary>Batches whose lease was lost.</summary>
        private readonly Counter<long> _leaseLost;

        /// <summary>The age of each key's oldest queued job, sampled per minute.</summary>
        private readonly Histogram<double> _backlogAge;

        /// <summary>Each poll round trip's duration.</summary>
        private readonly Histogram<double> _pollDuration;

        /// <summary>Polls that found the worker away past the gap threshold.</summary>
        private readonly Counter<long> _gapDetected;

        /// <summary>The latest orphan sample of each tenant this instance polls.</summary>
        private readonly ConcurrentDictionary<int, long> _orphaned = new();

        /// <summary>Creates the instruments.</summary>
        /// <param name="meterFactory">The host's meter factory, which owns the meter's lifetime.</param>
        public JobsMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

            Meter meter = meterFactory.Create(JobsTelemetryNames.MeterName);
            _claimed = meter.CreateCounter<long>(JobsTelemetryNames.Claimed, "{job}", "Jobs claimed, by handler.");
            _completed = meter.CreateCounter<long>(JobsTelemetryNames.Completed, "{job}", "Jobs whose completion committed, by handler and outcome.");
            _duration = meter.CreateHistogram<double>(JobsTelemetryNames.Duration, "s", "How long each handler invocation ran, by handler.");
            _queueLatency = meter.CreateHistogram<double>(JobsTelemetryNames.QueueLatency, "s", "How long each job waited past its due time before its first claim, by handler.");
            _batchFill = meter.CreateHistogram<double>(JobsTelemetryNames.BatchFill, "1", "The jobs each claim leased over the handler's batch size, by handler.");
            _inFlight = meter.CreateUpDownCounter<long>(JobsTelemetryNames.InFlight, "{job}", "Jobs running on this instance, by handler.");
            _leaseLost = meter.CreateCounter<long>(JobsTelemetryNames.LeaseLost, "{batch}", "Batches whose lease was lost, by handler.");
            _backlogAge = meter.CreateHistogram<double>(JobsTelemetryNames.BacklogAge, "s", "The age of each key's oldest queued job past its due time, sampled per minute, by handler.");
            _pollDuration = meter.CreateHistogram<double>(JobsTelemetryNames.PollDuration, "s", "How long each tenant's poll round trip took.");
            _ = meter.CreateObservableGauge(
                JobsTelemetryNames.Orphaned,
                OrphanedTotal,
                "{job}",
                "Queued jobs overdue by an hour whose key no handler on this instance registers and no feature retires, summed over this instance's latest sample of each tenant.");
            _gapDetected = meter.CreateCounter<long>(JobsTelemetryNames.SchedulesGapDetected, "{tick}", "Polls that found the worker had been away past the gap threshold.");
        }

        /// <summary>The outcome tag's value of an outcome kind.</summary>
        /// <param name="kind">The kind.</param>
        /// <returns>The value.</returns>
        public static string OutcomeValue(JobOutcomeKind kind)
        {
            return kind switch
            {
                JobOutcomeKind.Succeeded => Succeeded,
                JobOutcomeKind.Retry => Retry,
                JobOutcomeKind.Failed => Failed,
                JobOutcomeKind.Released => Released,
                JobOutcomeKind.Cancelled => Cancelled,
                JobOutcomeKind.PendingConfirmation => PendingConfirmation,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The outcome kind is not defined."),
            };
        }

        /// <summary>Records one claim that leased rows: their count, how full it was, and the queue latency of each row claimed for the first time.</summary>
        /// <param name="handler">The claim's handler.</param>
        /// <param name="rows">The rows the claim leased.</param>
        public void RecordClaim(JobHandlerRegistration handler, IReadOnlyList<ClaimedJob> rows)
        {
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentNullException.ThrowIfNull(rows);

            // An empty claim leased nothing: idle polls would otherwise drown the fill of real claims.
            if (rows.Count == 0)
            {
                return;
            }

            KeyValuePair<string, object?> tag = HandlerTag(handler.Key);
            _batchFill.Record((double)rows.Count / handler.Options.BatchSize, tag);
            _claimed.Add(rows.Count, tag);
            foreach (ClaimedJob row in rows)
            {
                if (QueueLatency(row.Job, handler.Options.LeaseSeconds) is TimeSpan latency)
                {
                    _queueLatency.Record(latency.TotalSeconds, tag);
                }
            }
        }

        /// <summary>Records one committed completion's outcomes.</summary>
        /// <param name="handlerKey">The jobs' handler key.</param>
        /// <param name="outcomes">The outcomes recorded.</param>
        public void RecordCompleted(string handlerKey, IEnumerable<JobOutcomeKind> outcomes)
        {
            ArgumentNullException.ThrowIfNull(handlerKey);
            ArgumentNullException.ThrowIfNull(outcomes);
            foreach (IGrouping<JobOutcomeKind, JobOutcomeKind> kind in outcomes.GroupBy(static kind => kind))
            {
                _completed.Add(kind.Count(), HandlerTag(handlerKey), new KeyValuePair<string, object?>(JobsTelemetryNames.OutcomeTag, OutcomeValue(kind.Key)));
            }
        }

        /// <summary>Records how long one handler invocation ran.</summary>
        /// <param name="handlerKey">The handler key.</param>
        /// <param name="duration">The invocation's duration.</param>
        public void RecordDuration(string handlerKey, TimeSpan duration)
        {
            _duration.Record(duration.TotalSeconds, HandlerTag(handlerKey));
        }

        /// <summary>Adds jobs to, or with a negative count removes them from, those running on this instance.</summary>
        /// <param name="handlerKey">The jobs' handler key.</param>
        /// <param name="count">The change.</param>
        public void AddInFlight(string handlerKey, int count)
        {
            if (count != 0)
            {
                _inFlight.Add(count, HandlerTag(handlerKey));
            }
        }

        /// <summary>Counts one batch whose lease was lost.</summary>
        /// <param name="handlerKey">The batch's handler key.</param>
        public void RecordLeaseLost(string handlerKey)
        {
            _leaseLost.Add(1, HandlerTag(handlerKey));
        }

        /// <summary>Records the age of a key's oldest queued job.</summary>
        /// <param name="handlerKey">The key.</param>
        /// <param name="age">How long the job is overdue.</param>
        public void RecordBacklogAge(string handlerKey, TimeSpan age)
        {
            _backlogAge.Record(Math.Max(0, age.TotalSeconds), HandlerTag(handlerKey));
        }

        /// <summary>Records one poll round trip's duration.</summary>
        /// <param name="duration">The duration.</param>
        public void RecordPollDuration(TimeSpan duration)
        {
            _pollDuration.Record(duration.TotalSeconds);
        }

        /// <summary>Counts one poll that found the worker away past the gap threshold.</summary>
        public void RecordGapDetected()
        {
            _gapDetected.Add(1);
        }

        /// <summary>Keeps a tenant's latest orphan sample, which the gauge sums.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="count">The orphaned jobs the sample counted.</param>
        public void SetOrphaned(int tenantId, long count)
        {
            _orphaned[tenantId] = count;
        }

        /// <summary>Drops a tenant's orphan sample once its poller retired.</summary>
        /// <param name="tenantId">The tenant.</param>
        public void DropOrphaned(int tenantId)
        {
            _ = _orphaned.TryRemove(tenantId, out _);
        }

        /// <summary>The orphan gauge's value: the latest sample of every tenant this instance polls, summed.</summary>
        /// <returns>The sum.</returns>
        private long OrphanedTotal()
        {
            return _orphaned.Values.Sum();
        }

        // Spec 0020 §13: the latency is StartedAt − DueAt at the first claim. The claim stamps
        // StartedAt only when it is null and sets the lease to expire LeaseSeconds after the same
        // instant, so a row whose start is that instant was claimed for the first time now.
        /// <summary>A claimed row's wait past its due time, when this claim was its first; null otherwise.</summary>
        /// <param name="job">The row, as the claim returned it.</param>
        /// <param name="leaseSeconds">The length of the lease the claim took.</param>
        /// <returns>The latency, or null.</returns>
        internal static TimeSpan? QueueLatency(Job job, int leaseSeconds)
        {
            ArgumentNullException.ThrowIfNull(job);
            if (job.StartedAt is not DateTimeOffset started || job.LeaseExpiresAt is not DateTimeOffset expires || started != expires.AddSeconds(-leaseSeconds))
            {
                return null;
            }

            // A job claimed before it was due (a clock a little behind) waited for nothing.
            TimeSpan latency = started - job.DueAt;
            return latency > TimeSpan.Zero ? latency : TimeSpan.Zero;
        }

        /// <summary>The handler tag.</summary>
        /// <param name="handlerKey">The handler key.</param>
        /// <returns>The tag.</returns>
        private static KeyValuePair<string, object?> HandlerTag(string handlerKey)
        {
            return new KeyValuePair<string, object?>(JobsTelemetryNames.HandlerTag, handlerKey);
        }
    }
}
