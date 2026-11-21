// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Realtime;

namespace Tellma.Core.Notifications
{
    /// <summary>
    ///     The client event publisher of a host without the real-time hub, such as the migrator: it
    ///     applies every refusal and coalesces a batch's events as any publisher does, then delivers
    ///     and counts nothing. A host that maps the hub replaces it.
    /// </summary>
    /// <param name="registry">The registered client events.</param>
    /// <param name="logger">Where failed deliveries would be logged.</param>
    public sealed class NullClientEventPublisher(IClientEventRegistry registry, ILogger<NullClientEventPublisher> logger)
        : ClientEventPublisher(registry, logger)
    {
        /// <inheritdoc />
        protected override Task DeliverAsync(ClientEvent clientEvent)
        {
            return Task.CompletedTask;
        }
    }
}
