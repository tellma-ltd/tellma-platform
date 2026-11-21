// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Outbox
{
    /// <summary>
    ///     The email outbox's instruments on the core meter: the emails enqueued, by source; the
    ///     emails the sending job settled, by state; and how long each sent or sandboxed email waited
    ///     from its enqueue to its send. No instrument carries a tenant, an address or a subject.
    /// </summary>
    internal sealed class OutboxMetrics
    {
        /// <summary>Emails enqueued.</summary>
        private readonly Counter<long> _enqueued;

        /// <summary>Emails settled by the sending job.</summary>
        private readonly Counter<long> _settled;

        /// <summary>Seconds from an email's enqueue to its send.</summary>
        private readonly Histogram<double> _latency;

        /// <summary>Creates the instruments.</summary>
        /// <param name="meterFactory">The host's meter factory, which owns the meter's lifetime.</param>
        public OutboxMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

            Meter meter = meterFactory.Create(OutboxTelemetryNames.MeterName);
            _enqueued = meter.CreateCounter<long>(
                OutboxTelemetryNames.Enqueued,
                "{email}",
                "Outgoing emails enqueued, one per row, by the source that produced them.");
            _settled = meter.CreateCounter<long>(
                OutboxTelemetryNames.Settled,
                "{email}",
                "Outgoing emails the sending job settled, by the state they settled in.");
            _latency = meter.CreateHistogram<double>(
                OutboxTelemetryNames.Latency,
                "s",
                "How long a sent or sandboxed email waited from its enqueue to its send, on the database clock.");
        }

        /// <summary>The state tag's value of a state: its name in lower case.</summary>
        /// <param name="state">The state.</param>
        /// <returns>The value.</returns>
        public static string StateValue(OutgoingEmailState state)
        {
            return state switch
            {
                OutgoingEmailState.Pending => "pending",
                OutgoingEmailState.Sending => "sending",
                OutgoingEmailState.Sent => "sent",
                OutgoingEmailState.Sandboxed => "sandboxed",
                OutgoingEmailState.Rejected => "rejected",
                OutgoingEmailState.Failed => "failed",
                OutgoingEmailState.Cancelled => "cancelled",
                _ => throw new ArgumentOutOfRangeException(nameof(state), state, "The state is not one the outbox defines."),
            };
        }

        /// <summary>Records the emails one enlisted save enqueued, one per row, each tagged with its request's source.</summary>
        /// <param name="requests">The requests whose rows the save took, each of a source the outbox admits.</param>
        public void RecordEnqueued(IReadOnlyList<EmailEnqueueRequest> requests)
        {
            ArgumentNullException.ThrowIfNull(requests);
            int notifications = requests.Count(static request => string.Equals(request.Source, EmailEnqueueRequest.NotificationSource, StringComparison.Ordinal));
            Add(_enqueued, requests.Count - notifications, new KeyValuePair<string, object?>(OutboxTelemetryNames.SourceTag, EmailEnqueueRequest.ApiSource));
            Add(_enqueued, notifications, new KeyValuePair<string, object?>(OutboxTelemetryNames.SourceTag, EmailEnqueueRequest.NotificationSource));
        }

        /// <summary>Records one email the sending job settled, and its wait when it was sent.</summary>
        /// <param name="state">The state it settled in.</param>
        /// <param name="latency">How long it waited from its enqueue to its send; null for an email that was not sent.</param>
        public void RecordSettled(OutgoingEmailState state, TimeSpan? latency)
        {
            _settled.Add(1, new KeyValuePair<string, object?>(OutboxTelemetryNames.StateTag, StateValue(state)));
            if (latency is TimeSpan wait)
            {
                _latency.Record(Math.Max(0, wait.TotalSeconds));
            }
        }

        /// <summary>Adds a positive amount to a counter; nothing for none, so an idle tag leaves no zero points.</summary>
        /// <param name="counter">The counter.</param>
        /// <param name="amount">The amount.</param>
        /// <param name="tag">The counter's one tag.</param>
        private static void Add(Counter<long> counter, int amount, KeyValuePair<string, object?> tag)
        {
            if (amount > 0)
            {
                counter.Add(amount, tag);
            }
        }
    }
}
