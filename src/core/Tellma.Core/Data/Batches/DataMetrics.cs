// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Batches
{
    // The instruments are spec 0012 §12.1's executor, allocator and emitter rows; the tree
    // instruments belong to the tree statements.
    /// <summary>
    ///     The data layer's instruments on the runtime meter: round trips per unit of work, each round
    ///     trip's duration, statements per batch, re-runs, commit probes, the allocator's ids and the
    ///     emitter's saved rows and conflicts. No instrument carries a tenant or a user.
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

        /// <summary>The conflict outcome of a row whose stamp did not match.</summary>
        public const string ConflictOutcome = "conflict";

        /// <summary>The conflict outcome of a row that no longer exists.</summary>
        public const string MissingOutcome = "missing";

        /// <summary>The seconds unit.</summary>
        private const string Seconds = "s";

        /// <summary>The unit of the id counters.</summary>
        private const string IdUnit = "{id}";

        /// <summary>The unit of the allocator's event counters.</summary>
        private const string EventUnit = "{event}";

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

        /// <summary>Ids handed to reservations and takes.</summary>
        private readonly Counter<long> _idsReserved;

        /// <summary>Ids returned to the buffer.</summary>
        private readonly Counter<long> _idsReturned;

        /// <summary>Ranges read from a sequence.</summary>
        private readonly Counter<long> _idRefills;

        /// <summary>Sequences found behind their table and healed.</summary>
        private readonly Counter<long> _idsHealed;

        /// <summary>Rows saves wrote.</summary>
        private readonly Counter<long> _rowsSaved;

        /// <summary>Rows a concurrency guard found in conflict.</summary>
        private readonly Counter<long> _conflicts;

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

            _idsReserved = meter.CreateCounter<long>(
                DataTelemetryNames.IdReserved,
                IdUnit,
                "Ids the allocator handed to reservations and takes, by sequence.");

            _idsReturned = meter.CreateCounter<long>(
                DataTelemetryNames.IdReturned,
                IdUnit,
                "Ids reservations gave back to the buffer before any persist, by sequence.");

            _idRefills = meter.CreateCounter<long>(
                DataTelemetryNames.IdRefills,
                EventUnit,
                "Ranges the allocator read from a sequence, by sequence.");

            _idsHealed = meter.CreateCounter<long>(
                DataTelemetryNames.IdHealed,
                EventUnit,
                "Sequences found behind their table at reservation and healed by consuming the gap, by sequence.");

            _rowsSaved = meter.CreateCounter<long>(
                DataTelemetryNames.RowsSaved,
                "{row}",
                "Rows saves inserted or updated, children included, by root entity.");

            _conflicts = meter.CreateCounter<long>(
                DataTelemetryNames.ConcurrencyConflicts,
                "{conflict}",
                "Rows a save's concurrency guard refused, by root entity and outcome.");
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

        /// <summary>Records ids handed to a reservation or a take.</summary>
        /// <param name="sequence">The sequence, schema-qualified.</param>
        /// <param name="count">The ids.</param>
        public void RecordIdsReserved(string sequence, long count)
        {
            _idsReserved.Add(count, new KeyValuePair<string, object?>(DataTelemetryNames.SequenceTag, sequence));
        }

        /// <summary>Records ids given back to the buffer.</summary>
        /// <param name="sequence">The sequence, schema-qualified.</param>
        /// <param name="count">The ids.</param>
        public void RecordIdsReturned(string sequence, long count)
        {
            _idsReturned.Add(count, new KeyValuePair<string, object?>(DataTelemetryNames.SequenceTag, sequence));
        }

        /// <summary>Records one range read from a sequence.</summary>
        /// <param name="sequence">The sequence, schema-qualified.</param>
        public void RecordIdRefill(string sequence)
        {
            _idRefills.Add(1, new KeyValuePair<string, object?>(DataTelemetryNames.SequenceTag, sequence));
        }

        /// <summary>Records one heal of a sequence found behind its table.</summary>
        /// <param name="sequence">The sequence, schema-qualified.</param>
        public void RecordIdHealed(string sequence)
        {
            _idsHealed.Add(1, new KeyValuePair<string, object?>(DataTelemetryNames.SequenceTag, sequence));
        }

        /// <summary>Records the rows one save wrote.</summary>
        /// <param name="entity">The root entity's name.</param>
        /// <param name="rows">The rows inserted and updated.</param>
        public void RecordRowsSaved(string entity, long rows)
        {
            _rowsSaved.Add(rows, new KeyValuePair<string, object?>(DataTelemetryNames.EntityTag, entity));
        }

        /// <summary>Records the rows one concurrency guard refused.</summary>
        /// <param name="entity">The root entity's name.</param>
        /// <param name="conflicts">The rows whose stamp did not match.</param>
        /// <param name="missing">The rows that no longer exist.</param>
        public void RecordConflicts(string entity, long conflicts, long missing)
        {
            if (conflicts > 0)
            {
                _conflicts.Add(
                    conflicts,
                    new KeyValuePair<string, object?>(DataTelemetryNames.EntityTag, entity),
                    new KeyValuePair<string, object?>(DataTelemetryNames.OutcomeTag, ConflictOutcome));
            }

            if (missing > 0)
            {
                _conflicts.Add(
                    missing,
                    new KeyValuePair<string, object?>(DataTelemetryNames.EntityTag, entity),
                    new KeyValuePair<string, object?>(DataTelemetryNames.OutcomeTag, MissingOutcome));
            }
        }
    }
}
