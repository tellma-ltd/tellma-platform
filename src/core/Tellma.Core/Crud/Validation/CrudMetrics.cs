// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Crud.Validation
{
    // The instruments are spec 0015 §16.1's validation-rounds and confirmations rows; the pipeline's
    // other instruments join this class as the pipeline lands.
    /// <summary>
    ///     The pipeline's instruments on the <c>Tellma.Core</c> meter: the validation rounds each
    ///     operation dispatched and the confirmation warnings raised.
    /// </summary>
    internal sealed class CrudMetrics
    {
        /// <summary>The <c>crud.outcome</c> of rounds that stayed within the ceiling.</summary>
        public const string RoundsOk = "ok";

        /// <summary>The <c>crud.outcome</c> of rounds that a validator drove past the ceiling.</summary>
        public const string RoundsExceeded = "exceeded";

        /// <summary>The <c>crud.outcome</c> of a warning the caller has not confirmed.</summary>
        public const string ConfirmationRequired = "required";

        /// <summary>The <c>crud.outcome</c> of a warning the caller confirmed, or that the system scope confirms.</summary>
        public const string ConfirmationConfirmed = "confirmed";

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

            _rounds = meter.CreateHistogram<long>(
                CrudTelemetryNames.ValidationRounds,
                "{round}",
                "Validation rounds one save, delete or action dispatched, by operation and by whether a validator drove them past the ceiling.");

            _confirmations = meter.CreateCounter<long>(
                CrudTelemetryNames.Confirmations,
                "{warning}",
                "Confirmation warnings raised, by key and by whether the caller had confirmed them.");
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
