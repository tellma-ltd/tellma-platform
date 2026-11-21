// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Jobs;
using Tellma.Testing.Support.Observability;

namespace Tellma.Core.Tests.Jobs
{
    /// <summary>
    ///     The alerts the job machinery's telemetry was designed to back are checked in as queries, and
    ///     this checks the queries back against the instruments, tags and log events. Renaming any of
    ///     them turns a stale alert into a failing build rather than into an alert that quietly stops
    ///     firing.
    /// </summary>
    public sealed class JobAlertQueryTests
    {
        /// <summary>The checked-in queries, copied beside the suite.</summary>
        private static IReadOnlyList<AlertQuery> Queries => AlertQueries.Load(Path.Combine(AppContext.BaseDirectory, "JobAlertQueries"));

        /// <summary>The instruments the machinery declares: every name constant but the meter's, the source's and the tags'.</summary>
        private static HashSet<string> Instruments => AlertQueries.Constants(
            [typeof(JobsTelemetryNames)],
            static name => !name.EndsWith("Tag", StringComparison.Ordinal) && !name.EndsWith("Name", StringComparison.Ordinal));

        [Fact]
        public void Every_alert_query_references_only_instruments_tags_events_and_values_the_machinery_emits()
        {
            IReadOnlyList<AlertQuery> queries = Queries;
            Assert.NotEmpty(queries);

            // The tags: the instruments' own, the event name every log record carries, and the keys
            // of the job log scope, the tenant id among them.
            HashSet<string> tags = AlertQueries.Constants([typeof(JobsTelemetryNames)], static name => name.EndsWith("Tag", StringComparison.Ordinal));
            _ = tags.Add("EventName");
            tags.UnionWith(new JobLogScope(tenantId: 1, userId: null, [], originTraceParent: null).Select(static pair => pair.Key));

            // The values: every outcome the completion counter tags, and every event the log emits.
            HashSet<string> values = AlertQueries.LogEventNames(typeof(JobsLog));
            foreach (JobOutcomeKind kind in Enum.GetValues<JobOutcomeKind>())
            {
                _ = values.Add(JobsMetrics.OutcomeValue(kind));
            }

            Assert.Empty(AlertQueries.Unresolved(queries, Instruments, tags, values));
        }

        [Fact]
        public void Every_instrument_but_those_that_size_the_workload_is_covered_by_at_least_one_query()
        {
            Assert.Equal(
                [
                    JobsTelemetryNames.BatchFill,
                    JobsTelemetryNames.Claimed,
                    JobsTelemetryNames.Duration,
                    JobsTelemetryNames.InFlight,
                    JobsTelemetryNames.LeaseLost,
                    JobsTelemetryNames.Orphaned,
                    JobsTelemetryNames.PollDuration,
                    JobsTelemetryNames.QueueLatency,
                    JobsTelemetryNames.SchedulesFired,
                    JobsTelemetryNames.SchedulesMissed,
                    JobsTelemetryNames.SchedulesOverlapSkipped,
                ],
                AlertQueries.Uncovered(Queries, Instruments));
        }

        [Fact]
        public void The_heartbeat_query_reads_the_events_the_worker_logs_for_a_heartbeat_and_a_retired_poller()
        {
            // The staleness alert is log-based: it must see both the heartbeat and the retirement
            // that ends a tenant's heartbeats by design, or every suspended tenant would alert.
            AlertQuery query = Assert.Single(Queries, static query => query.FileName == "stale-heartbeat.kql");
            HashSet<string> values = [.. AlertQueries.Values(query)];

            Assert.Contains("JobWorkerHeartbeat", values);
            Assert.Contains("JobPollerRetired", values);
        }
    }
}
