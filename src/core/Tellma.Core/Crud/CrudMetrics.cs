// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Crud
{
    // The instruments are spec 0015 §16.1's operations, duration, discovery, validation-rounds and
    // confirmations rows; the save, concurrency, effects and enlistment rows join this class as the
    // operations that raise them land.
    /// <summary>
    ///     The pipeline's instruments on the <c>Tellma.Core</c> meter: the operations and how long
    ///     each took, the argument discoveries a query's cache missed, the validation rounds each
    ///     operation dispatched and the confirmation warnings raised.
    /// </summary>
    internal sealed class CrudMetrics
    {
        /// <summary>The <c>crud.lane</c> of an operation whose payload rows are at most the large-batch threshold.</summary>
        public const string SmallLane = "small";

        /// <summary>The <c>crud.lane</c> of an operation whose payload rows exceed the large-batch threshold.</summary>
        public const string LargeLane = "large";

        /// <summary>The <c>crud.outcome</c> of rounds that stayed within the ceiling.</summary>
        public const string RoundsOk = "ok";

        /// <summary>The <c>crud.outcome</c> of rounds that a validator drove past the ceiling.</summary>
        public const string RoundsExceeded = "exceeded";

        /// <summary>The <c>crud.outcome</c> of a warning the caller has not confirmed.</summary>
        public const string ConfirmationRequired = "required";

        /// <summary>The <c>crud.outcome</c> of a warning the caller confirmed, or that the system scope confirms.</summary>
        public const string ConfirmationConfirmed = "confirmed";

        /// <summary>The operations, by operation, outcome and source.</summary>
        private readonly Counter<long> _operations;

        /// <summary>The operations' durations in seconds, by operation, outcome, source and lane.</summary>
        private readonly Histogram<double> _duration;

        /// <summary>The seconds each argument discovery took on a cache miss.</summary>
        private readonly Histogram<double> _discover;

        /// <summary>The validation rounds per operation.</summary>
        private readonly Histogram<long> _rounds;

        /// <summary>The confirmation warnings raised.</summary>
        private readonly Counter<long> _confirmations;

        /// <summary>Creates the instruments.</summary>
        /// <param name="meterFactory">The meter factory.</param>
        public CrudMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

            Meter meter = meterFactory.Create(CrudTelemetryNames.MeterName);

            _operations = meter.CreateCounter<long>(
                CrudTelemetryNames.Operations,
                "{operation}",
                "Stack operations, by operation, by outcome and, for a write, by source.");

            _duration = meter.CreateHistogram<double>(
                CrudTelemetryNames.OperationDuration,
                "s",
                "How long each stack operation took, by operation, outcome, source and lane.");

            _discover = meter.CreateHistogram<double>(
                CrudTelemetryNames.QueryDiscoverDuration,
                "s",
                "How long a query's argument discovery took when its cache missed.");

            _rounds = meter.CreateHistogram<long>(
                CrudTelemetryNames.ValidationRounds,
                "{round}",
                "Validation rounds one save, delete or action dispatched, by operation and by whether a validator drove them past the ceiling.");

            _confirmations = meter.CreateCounter<long>(
                CrudTelemetryNames.Confirmations,
                "{warning}",
                "Confirmation warnings raised, by key and by whether the caller had confirmed them.");
        }

        /// <summary>The <c>crud.lane</c> of an operation over a number of rows or ids.</summary>
        /// <param name="count">The rows or ids.</param>
        /// <param name="largeBatchThreshold">The data layer's large-batch threshold.</param>
        /// <returns><see cref="LargeLane" /> beyond the threshold; <see cref="SmallLane" /> otherwise.</returns>
        public static string Lane(int count, int largeBatchThreshold)
        {
            return count > largeBatchThreshold ? LargeLane : SmallLane;
        }

        /// <summary>Records one operation: counted, and its duration measured.</summary>
        /// <param name="operation">The <c>crud.operation</c> value, such as <c>query</c>.</param>
        /// <param name="outcome">The <c>crud.outcome</c> value, such as <c>ok</c>.</param>
        /// <param name="source">The <c>crud.source</c> value of a write; null for a read, which carries none.</param>
        /// <param name="lane">The <c>crud.lane</c> value, <see cref="SmallLane" /> or <see cref="LargeLane" />.</param>
        /// <param name="seconds">How long the operation took.</param>
        public void RecordOperation(string operation, string outcome, string? source, string lane, double seconds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);
            ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
            ArgumentException.ThrowIfNullOrWhiteSpace(lane);

            // --- The counter carries no lane; the histogram carries every tag ---
            KeyValuePair<string, object?> operationTag = new(CrudTelemetryNames.OperationTag, operation);
            KeyValuePair<string, object?> outcomeTag = new(CrudTelemetryNames.OutcomeTag, outcome);
            KeyValuePair<string, object?> laneTag = new(CrudTelemetryNames.LaneTag, lane);
            if (source is null)
            {
                _operations.Add(1, operationTag, outcomeTag);
                _duration.Record(seconds, operationTag, outcomeTag, laneTag);
                return;
            }

            KeyValuePair<string, object?> sourceTag = new(CrudTelemetryNames.SourceTag, source);
            _operations.Add(1, operationTag, outcomeTag, sourceTag);
            _duration.Record(seconds, operationTag, outcomeTag, sourceTag, laneTag);
        }

        /// <summary>Records how long an argument discovery took on a cache miss.</summary>
        /// <param name="seconds">The duration.</param>
        public void RecordDiscovery(double seconds)
        {
            _discover.Record(seconds);
        }

        /// <summary>Records the rounds one operation dispatched.</summary>
        /// <param name="operation">The <c>crud.operation</c> value, such as <c>save</c>.</param>
        /// <param name="rounds">The rounds dispatched.</param>
        /// <param name="exceeded">Whether a validator asked for a round beyond the ceiling.</param>
        public void RecordRounds(string operation, int rounds, bool exceeded)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);
            _rounds.Record(
                rounds,
                new KeyValuePair<string, object?>(CrudTelemetryNames.OperationTag, operation),
                new KeyValuePair<string, object?>(CrudTelemetryNames.OutcomeTag, exceeded ? RoundsExceeded : RoundsOk));
        }

        /// <summary>Counts one warning raised.</summary>
        /// <param name="key">The warning's declared key.</param>
        /// <param name="confirmed">Whether it counts as confirmed.</param>
        public void CountConfirmation(string key, bool confirmed)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            _confirmations.Add(
                1,
                new KeyValuePair<string, object?>(CrudTelemetryNames.KeyTag, key),
                new KeyValuePair<string, object?>(CrudTelemetryNames.OutcomeTag, confirmed ? ConfirmationConfirmed : ConfirmationRequired));
        }
    }
}
