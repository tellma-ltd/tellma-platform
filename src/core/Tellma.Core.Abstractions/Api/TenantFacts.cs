// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     What a browser tab needs to know about the tenant it shows: its kind, its lifecycle state
    ///     and whether the host serves the real-time hub, as <c>client/sync</c> reports them.
    /// </summary>
    /// <remarks>
    ///     A tenant that serves a member's call is <see cref="TenantState.Active" />,
    ///     <see cref="TenantState.ReadOnly" /> or <see cref="TenantState.Frozen" />; every other state
    ///     refuses the call before it runs.
    /// </remarks>
    /// <param name="Id">The tenant's id.</param>
    /// <param name="Category">Whether the tenant holds live books or is a sandbox.</param>
    /// <param name="State">The tenant's lifecycle state.</param>
    /// <param name="LiveTenantId">A sandbox's live tenant; null for a live tenant.</param>
    /// <param name="Realtime">Whether this host serves the real-time hub, so the client connects instead of polling.</param>
    public sealed record TenantFacts(int Id, TenantCategory Category, TenantState State, int? LiveTenantId, bool Realtime);
}
