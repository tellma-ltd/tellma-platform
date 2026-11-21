// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Outbox
{
    /// <summary>
    ///     The email outbox's instruments on the core meter: the emails enqueued, by source; the
    ///     emails the sending job settled, by state; how long each sent or sandboxed email waited
    ///     from its enqueue to its send; the delivery events weighed, by what became of them; and the
    ///     emails the retention deleted. No instrument carries a tenant, an address or a subject.
    /// </summary>
    internal sealed class OutboxMetrics
    {
        /// <summary>The result of a delivery event applied to its row.</summary>
        public const string AppliedResult = "applied";

        /// <summary>The result of a delivery event that repeats one applied, or is no stronger than what its row records.</summary>
        public const string StaleResult = "stale";

        /// <summary>The result of a delivery event whose reference no row has.</summary>
        public const string UnmatchedResult = "unmatched";

        /// <summary>The result of a delivery event dropped with its missing, provisioning or retired tenant.</summary>
        public const string TenantSkippedResult = "tenant_skipped";

        /// <summary>Every result a delivery event is counted by.</summary>
        public static IReadOnlyList<string> Results { get; } = [AppliedResult, StaleResult, UnmatchedResult, TenantSkippedResult];

        /// <summary>Emails enqueued.</summary>
        private readonly Counter<long> _enqueued;

        /// <summary>Emails settled by the sending job.</summary>
        private readonly Counter<long> _settled;

        /// <summary>Seconds from an email's enqueue to its send.</summary>
        private readonly Histogram<double> _latency;

        /// <summary>Delivery events weighed, by result.</summary>
        private readonly Counter<long> _deliveryUpdates;

        /// <summary>Emails the retention deleted.</summary>
        private readonly Counter<long> _retained;

        /// <summary>
        ///     The source of each row the outbox handed to an enlisted save, until its frame commits;
        ///     a row whose frame rolls back is dropped with the row itself.
        /// </summary>
        private readonly ConditionalWeakTable<OutgoingEmail, string> _sources = [];

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
            _deliveryUpdates = meter.CreateCounter<long>(
                OutboxTelemetryNames.DeliveryUpdates,
                "{event}",
                "Delivery events the outbox weighed, by whether each was applied, stale, unmatched or dropped with its tenant.");
            _retained = meter.CreateCounter<long>(
                OutboxTelemetryNames.Retained,
                "{email}",
                "Outgoing emails the nightly retention deleted.");
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

        /// <summary>Notes the source of each row the outbox is about to enlist, so its commit can be counted by it.</summary>
        /// <param name="rows">The rows, in request order.</param>
        /// <param name="requests">The requests, each of a source the outbox admits.</param>
        /// <exception cref="ArgumentException">The two lists differ in length.</exception>
        public void Took(IReadOnlyList<OutgoingEmail> rows, IReadOnlyList<EmailEnqueueRequest> requests)
        {
            ArgumentNullException.ThrowIfNull(rows);
            ArgumentNullException.ThrowIfNull(requests);
            if (rows.Count != requests.Count)
            {
                throw new ArgumentException("Every row needs the request it came from.", nameof(rows));
            }

            for (int index = 0; index < rows.Count; index++)
            {
                _sources.AddOrUpdate(rows[index], requests[index].Source);
            }
        }

        /// <summary>
        ///     Records the emails a committed save wrote, one per row the outbox took, each tagged with
        ///     its request's source; a row is counted once, and a row the outbox never took not at all.
        /// </summary>
        /// <param name="committed">The rows the committed save wrote.</param>
        public void RecordEnqueued(IEnumerable<OutgoingEmail> committed)
        {
            ArgumentNullException.ThrowIfNull(committed);
            int api = 0;
            int notifications = 0;
            foreach (OutgoingEmail row in committed)
            {
                // Removing the row's source counts it once, however often a commit reports it.
                if (!_sources.TryGetValue(row, out string? source) || !_sources.Remove(row))
                {
                    continue;
                }

                if (string.Equals(source, EmailEnqueueRequest.NotificationSource, StringComparison.Ordinal))
                {
                    notifications++;
                }
                else
                {
                    api++;
                }
            }

            Add(_enqueued, api, new KeyValuePair<string, object?>(OutboxTelemetryNames.SourceTag, EmailEnqueueRequest.ApiSource));
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

        /// <summary>Records delivery events weighed with one result.</summary>
        /// <param name="result">The result, one of <see cref="Results" />.</param>
        /// <param name="count">The events; nothing is recorded for none.</param>
        /// <exception cref="ArgumentException"><paramref name="result" /> is not one of <see cref="Results" />.</exception>
        public void RecordDeliveryUpdates(string result, int count)
        {
            if (!Results.Contains(result, StringComparer.Ordinal))
            {
                throw new ArgumentException($"'{result}' is not a delivery event result.", nameof(result));
            }

            Add(_deliveryUpdates, count, new KeyValuePair<string, object?>(OutboxTelemetryNames.ResultTag, result));
        }

        /// <summary>Records emails the retention deleted.</summary>
        /// <param name="count">The emails; nothing is recorded for none.</param>
        public void RecordRetained(int count)
        {
            if (count > 0)
            {
                _retained.Add(count);
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
