// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     One entry of a subject's membership list: a tenant the subject may be shown, and whether
    ///     the subject's user there is active. A navigation hint, never an authorization: every entry
    ///     into a tenant verifies the membership against the tenant's own database.
    /// </summary>
    /// <param name="Tenant">The tenant, as the catalog describes it.</param>
    /// <param name="IsActive">Whether the subject's user in the tenant is active.</param>
    /// <param name="UpdatedAt">When the hint was last written.</param>
    public sealed record TenantMembership(TenantDescriptor Tenant, bool IsActive, DateTimeOffset UpdatedAt)
    {
        /// <inheritdoc cref="TenantMembership(TenantDescriptor, bool, DateTimeOffset)" />
        public TenantDescriptor Tenant { get; init; } = Tenant ?? throw new ArgumentNullException(nameof(Tenant));
    }
}
