// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     What an endpoint asks of the tenant and of the caller's session before it runs, read from
    ///     the endpoint's metadata by the front door that calls <see cref="ITenantAccessGuard" />.
    /// </summary>
    /// <param name="IsMutation">Whether the endpoint changes the tenant's data. A read-only or frozen tenant refuses a mutation.</param>
    /// <param name="Assurance">The assurance bar the caller's session must meet; null when the endpoint sets none.</param>
    /// <param name="AllowWhenReadOnly">
    ///     Whether a read-only tenant still serves the mutation: one that only changes the caller's
    ///     own presentation or bookkeeping, starts an export, or stops platform work. A frozen tenant
    ///     refuses it all the same.
    /// </param>
    public sealed record TenantAccessRequirement(bool IsMutation, RequireAssuranceMetadata? Assurance, bool AllowWhenReadOnly = false);
}
