// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Webhooks
{
    /// <summary>
    ///     Marks the webhook endpoint so a host's own middleware can recognize it.
    /// </summary>
    /// <remarks>
    ///     A webhook belongs to the deployable, not to a tenant: the caller holds no platform
    ///     credentials, presents no tenant, and the tenant is reached later through the correlation
    ///     carried in the payload. A distribution's tenant-resolution middleware must therefore
    ///     short-circuit on this metadata rather than trying (and failing) to resolve a tenant for
    ///     the request.
    /// </remarks>
    public sealed class WebhookEndpointMetadata;
}
