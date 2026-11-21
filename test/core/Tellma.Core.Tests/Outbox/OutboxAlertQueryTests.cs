// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Email;
using Tellma.Core.Outbox;
using Tellma.Testing.Support.Observability;

namespace Tellma.Core.Tests.Outbox
{
    /// <summary>
    ///     The alerts the outbox's telemetry was designed to back are checked in as queries, and this
    ///     checks the queries back against the instruments, tags, tag values and log events. Renaming
    ///     any of them turns a stale alert into a failing build rather than into an alert that quietly
    ///     stops firing.
    /// </summary>
    public sealed class OutboxAlertQueryTests
    {
        /// <summary>The checked-in queries, copied beside the suite.</summary>
        private static IReadOnlyList<AlertQuery> Queries => AlertQueries.Load(Path.Combine(AppContext.BaseDirectory, "OutboxAlertQueries"));

        /// <summary>The instruments the outbox declares: every name constant but the meter's and the tags'.</summary>
        private static HashSet<string> Instruments => AlertQueries.Constants(
            [typeof(OutboxTelemetryNames)],
            static name => !name.EndsWith("Tag", StringComparison.Ordinal) && !name.EndsWith("Name", StringComparison.Ordinal));

        [Fact]
        public void Every_alert_query_references_only_instruments_tags_events_and_values_the_outbox_emits()
        {
            IReadOnlyList<AlertQuery> queries = Queries;
            Assert.NotEmpty(queries);

            // The tags: the instruments' own and the event name every log record carries.
            HashSet<string> tags = AlertQueries.Constants([typeof(OutboxTelemetryNames)], static name => name.EndsWith("Tag", StringComparison.Ordinal));
            _ = tags.Add("EventName");

            // The values: every state the settled counter tags, every source, and every event the log emits.
            HashSet<string> values = AlertQueries.LogEventNames(typeof(OutboxLog));
            foreach (OutgoingEmailState state in Enum.GetValues<OutgoingEmailState>())
            {
                _ = values.Add(OutboxMetrics.StateValue(state));
            }

            values.UnionWith([EmailEnqueueRequest.ApiSource, EmailEnqueueRequest.NotificationSource]);

            Assert.Empty(AlertQueries.Unresolved(queries, Instruments, tags, values));
        }

        [Fact]
        public void Every_instrument_but_the_one_that_sizes_the_workload_is_covered_by_at_least_one_query()
        {
            Assert.Equal([OutboxTelemetryNames.Enqueued], AlertQueries.Uncovered(Queries, Instruments));
        }

        [Fact]
        public void The_interrupted_sends_query_reads_the_event_the_sending_job_logs()
        {
            AlertQuery query = Assert.Single(Queries, static query => query.FileName == "interrupted-sends.kql");

            Assert.Contains("OutboxSendInterrupted", AlertQueries.Values(query));
        }
    }
}
