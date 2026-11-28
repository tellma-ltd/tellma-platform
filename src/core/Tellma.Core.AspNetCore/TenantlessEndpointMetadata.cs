// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore
{
    /// <summary>
    ///     Marks an endpoint that serves no tenant, with the reason it needs none. Every endpoint
    ///     outside the tenant route groups carries it or an authorization policy, and only an endpoint
    ///     carrying it may allow anonymous callers.
    /// </summary>
    /// <param name="Reason">Why the endpoint serves no tenant, such as <c>health</c>.</param>
    public sealed record TenantlessEndpointMetadata(string Reason)
    {
        /// <summary>Why the endpoint serves no tenant.</summary>
        public string Reason { get; init; } = string.IsNullOrWhiteSpace(Reason)
            ? throw new ArgumentException("A tenantless endpoint states why it serves no tenant.", nameof(Reason))
            : Reason;
    }
}
