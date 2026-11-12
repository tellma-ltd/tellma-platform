// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Batches
{
    // The instruments are spec 0012 §12.1's executor rows; the id, row, conflict and tree
    // instruments belong to the allocator, the emitter and the tree statements.
    /// <summary>
    ///     The executor's instruments on the runtime meter: round trips per unit of work, each round
    ///     trip's duration, statements per batch, re-runs and commit probes. No instrument carries a
    ///     tenant or a user.
    /// </summary>
    internal sealed class DataMetrics
    {
        /// <summary>The outcome of a round trip that succeeded.</summary>
        public const string Ok = "ok";

        /// <summary>The outcome of a round trip that failed and was re-run.</summary>
        public const string Retried = "retried";

        /// <summary>The outcome of a round trip whose failure surfaced.</summary>
        public const string Failed = "failed";

        /// <summary>The re-run class of a failure the server reported as transient.</summary>
        public const string ReportedClass = "reported";

        /// <summary>The re-run class of a lost connection.</summary>
        public const string AmbiguousClass = "ambiguous";

        /// <summary>The error tag of a lost connection that raised no server error.</summary>
        public const string ConnectionLost = "connection-lost";

        /// <summary>The probe outcome of a batch that committed.</summary>
        public const string ProbeCommitted = "committed";

        /// <summary>The probe outcome of a batch that rolled back.</summary>
        public const string ProbeNotCommitted = "not-committed";

        /// <summary>The probe outcome when nothing could tell.</summary>
        public const string ProbeUnknown = "unknown";

        /// <summary>The seconds unit.</summary>
        private const string Seconds = "s";

        /// <summary>Round trips per unit of work.</summary>
        private readonly Histogram<int> _roundTrips;

        /// <summary>Each round trip's duration.</summary>
        private readonly Histogram<double> _duration;

        /// <summary>Statements per batch.</summary>
        private readonly Histogram<int> _statements;

        /// <summary>Re-runs.</summary>
        private readonly Counter<long> _retries;

        /// <summary>Commit probes.</summary>
        private readonly Counter<long> _probes;

        /// <summary>Creates the instruments.</summary>
        /// <param name="meterFactory">The host's meter factory, which owns the meter's lifetime.</param>
        public DataMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);
            Meter meter = meterFactory.Create(DataTelemetryNames.MeterName);

            _roundTrips = meter.CreateHistogram<int>(
                DataTelemetryNames.RoundTrips,
                "{roundtrip}",
                "Database round trips one unit of work took, retries included, by operation.");

            _duration = meter.CreateHistogram<double>(
                DataTelemetryNames.RoundTripDuration,
                Seconds,
                "Duration of one database round trip, by operation, purpose and outcome.");

            _statements = meter.CreateHistogram<int>(
                DataTelemetryNames.Statements,
                "{statement}",
                "Statements one batch carries, by purpose.");

            _retries = meter.CreateCounter<long>(
                DataTelemetryNames.Retries,
                "{retry}",
                "Round trips re-run after a failure, by class and error.");

            _probes = meter.CreateCounter<long>(
                DataTelemetryNames.CommitProbes,
                "{probe}",
                "Verdicts on whether an ambiguous persist round trip committed, by outcome.");
        }

        /// <summary>The probe outcome tag of a verdict.</summary>
        /// <param name="verdict">The verdict.</param>
        /// <returns>The tag value.</returns>
        public static string ProbeOutcome(CommitVerdict verdict)
        {
            return verdict switch
            {
                CommitVerdict.Committed => ProbeCommitted,
                CommitVerdict.NotCommitted => ProbeNotCommitted,
                CommitVerdict.Unknown => ProbeUnknown,
                _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Not a commit verdict."),
            };
        }

        /// <summary>Records the round trips of a unit of work that ended.</summary>
        /// <param name="operation">The unit of work's operation.</param>
        /// <param name="roundTrips">Its round trips.</param>
        public void RecordRoundTrips(string operation, int roundTrips)
        {
            _roundTrips.Record(roundTrips, new KeyValuePair<string, object?>(DataTelemetryNames.OperationTag, operation));
        }

        /// <summary>Records one round trip's duration.</summary>
        /// <param name="operation">The unit of work's operation.</param>
        /// <param name="purpose">The batch's purpose, in lower case.</param>
        /// <param name="outcome"><see cref="Ok" />, <see cref="Retried" /> or <see cref="Failed" />.</param>
        /// <param name="duration">How long it took.</param>
        public void RecordRoundTrip(string operation, string purpose, string outcome, TimeSpan duration)
        {
            _duration.Record(
                duration.TotalSeconds,
                new KeyValuePair<string, object?>(DataTelemetryNames.OperationTag, operation),
                new KeyValuePair<string, object?>(DataTelemetryNames.PurposeTag, purpose),
                new KeyValuePair<string, object?>(DataTelemetryNames.OutcomeTag, outcome));
        }

        /// <summary>Records the statements one batch carries.</summary>
        /// <param name="purpose">The batch's purpose, in lower case.</param>
        /// <param name="statements">Its statements that carry text.</param>
        public void RecordStatements(string purpose, int statements)
        {
            _statements.Record(statements, new KeyValuePair<string, object?>(DataTelemetryNames.PurposeTag, purpose));
        }

        /// <summary>Records one re-run.</summary>
        /// <param name="retryClass"><see cref="ReportedClass" /> or <see cref="AmbiguousClass" />.</param>
        /// <param name="error">The failure's server error number, or <see cref="ConnectionLost" />.</param>
        public void RecordRetry(string retryClass, string error)
        {
            _retries.Add(
                1,
                new KeyValuePair<string, object?>(DataTelemetryNames.ClassTag, retryClass),
                new KeyValuePair<string, object?>(DataTelemetryNames.ErrorTag, error));
        }

        /// <summary>Records one commit verdict.</summary>
        /// <param name="verdict">The verdict.</param>
        public void RecordProbe(CommitVerdict verdict)
        {
            _probes.Add(1, new KeyValuePair<string, object?>(DataTelemetryNames.OutcomeTag, ProbeOutcome(verdict)));
        }
    }
}
