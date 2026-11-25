// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The body of one call of the client telemetry relay (<c>POST /api/client-telemetry</c>): one
    ///     browser tab's items of one tenant, with the envelope the relay checks and labels them by.
    /// </summary>
    /// <param name="Version">
    ///     The UI version, the distribution's informational version the SPA was built as. It equals the
    ///     version part of the call's <c>Tellma-Client</c> header.
    /// </param>
    /// <param name="TabId">The browser tab's id, 8 to 40 characters drawn from letters, digits, <c>_</c> and <c>-</c>.</param>
    /// <param name="TenantId">
    ///     The tenant the items were recorded in, as the client claims it, for filtering alone; null for
    ///     the items of tenantless pages. The relay never authorizes by it and never tags a metric with it.
    /// </param>
    /// <param name="SentAt">The client's clock when it sent the batch, which corrects the items' times by the client's skew.</param>
    /// <param name="Items">The items, oldest first; at most 100.</param>
    public sealed record ClientTelemetryBatch(
        string Version,
        string TabId,
        int? TenantId,
        DateTimeOffset SentAt,
        IReadOnlyList<ClientTelemetryItem> Items);
}
