// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Realtime
{
    /// <summary>
    ///     Publishes client events to the current tenant's open clients. A scoped service; publishers
    ///     never see the hub's transport.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Both members refuse, synchronously and before anything is registered or sent, an event
    ///         whose name is not registered or whose serialised payload exceeds
    ///         <see cref="ClientEvent.MaxPayloadBytes" />, with <see cref="InvalidOperationException" />.
    ///         A failed delivery is logged and never thrown or retried: a client catches up on its next
    ///         read.
    ///     </para>
    ///     <para>
    ///         A host without the hub, such as the migrator, publishes through a publisher that applies
    ///         the refusals and delivers nothing.
    ///     </para>
    /// </remarks>
    public interface IClientEventPublisher
    {
        /// <summary>
        ///     Publishes an event after <paramref name="batch" /> commits, or at once when no batch is
        ///     given. Every event published on one batch is sent from one commit callback, once per
        ///     distinct name, audience and payload; an event published on a batch that does not commit
        ///     is never sent. Without a batch, the event is sent in the background and this call
        ///     returns at once.
        /// </summary>
        /// <param name="batch">The batch whose commit the event announces, or <see langword="null" /> to publish at once.</param>
        /// <param name="clientEvent">The event.</param>
        /// <exception cref="ArgumentNullException"><paramref name="clientEvent" /> is null.</exception>
        /// <exception cref="ArgumentException">The event's user ids are null rather than empty.</exception>
        /// <exception cref="InvalidOperationException">The event's name is not registered, its payload is too large, in a host that delivers events the scope has no tenant, or <paramref name="batch" /> already executed.</exception>
        void Publish(IDataBatch? batch, ClientEvent clientEvent);

        /// <summary>Publishes an event at once to the current tenant's clients.</summary>
        /// <param name="clientEvent">The event.</param>
        /// <returns>A task that completes once the event was handed to the transport; it never faults on a delivery failure.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="clientEvent" /> is null.</exception>
        /// <exception cref="ArgumentException">The event's user ids are null rather than empty.</exception>
        /// <exception cref="InvalidOperationException">The event's name is not registered, its payload is too large, or, in a host that delivers events, the scope has no tenant.</exception>
        Task PublishAsync(ClientEvent clientEvent);
    }
}
