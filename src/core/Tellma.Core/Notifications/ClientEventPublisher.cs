// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Realtime;

namespace Tellma.Core.Notifications
{
    /// <summary>
    ///     What every <see cref="IClientEventPublisher" /> shares: the refusals, the coalescing of the
    ///     events published on one batch into its one commit callback, and the logging of a failed
    ///     delivery. A derived class supplies the delivery alone.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Both members refuse, synchronously and before anything is registered or sent, an
    ///         unregistered name and a payload whose serialisation under the platform's JSON options
    ///         exceeds <see cref="ClientEvent.MaxPayloadBytes" /> UTF-8 bytes; a derived class adds
    ///         its own refusals through <see cref="ValidateScope" />.
    ///     </para>
    ///     <para>
    ///         The events published on one batch are sent from one commit callback, each distinct
    ///         name, set of users and payload once, in the order first published. A failed delivery
    ///         is logged as <c>ClientEventFailed</c> and never thrown or retried.
    ///     </para>
    /// </remarks>
    public abstract class ClientEventPublisher : IClientEventPublisher
    {
        // Per batch: the events published on it, which its one commit callback sends. Weak, so a
        // batch that never executes takes its events with it.
        private readonly ConditionalWeakTable<IDataBatch, PendingEvents> _pending = [];
        private readonly Lock _gate = new();
        private readonly IClientEventRegistry _registry;
        private readonly ILogger _logger;

        /// <summary>Creates the publisher.</summary>
        /// <param name="registry">The registered client events.</param>
        /// <param name="logger">Where failed deliveries are logged.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        protected ClientEventPublisher(IClientEventRegistry registry, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(logger);

            _registry = registry;
            _logger = logger;
        }

        /// <inheritdoc />
        public void Publish(IDataBatch? batch, ClientEvent clientEvent)
        {
            string payload = Validate(clientEvent);

            // --- Without a batch: send now, in the background ---
            if (batch is null)
            {
                _ = SendAsync(clientEvent);
                return;
            }

            // --- With a batch: join the batch's one commit callback ---
            lock (_gate)
            {
                if (!_pending.TryGetValue(batch, out PendingEvents? pending))
                {
                    // The callback is registered before the batch is remembered: a batch that already
                    // executed refuses it, and nothing is then left waiting on a commit that has passed.
                    PendingEvents created = new();
                    batch.OnCommitted(_ => Flush(batch, created));
                    _pending.Add(batch, created);
                    pending = created;
                }

                pending.Add(clientEvent, payload);
            }
        }

        /// <inheritdoc />
        public Task PublishAsync(ClientEvent clientEvent)
        {
            // Validation throws here, to the caller; only the delivery's failures are swallowed.
            _ = Validate(clientEvent);
            return SendAsync(clientEvent);
        }

        /// <summary>
        ///     Refuses an event that this host cannot send, such as one published from a scope
        ///     without a tenant. Runs after the name and payload refusals and before anything is
        ///     registered or sent; the base refuses nothing more.
        /// </summary>
        /// <param name="clientEvent">The event.</param>
        /// <exception cref="InvalidOperationException">The event cannot be sent from this scope.</exception>
        protected virtual void ValidateScope(ClientEvent clientEvent)
        {
        }

        /// <summary>
        ///     Delivers one validated event to its users' connections, or to every connection of the
        ///     tenant when it names no users. A failure, thrown or faulted, is logged by the caller.
        /// </summary>
        /// <param name="clientEvent">The event.</param>
        /// <returns>A task that completes once the event was handed to the transport.</returns>
        protected abstract Task DeliverAsync(ClientEvent clientEvent);

        /// <summary>Applies every refusal and returns the payload's JSON text.</summary>
        /// <param name="clientEvent">The event.</param>
        /// <returns>The payload as serialised, which identifies it among a batch's events.</returns>
        private string Validate(ClientEvent clientEvent)
        {
            // --- Shape ---
            ArgumentNullException.ThrowIfNull(clientEvent);
            if (clientEvent.UserIds is null)
            {
                throw new ArgumentException("A client event's user ids are an empty list, not null, when it is for every connected user of the tenant.", nameof(clientEvent));
            }

            // --- The name is registered ---
            if (!_registry.IsRegistered(clientEvent.Name))
            {
                throw new InvalidOperationException(
                    $"The client event '{clientEvent.Name}' is not registered. Register it in the feature that publishes it with contribution.ClientEvent(name).");
            }

            // --- The payload is thin ---
            // Measured as sent: UTF-8 under the platform's JSON options, the payload's runtime type.
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(clientEvent.Payload, TellmaJson.Options);
            if (payload.Length > ClientEvent.MaxPayloadBytes)
            {
                throw new InvalidOperationException(
                    $"The payload of the client event '{clientEvent.Name}' serialises to {payload.Length} bytes, more than the {ClientEvent.MaxPayloadBytes} a client event may carry. Send ids and short codes only; the client fetches what the event announces.");
            }

            // --- The host's own refusals ---
            ValidateScope(clientEvent);
            return Encoding.UTF8.GetString(payload);
        }

        /// <summary>Sends every distinct event published on a batch that committed.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="pending">Its events.</param>
        private void Flush(IDataBatch batch, PendingEvents pending)
        {
            List<ClientEvent> events;
            lock (_gate)
            {
                // Forgotten, so a later publish on the executed batch is refused by the batch rather
                // than waiting on a commit that has passed.
                _ = _pending.Remove(batch);
                events = pending.Drain();
            }

            foreach (ClientEvent clientEvent in events)
            {
                _ = SendAsync(clientEvent);
            }
        }

        /// <summary>Delivers an event, logging and swallowing any failure.</summary>
        /// <param name="clientEvent">The event.</param>
        /// <returns>A task that never faults.</returns>
        private async Task SendAsync(ClientEvent clientEvent)
        {
            try
            {
                await DeliverAsync(clientEvent).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A delivery failure never reaches the publisher: the client catches up on its
                // next read, so the event is logged and dropped.
                NotificationsLog.ClientEventFailed(_logger, exception, clientEvent.Name);
            }
        }

        /// <summary>The events published on one batch, each distinct name, set of users and payload once.</summary>
        private sealed class PendingEvents
        {
            private readonly HashSet<(string Name, string Users, string Payload)> _seen = [];
            private List<ClientEvent> _events = [];

            /// <summary>Adds an event unless an equal one was added.</summary>
            /// <param name="clientEvent">The event.</param>
            /// <param name="payload">Its payload's JSON text.</param>
            public void Add(ClientEvent clientEvent, string payload)
            {
                // The users as a set: the same ids in another order or repeated are one audience.
                string users = string.Join(',', clientEvent.UserIds.Distinct().Order());
                if (_seen.Add((clientEvent.Name, users, payload)))
                {
                    _events.Add(clientEvent);
                }
            }

            /// <summary>Takes the events, leaving none.</summary>
            /// <returns>The events, in the order first added.</returns>
            public List<ClientEvent> Drain()
            {
                List<ClientEvent> events = _events;
                _events = [];
                _seen.Clear();
                return events;
            }
        }
    }
}
