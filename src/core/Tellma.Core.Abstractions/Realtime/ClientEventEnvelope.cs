// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Realtime
{
    /// <summary>
    ///     A client event as the hub sends it: the argument of the client method <c>event</c>. It names
    ///     the tenant, so a client connected to several tenants routes it to the right one.
    /// </summary>
    /// <param name="Name">The event's name.</param>
    /// <param name="TenantId">The tenant the event belongs to.</param>
    /// <param name="Payload">The event's payload, or <see langword="null" /> for none.</param>
    public sealed record ClientEventEnvelope(string Name, int TenantId, object? Payload);
}
