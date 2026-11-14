// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Access
{
    /// <summary>
    ///     Source-generated log messages of the permission set, under the access category. No message
    ///     carries a filter's text, only its diagnostics' codes.
    /// </summary>
    internal static partial class AccessLog
    {
        /// <summary>
        ///     A stored permission grants nothing. Logged once per permission and problem for each
        ///     version of the tenant's permissions.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The exception the query engine threw validating the filter, a defect; null otherwise.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="userId">The user whose set build found it.</param>
        /// <param name="permissionId">The permission.</param>
        /// <param name="roleId">Its role.</param>
        /// <param name="resource">The permission's resource.</param>
        /// <param name="action">The permission's action.</param>
        /// <param name="problem">The problem's tag value.</param>
        /// <param name="diagnostics">The codes of the engine's diagnostics, comma-separated; empty for none.</param>
        [LoggerMessage(
            EventId = AccessEvents.PermissionDrift,
            EventName = nameof(AccessEvents.PermissionDrift),
            Level = LogLevel.Warning,
            Message = "Permission {PermissionId} of role {RoleId} in tenant {TenantId} grants nothing ({Problem}) on {Resource}:{Action}; found building the permissions of user {UserId}. Diagnostics: {Diagnostics}.")]
        public static partial void PermissionDrift(
            ILogger logger,
            Exception? exception,
            int tenantId,
            int userId,
            int permissionId,
            int roleId,
            string resource,
            string action,
            string problem,
            string diagnostics);

        /// <summary>A caller's permission set was built.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="userId">The user.</param>
        /// <param name="grants">The grants of the set.</param>
        /// <param name="problems">The permissions of the set that grant nothing.</param>
        /// <param name="milliseconds">How long the build took, in milliseconds.</param>
        [LoggerMessage(
            EventId = AccessEvents.AccessSetBuilt,
            EventName = nameof(AccessEvents.AccessSetBuilt),
            Level = LogLevel.Debug,
            Message = "Built the permissions of user {UserId} in tenant {TenantId}: {Grants} grants and {Problems} permissions that grant nothing, in {Milliseconds} ms.")]
        public static partial void AccessSetBuilt(ILogger logger, int tenantId, int userId, int grants, int problems, double milliseconds);
    }
}
