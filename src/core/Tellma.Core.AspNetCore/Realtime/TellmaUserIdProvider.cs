// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.SignalR;
using Tellma.Core.AspNetCore.Authentication;

namespace Tellma.Core.AspNetCore.Realtime
{
    /// <summary>
    ///     Names a connection's user by the identity's subject, so SignalR's per-user addressing and
    ///     Azure SignalR Service's user-scoped operations name the principal the identity server does.
    /// </summary>
    /// <remarks>
    ///     The platform never addresses events by user: a subject open in two tenants holds a
    ///     connection in each, and an event of one tenant must never reach the other's. Events go to
    ///     the tenant-qualified groups alone.
    /// </remarks>
    public sealed class TellmaUserIdProvider : IUserIdProvider
    {
        /// <inheritdoc />
        public string? GetUserId(HubConnectionContext connection)
        {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.User?.FindFirst(TellmaClaimTypes.Subject)?.Value;
        }
    }
}
