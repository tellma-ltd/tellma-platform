// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore
{
    /// <summary>
    ///     Marks a tenant endpoint: its surface, whether it changes data, and whether a
    ///     <c>ReadOnly</c> tenant still lets it change data. The tenant middleware resolves the tenant
    ///     of exactly the endpoints that carry it, and the access guard's state verdict reads it.
    /// </summary>
    /// <param name="Surface">The surface the endpoint belongs to.</param>
    /// <param name="IsMutation">Whether the endpoint changes data.</param>
    /// <param name="AllowWhenReadOnly">Whether a <c>ReadOnly</c> tenant still serves the mutation.</param>
    public sealed record TenantEndpointMetadata(TenantSurface Surface, bool IsMutation, bool AllowWhenReadOnly)
    {
        /// <summary>The surface the endpoint belongs to.</summary>
        public TenantSurface Surface { get; init; } = Enum.IsDefined(Surface)
            ? Surface
            : throw new ArgumentOutOfRangeException(nameof(Surface), Surface, "Not a tenant surface.");
    }
}
