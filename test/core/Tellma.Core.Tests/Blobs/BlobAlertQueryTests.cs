// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Blobs;
using Tellma.Testing.Support.Observability;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>
    ///     The alerts the blob telemetry was designed to back are checked in as queries, and this
    ///     checks the queries back against the instruments, tags, tag values and log events. Renaming
    ///     any of them turns a stale alert into a failing build rather than into an alert that quietly
    ///     stops firing.
    /// </summary>
    public sealed class BlobAlertQueryTests
    {
        /// <summary>The checked-in queries, copied beside the suite.</summary>
        private static IReadOnlyList<AlertQuery> Queries => AlertQueries.Load(Path.Combine(AppContext.BaseDirectory, "BlobAlertQueries"));

        /// <summary>The instruments the blob machinery declares: every name constant but the meters', the tags' and the units'.</summary>
        private static HashSet<string> Instruments => AlertQueries.Constants(
            [typeof(BlobTelemetryNames)],
            static name => !name.EndsWith("Tag", StringComparison.Ordinal) && !name.EndsWith("Name", StringComparison.Ordinal) && !name.EndsWith("Unit", StringComparison.Ordinal));

        [Fact]
        public void Every_alert_query_references_only_instruments_tags_events_and_values_the_blob_machinery_emits()
        {
            IReadOnlyList<AlertQuery> queries = Queries;
            Assert.Equal(3, queries.Count);

            // The tags: the instruments' own and the event name every log record carries.
            HashSet<string> tags = AlertQueries.Constants([typeof(BlobTelemetryNames)], static name => name.EndsWith("Tag", StringComparison.Ordinal));
            _ = tags.Add("EventName");

            // The values: every tag value, and every event the sweep and reconcile log.
            HashSet<string> values = AlertQueries.Constants([typeof(BlobTelemetryTagValues)], static _ => true);
            values.UnionWith(AlertQueries.LogEventNames(typeof(BlobMaintenanceLog)));

            Assert.Empty(AlertQueries.Unresolved(queries, Instruments, tags, values));
        }

        [Fact]
        public void Exactly_the_sweep_failures_the_reconcile_releases_and_the_attach_refusals_are_alerted_on()
        {
            HashSet<string> instruments = Instruments;
            IReadOnlyList<string> uncovered = AlertQueries.Uncovered(Queries, instruments);

            Assert.Equal(
                [BlobTelemetryNames.AttachRejected, BlobTelemetryNames.ReconcileReleased, BlobTelemetryNames.SweepFailures],
                instruments.Except(uncovered).Order(StringComparer.Ordinal));
        }

        [Fact]
        public void The_reconcile_and_attach_queries_read_the_kind_and_the_attach_query_the_reason()
        {
            AlertQuery reconcile = Assert.Single(Queries, static query => query.FileName == "reconcile-released.kql");
            AlertQuery attach = Assert.Single(Queries, static query => query.FileName == "attach-rejected-rate.kql");

            Assert.Equal([BlobTelemetryNames.KindTag], AlertQueries.Dimensions(reconcile));
            Assert.Equal([BlobTelemetryNames.KindTag, BlobTelemetryNames.ReasonTag], AlertQueries.Dimensions(attach).Order(StringComparer.Ordinal));
        }
    }
}
