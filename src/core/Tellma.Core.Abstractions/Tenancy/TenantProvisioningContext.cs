// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     What a <see cref="ITenantProvisioningStep" /> runs against: the tenant, the services of the
    ///     system scope the step runs in, whether the tenant is being provisioned, and the first
    ///     administrator the run names.
    /// </summary>
    /// <param name="Tenant">The tenant the step runs for.</param>
    /// <param name="Services">
    ///     The services of the tenant scope the step runs in: a system principal's, opened for this
    ///     step alone. Steps resolve the platform's services from it rather than injecting a root
    ///     provider.
    /// </param>
    /// <param name="IsNew">
    ///     True while the tenant is <see cref="TenantState.Provisioning" />, which is when its first
    ///     provisioning runs; false when a later migration re-runs a step whose version moved.
    /// </param>
    /// <param name="AdminSubject">
    ///     The first administrator's identity-provider subject, in Development only; null elsewhere,
    ///     where the administrator obtains a subject through an invitation.
    /// </param>
    /// <param name="AdminEmail">The first administrator's email address, or null when the run names none.</param>
    public sealed record TenantProvisioningContext(
        TenantDescriptor Tenant,
        IServiceProvider Services,
        bool IsNew,
        string? AdminSubject,
        string? AdminEmail);
}
