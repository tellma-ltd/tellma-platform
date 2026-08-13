// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Email
{
    /// <summary>
    ///     Routes translated delivery events to the handler owning each event's underlying state, and
    ///     meters the ones that route nowhere.
    /// </summary>
    /// <remarks>
    ///     Handler failures propagate deliberately. No event queue exists at this tier, so the
    ///     provider's own redelivery is the only durable retry: the receiver maps the exception to a
    ///     transient failure, the provider redelivers the batch, and handler-side deduplication on
    ///     the provider event id makes that harmless. Swallowing the failure would lose the event
    ///     instead.
    /// </remarks>
    /// <param name="handlers">Every delivery-event handler in the composition.</param>
    /// <param name="metrics">The pipeline's instruments.</param>
    /// <param name="timeProvider">The clock the arrival-lag histogram is measured against.</param>
    /// <param name="logger">Where routing outcomes are logged.</param>
    internal sealed class EmailDeliveryEventDispatcher(
        IEnumerable<IEmailDeliveryEventHandler> handlers,
        EmailMetrics metrics,
        TimeProvider timeProvider,
        ILogger<EmailDeliveryEventDispatcher> logger) : IEmailDeliveryEventDispatcher
    {
        private readonly Dictionary<string, IEmailDeliveryEventHandler> _handlers =
            handlers.ToDictionary(static h => h.OwnerKey, StringComparer.Ordinal);

        /// <inheritdoc />
        public async Task DispatchAsync(
            string transport, IReadOnlyList<EmailDeliveryEvent> events, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(transport);
            ArgumentNullException.ThrowIfNull(events);

            if (events.Count == 0)
            {
                return;
            }

            using Activity? activity =
                EmailDiagnostics.ActivitySource.StartActivity(EmailDiagnostics.DispatchActivityName);
            activity?.SetTag(EmailTelemetryNames.TransportTag, transport);
            activity?.SetTag(EmailDiagnostics.EventCountTag, events.Count);

            DateTimeOffset now = timeProvider.GetUtcNow();

            // Arrival order is preserved inside each owner's list, and owners appear in the order
            // their first event did — so a handler sees its events exactly as the provider sent them.
            Dictionary<string, List<EmailDeliveryEvent>> routed = new(StringComparer.Ordinal);
            Dictionary<string, int>? unknownOwners = null;
            int uncorrelated = 0;

            foreach (EmailDeliveryEvent @event in events)
            {
                metrics.RecordDeliveryEventLag(transport, @event.Type, (now - @event.Timestamp).TotalSeconds);

                string? ownerKey = @event.Correlation?.OwnerKey;
                if (ownerKey is null)
                {
                    // Mail sent outside the platform through the same provider account, or provider
                    // events that carry no custom arguments. Expected background noise.
                    uncorrelated++;
                    metrics.RecordDeliveryEvent(
                        transport, @event.Type, EmailTelemetryNames.UncorrelatedEvent, ownerKey: null);
                    continue;
                }

                if (!_handlers.ContainsKey(ownerKey))
                {
                    // Foreign deployments are already filtered at translation, so this is a
                    // composition bug: an owner minted correlations but registered no handler.
                    unknownOwners ??= new Dictionary<string, int>(StringComparer.Ordinal);
                    unknownOwners[ownerKey] = unknownOwners.GetValueOrDefault(ownerKey) + 1;

                    // A literal, not the key itself: an unrecognized key came off the wire, and a
                    // forged or stale correlation would otherwise make this dimension unbounded. The
                    // key that was actually seen is in the log line below, where cardinality costs
                    // nothing.
                    metrics.RecordDeliveryEvent(
                        transport, @event.Type, EmailTelemetryNames.UnknownOwnerEvent,
                        EmailTelemetryNames.UnknownOwnerTagValue);
                    continue;
                }

                if (!routed.TryGetValue(ownerKey, out List<EmailDeliveryEvent>? forOwner))
                {
                    forOwner = [];
                    routed[ownerKey] = forOwner;
                }

                forOwner.Add(@event);
                metrics.RecordDeliveryEvent(transport, @event.Type, EmailTelemetryNames.RoutedEvent, ownerKey);
            }

            if (uncorrelated > 0)
            {
                EmailLog.UncorrelatedEvents(logger, transport, uncorrelated);
            }

            if (unknownOwners is not null)
            {
                foreach (KeyValuePair<string, int> unknown in unknownOwners)
                {
                    EmailLog.UnknownOwnerKey(logger, transport, unknown.Key, unknown.Value);
                }
            }

            activity?.SetTag(EmailDiagnostics.OwnerCountTag, routed.Count);

            // Batch in, batch out: one call per owner, never one per event.
            foreach (KeyValuePair<string, List<EmailDeliveryEvent>> group in routed)
            {
                await _handlers[group.Key].HandleAsync(group.Value, cancellationToken).ConfigureAwait(false);
            }

            if (routed.Count > 0 && logger.IsEnabled(LogLevel.Debug))
            {
                int dispatched = routed.Sum(static g => g.Value.Count);
                EmailLog.EventsDispatched(logger, transport, routed.Count, dispatched);
            }
        }
    }
}
