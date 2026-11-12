// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Data.Batches
{
    /// <summary>
    ///     The executor's instruments: each one's name, unit and tags, and the scope recorder that
    ///     measures a unit of work's round trips when its scope ends.
    /// </summary>
    public sealed class DataMetricsTests : IDisposable
    {
        /// <summary>The meter factory the instruments come from.</summary>
        private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().BuildServiceProvider();

        private IMeterFactory Factory => _services.GetRequiredService<IMeterFactory>();

        [Fact]
        public void Each_instrument_records_its_measure_with_its_tags_and_no_tenant()
        {
            DataMetrics metrics = new(Factory);
            using MetricCollector<int> roundTrips = Collector<int>(DataTelemetryNames.RoundTrips);
            using MetricCollector<double> duration = Collector<double>(DataTelemetryNames.RoundTripDuration);
            using MetricCollector<int> statements = Collector<int>(DataTelemetryNames.Statements);
            using MetricCollector<long> retries = Collector<long>(DataTelemetryNames.Retries);
            using MetricCollector<long> probes = Collector<long>(DataTelemetryNames.CommitProbes);

            metrics.RecordRoundTrips("documents.save", 3);
            metrics.RecordRoundTrip("documents.save", "persist", DataMetrics.Retried, TimeSpan.FromMilliseconds(250));
            metrics.RecordStatements("persist", 12);
            metrics.RecordRetry(DataMetrics.ReportedClass, "1205");
            metrics.RecordProbe(CommitVerdict.NotCommitted);

            Assert.Equal((3, Tags((DataTelemetryNames.OperationTag, "documents.save"))), Single(roundTrips));
            Assert.Equal(
                (0.25, Tags((DataTelemetryNames.OperationTag, "documents.save"), (DataTelemetryNames.PurposeTag, "persist"), (DataTelemetryNames.OutcomeTag, "retried"))),
                Single(duration));
            Assert.Equal((12, Tags((DataTelemetryNames.PurposeTag, "persist"))), Single(statements));
            Assert.Equal((1L, Tags((DataTelemetryNames.ClassTag, "reported"), (DataTelemetryNames.ErrorTag, "1205"))), Single(retries));
            Assert.Equal((1L, Tags((DataTelemetryNames.OutcomeTag, "not-committed"))), Single(probes));

            // --- The units ---
            Assert.Equal(
                ["{roundtrip}", "s", "{statement}", "{retry}", "{probe}"],
                new Instrument?[] { roundTrips.Instrument, duration.Instrument, statements.Instrument, retries.Instrument, probes.Instrument }.Select(static instrument => instrument?.Unit));
        }

        [Fact]
        public void The_probe_outcomes_name_each_verdict()
        {
            Assert.Equal(
                ["committed", "not-committed", "unknown"],
                new[] { CommitVerdict.Committed, CommitVerdict.NotCommitted, CommitVerdict.Unknown }.Select(DataMetrics.ProbeOutcome));
        }

        [Fact]
        public void The_recorder_measures_the_scopes_round_trips_once_and_stamps_them_on_the_current_span()
        {
            DataMetrics metrics = new(Factory);
            using MetricCollector<int> roundTrips = Collector<int>(DataTelemetryNames.RoundTrips);
            using ActivitySource source = new("Tellma.Core.Tests.DataMetrics");
            using ActivityListener listener = new()
            {
                ShouldListenTo = candidate => ReferenceEquals(candidate, source),
                Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            };
            ActivitySource.AddActivityListener(listener);

            using Activity request = Assert.IsType<Activity>(source.StartActivity("request"));
            DataAccessRecorder recorder = new(metrics);
            recorder.Scope.Operation = "documents.save";
            recorder.Scope.RoundTrips = 4;
            recorder.Dispose();
            recorder.Dispose();

            Assert.Equal((4, Tags((DataTelemetryNames.OperationTag, "documents.save"))), Single(roundTrips));
            Assert.Equal(4, request.GetTagItem(DataTelemetryNames.RoundTrips));
        }

        [Fact]
        public async Task A_composed_scope_that_names_its_operation_records_its_round_trips_even_with_no_batch()
        {
            await using ServiceProvider root = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);
            using MetricCollector<int> roundTrips = new(
                root.GetRequiredService<IMeterFactory>(), DataTelemetryNames.MeterName, DataTelemetryNames.RoundTrips);

            // --- A unit of work served without the database: its opener names it, and nothing else runs ---
            await using (AsyncServiceScope scope = root.CreateAsyncScope())
            {
                DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
                access.Operation = "settings:query";
                Assert.Same(access, scope.ServiceProvider.GetRequiredService<DataAccessScope>());
            }

            Assert.Equal((0, Tags((DataTelemetryNames.OperationTag, "settings:query"))), Single(roundTrips));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _services.Dispose();
        }

        /// <summary>The one measurement a collector holds, as its value and its tags.</summary>
        /// <typeparam name="T">The measure's type.</typeparam>
        /// <param name="collector">The collector.</param>
        /// <returns>The value and the tags, by key.</returns>
        private static (T Value, string Tags) Single<T>(MetricCollector<T> collector)
            where T : struct
        {
            CollectedMeasurement<T> measurement = Assert.Single(collector.GetMeasurementSnapshot());
            return (measurement.Value, string.Join(";", measurement.Tags.OrderBy(static tag => tag.Key, StringComparer.Ordinal).Select(static tag => $"{tag.Key}={tag.Value}")));
        }

        /// <summary>Tags as <see cref="Single{T}" /> renders them.</summary>
        /// <param name="tags">The tags.</param>
        /// <returns>The rendering.</returns>
        private static string Tags(params (string Key, string Value)[] tags)
        {
            return string.Join(";", tags.OrderBy(static tag => tag.Key, StringComparer.Ordinal).Select(static tag => $"{tag.Key}={tag.Value}"));
        }

        /// <summary>A collector of one data instrument from this test's factory.</summary>
        /// <typeparam name="T">The measure's type.</typeparam>
        /// <param name="instrument">The instrument's name.</param>
        /// <returns>The collector.</returns>
        private MetricCollector<T> Collector<T>(string instrument)
            where T : struct
        {
            return new MetricCollector<T>(Factory, DataTelemetryNames.MeterName, instrument);
        }
    }
}
