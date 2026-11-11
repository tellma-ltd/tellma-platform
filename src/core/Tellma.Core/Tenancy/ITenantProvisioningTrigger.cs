// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     Starts the provisioning of a tenant the catalog has just registered: whatever creates its
    ///     database, migrates it and runs its provisioning steps, in or out of the process.
    /// </summary>
    /// <remarks>
    ///     The composition registers <see cref="NotConfiguredProvisioningTrigger" />, under which the
    ///     admin surface's tenant registration answers that provisioning is not available and registers
    ///     nothing. A deployment that provisions from its control plane registers its own trigger.
    /// </remarks>
    public interface ITenantProvisioningTrigger
    {
        /// <summary>Starts the provisioning of a registered tenant; returns once it is started, not finished.</summary>
        /// <param name="tenantId">The registered tenant, in <c>Provisioning</c>.</param>
        /// <param name="cancellationToken">Cancels the start.</param>
        /// <returns>A task that completes when the provisioning is started.</returns>
        Task StartAsync(int tenantId, CancellationToken cancellationToken = default);
    }
}
