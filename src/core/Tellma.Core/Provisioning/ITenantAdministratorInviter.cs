// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Provisioning
{
    /// <summary>
    ///     Invites a newly provisioned tenant's first administrator, after its provisioning steps and
    ///     before it turns active. The migrator resolves it from the tenant's system scope when one is
    ///     registered and the run names an administrator, and skips the invitation otherwise.
    /// </summary>
    internal interface ITenantAdministratorInviter
    {
        /// <summary>Invites the administrator the context names.</summary>
        /// <param name="context">The tenant, the system scope's services and the administrator.</param>
        /// <param name="cancellationToken">Cancels the invitation.</param>
        /// <returns>A task that completes when the invitation is committed.</returns>
        Task InviteAsync(TenantProvisioningContext context, CancellationToken cancellationToken);
    }
}
