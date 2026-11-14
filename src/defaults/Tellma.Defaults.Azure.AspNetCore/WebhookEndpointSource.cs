// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Tellma.Core.AspNetCore.Routing;
using Tellma.Core.Webhooks;

namespace Tellma.Defaults.Azure.AspNetCore
{
    /// <summary>
    ///     The webhook endpoint, <c>GET</c> and <c>POST /api/webhooks/{key}</c>, contributed to the
    ///     platform's tenantless routes: a provider's callback carries no session and names no tenant,
    ///     and each receiver authenticates its own requests.
    /// </summary>
    internal sealed class WebhookEndpointSource : IPlatformEndpointSource
    {
        /// <summary>The tenantless reason the endpoint is stamped with.</summary>
        public const string TenantlessReason = "webhooks";

        /// <inheritdoc />
        public string Reason => TenantlessReason;

        /// <inheritdoc />
        public IReadOnlyList<IEndpointConventionBuilder> Map(IEndpointRouteBuilder endpoints)
        {
            return [endpoints.MapTellmaWebhooks()];
        }
    }
}
