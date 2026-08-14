// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Testing.Email
{
    /// <summary>Builds plausible delivery-event batches for handler and dispatcher tests.</summary>
    public static class DeliveryEvents
    {
        /// <summary>Starts a batch for one correlation.</summary>
        /// <param name="correlation">The correlation every event in the batch carries, or null to
        ///     build the uncorrelated events a provider also delivers.</param>
        /// <returns>A builder.</returns>
        public static DeliveryEventBuilder For(EmailCorrelation? correlation)
        {
            return new DeliveryEventBuilder(correlation);
        }
    }

    /// <summary>Accumulates a delivery-event batch.</summary>
    /// <remarks>
    ///     Deterministic by default — event ids run <c>evt-0001</c> upward and timestamps advance by a
    ///     fixed step from a fixed epoch — because a fixture built on the wall clock makes every
    ///     assertion that touches ordering or lag flaky.
    /// </remarks>
    public sealed class DeliveryEventBuilder
    {
        private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly TimeSpan Step = TimeSpan.FromSeconds(30);

        private readonly EmailCorrelation? _correlation;
        private readonly List<EmailDeliveryEvent> _events = [];
        private string _recipient = "recipient@example.com";

        // Counts appended events rather than list entries, so a redelivery — which adds a duplicate
        // without being a new event — does not push the next real event's id and timestamp along.
        private int _appended;

        internal DeliveryEventBuilder(EmailCorrelation? correlation)
        {
            _correlation = correlation;
        }

        /// <summary>Sets the recipient every subsequent event concerns.</summary>
        /// <param name="recipient">The address.</param>
        /// <returns>This builder.</returns>
        public DeliveryEventBuilder ForRecipient(string recipient)
        {
            _recipient = recipient;
            return this;
        }

        /// <summary>Appends a delivered event.</summary>
        /// <returns>This builder.</returns>
        public DeliveryEventBuilder Delivered()
        {
            return Append(EmailDeliveryEventType.Delivered, "delivered", null);
        }

        /// <summary>Appends a deferred event.</summary>
        /// <returns>This builder.</returns>
        public DeliveryEventBuilder Deferred()
        {
            return Append(EmailDeliveryEventType.Deferred, "deferred", "temporarily deferred");
        }

        /// <summary>Appends a bounce.</summary>
        /// <param name="reason">The provider-reported reason.</param>
        /// <returns>This builder.</returns>
        public DeliveryEventBuilder Bounced(string reason = "550 unknown user")
        {
            return Append(EmailDeliveryEventType.Bounced, "bounce", reason);
        }

        /// <summary>Appends a pre-send discard.</summary>
        /// <param name="reason">The provider-reported reason.</param>
        /// <returns>This builder.</returns>
        public DeliveryEventBuilder Dropped(string reason = "suppressed")
        {
            return Append(EmailDeliveryEventType.Dropped, "dropped", reason);
        }

        /// <summary>Appends an open.</summary>
        /// <returns>This builder.</returns>
        public DeliveryEventBuilder Opened()
        {
            return Append(EmailDeliveryEventType.Opened, "open", null);
        }

        /// <summary>Appends a click.</summary>
        /// <returns>This builder.</returns>
        public DeliveryEventBuilder Clicked()
        {
            return Append(EmailDeliveryEventType.Clicked, "click", null);
        }

        /// <summary>Appends a spam report.</summary>
        /// <returns>This builder.</returns>
        public DeliveryEventBuilder SpamReported()
        {
            return Append(EmailDeliveryEventType.SpamReported, "spamreport", null);
        }

        /// <summary>
        ///     Appends the previous event again, verbatim — same provider event id and timestamp — as
        ///     a provider redelivery, so a handler's deduplication can be exercised.
        /// </summary>
        /// <returns>This builder.</returns>
        /// <exception cref="InvalidOperationException">There is no previous event to redeliver.</exception>
        public DeliveryEventBuilder Redeliver()
        {
            if (_events.Count == 0)
            {
                throw new InvalidOperationException("There is no event to redeliver yet.");
            }

            _events.Add(_events[^1]);
            return this;
        }

        /// <summary>Returns the batch.</summary>
        /// <returns>The events, in the order they were appended.</returns>
        public IReadOnlyList<EmailDeliveryEvent> Build()
        {
            return [.. _events];
        }

        private DeliveryEventBuilder Append(EmailDeliveryEventType type, string rawType, string? reason)
        {
            int index = ++_appended;
            _events.Add(new EmailDeliveryEvent(
                _correlation,
                _recipient,
                type,
                rawType,
                reason,
                Epoch + (Step * index),
                $"evt-{index.ToString("D4", CultureInfo.InvariantCulture)}"));

            return this;
        }
    }
}
