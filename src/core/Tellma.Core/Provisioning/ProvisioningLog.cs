// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Provisioning
{
    /// <summary>Source-generated log messages of the provisioning step runner.</summary>
    /// <remarks>Messages name tenants by id and steps by name, never an administrator's email address or subject.</remarks>
    internal static partial class ProvisioningLog
    {
        /// <summary>A provisioning step completed and was recorded.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="step">The step's name.</param>
        /// <param name="version">The version that completed.</param>
        [LoggerMessage(
            EventName = "ProvisioningStepCompleted",
            Level = LogLevel.Information,
            Message = "Provisioning step {Step} version {Version} completed on tenant {TenantId}.")]
        public static partial void StepCompleted(ILogger logger, int tenantId, string step, int version);

        /// <summary>A provisioning step failed; the steps after it did not run.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="step">The step's name.</param>
        /// <param name="exception">The failure.</param>
        [LoggerMessage(
            EventName = "ProvisioningStepFailed",
            Level = LogLevel.Error,
            Message = "Provisioning step {Step} failed on tenant {TenantId}; the steps after it did not run.")]
        public static partial void StepFailed(ILogger logger, int tenantId, string step, Exception exception);
    }
}
