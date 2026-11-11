// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     The provisioning trigger of a deployment that cannot provision on its own: tenants are
    ///     provisioned by the migrator's <c>provision</c> command instead. A caller that registers
    ///     tenants checks for this trigger first and registers nothing, so no tenant is left in
    ///     <c>Provisioning</c> with nothing to provision it.
    /// </summary>
    public sealed class NotConfiguredProvisioningTrigger : ITenantProvisioningTrigger
    {
        /// <summary>Refuses: this deployment has no provisioning to start.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="cancellationToken">Unused.</param>
        /// <returns>Never returns normally.</returns>
        /// <exception cref="InvalidOperationException">Always.</exception>
        public Task StartAsync(int tenantId, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "No tenant provisioning is configured for this deployment; provision tenants with the migrator's provision command, or register an ITenantProvisioningTrigger.");
        }
    }
}
