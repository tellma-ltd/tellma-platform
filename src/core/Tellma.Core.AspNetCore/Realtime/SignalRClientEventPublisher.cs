// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Notifications;

namespace Tellma.Core.AspNetCore.Realtime
{
    /// <summary>
    ///     Publishes client events through the hub: to <c>t{tenantId}.u{userId}</c> for each user an
    ///     event names, or to <c>t{tenantId}</c> when it names none, the tenant being the scope's. The
    ///     hosting mode carries each send to every instance it reaches.
    /// </summary>
    /// <remarks>
    ///     A scope without a tenant cannot publish: its events would name no tenant's groups. The
    ///     refusal is thrown with the base's, before anything is registered or sent.
    /// </remarks>
    internal sealed class SignalRClientEventPublisher : ClientEventPublisher
    {
        private readonly IHubContext<TellmaHub> _hub;
        private readonly IRequestContextAccessor _accessor;
        private readonly RealtimeMetrics _metrics;

        /// <summary>The scope's tenant, taken on the first publish; a scope binds its tenant once.</summary>
        private int? _tenantId;

        /// <summary>Creates the publisher.</summary>
        /// <param name="hub">The hub's context.</param>
        /// <param name="accessor">The scope's request context.</param>
        /// <param name="registry">The registered client events.</param>
        /// <param name="metrics">The realtime instruments.</param>
        /// <param name="logger">Where failed deliveries are logged.</param>
        public SignalRClientEventPublisher(
            IHubContext<TellmaHub> hub,
            IRequestContextAccessor accessor,
            IClientEventRegistry registry,
            RealtimeMetrics metrics,
            ILogger<SignalRClientEventPublisher> logger)
            : base(registry, logger)
        {
            ArgumentNullException.ThrowIfNull(hub);
            ArgumentNullException.ThrowIfNull(accessor);
            ArgumentNullException.ThrowIfNull(metrics);

            _hub = hub;
            _accessor = accessor;
            _metrics = metrics;
        }

        /// <inheritdoc />
        protected override void ValidateScope(ClientEvent clientEvent)
        {
            // Read now, while the scope is surely alive: a batch's events are sent from its commit callback.
            _tenantId = _accessor.Current.Tenant?.Id
                ?? throw new InvalidOperationException(
                    $"The client event '{clientEvent.Name}' was published from a scope without a tenant; a client event is sent to one tenant's connections, so it is published from a tenant request or a tenant background scope.");
        }

        /// <inheritdoc />
        protected override async Task DeliverAsync(ClientEvent clientEvent)
        {
            // The base validates, and so sets the tenant, before every delivery.
            int tenantId = _tenantId ?? throw new InvalidOperationException("A client event was delivered before its scope was validated.");
            ClientEventEnvelope envelope = new(clientEvent.Name, tenantId, clientEvent.Payload);

            // --- The tenant's group, or each named user's group in the tenant, never Clients.User ---
            IClientProxy target = clientEvent.UserIds.Count == 0
                ? _hub.Clients.Group(HubGroups.Tenant(tenantId))
                : _hub.Clients.Groups([.. clientEvent.UserIds.Distinct().Order().Select(userId => HubGroups.User(tenantId, userId))]);
            await target.SendAsync(TellmaHub.EventMethod, envelope).ConfigureAwait(false);
            _metrics.RecordEvent(clientEvent.Name);
        }
    }
}
