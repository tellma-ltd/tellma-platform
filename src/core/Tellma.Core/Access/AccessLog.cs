// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Access
{
    /// <summary>
    ///     Source-generated log messages of the permission set, the evaluator and the user
    ///     connection, under the access category. No message carries a filter's text, only its
    ///     diagnostics' codes.
    /// </summary>
    internal static partial class AccessLog
    {
        /// <summary>
        ///     A successful response carried no access witness: the endpoint's securable, or for a
        ///     member endpoint the member witness, was never evaluated during the request. The
        ///     response is replaced with an error.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant; 0 outside one.</param>
        /// <param name="userId">The scope's user, when one is bound.</param>
        /// <param name="endpoint">The endpoint's display name.</param>
        /// <param name="expected">What was expected: the declared securable as <c>resource:action</c>, or <c>member</c>.</param>
        [LoggerMessage(
            EventId = AccessEvents.MissingWitness,
            EventName = nameof(AccessEvents.MissingWitness),
            Level = LogLevel.Error,
            Message = "The successful response of {Endpoint} for user {UserId} in tenant {TenantId} carried no access witness: {Expected} was never evaluated. The response is replaced with an error.")]
        public static partial void MissingWitness(ILogger logger, int tenantId, int? userId, string endpoint, string expected);

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

        /// <summary>
        ///     A batch's guard failed twice in one call: the caller's permissions or the tenant's
        ///     settings moved again after the batch was recomposed from fresh state.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="userId">The connected user.</param>
        /// <param name="purpose">The batch's purpose.</param>
        [LoggerMessage(
            EventId = AccessEvents.GuardThrash,
            EventName = nameof(AccessEvents.GuardThrash),
            Level = LogLevel.Warning,
            Message = "The guard of a {Purpose} batch of user {UserId} in tenant {TenantId} failed again after the batch was recomposed; the call is refused as stale.")]
        public static partial void GuardThrash(ILogger logger, int tenantId, int userId, string purpose);

        /// <summary>
        ///     A connect prologue refused the caller: no user of the tenant matched, or the user is
        ///     deactivated. The subject is never logged.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="userId">The user the prologue matched, when it matched one.</param>
        /// <param name="reason">The connect result's tag value.</param>
        [LoggerMessage(
            EventId = AccessEvents.ConnectRejected,
            EventName = nameof(AccessEvents.ConnectRejected),
            Level = LogLevel.Information,
            Message = "The connection to tenant {TenantId} was refused ({Reason}); matched user: {UserId}.")]
        public static partial void ConnectRejected(ILogger logger, int tenantId, int? userId, string reason);

        /// <summary>
        ///     A query under a composed access filter exceeded the compile ceilings: an
        ///     administrator's configuration problem, surfaced as an error rather than a silent denial.
        ///     The exception carries the engine's diagnostics.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The compile's refusal, which carries the diagnostics.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="userId">The user whose decision composed the filter.</param>
        /// <param name="resource">The decided securable's resource.</param>
        /// <param name="action">The decided securable's action.</param>
        /// <param name="roleIds">The ids of the roles whose grants the filter unions, comma-separated.</param>
        [LoggerMessage(
            EventId = AccessEvents.FilterTooComplex,
            EventName = nameof(AccessEvents.FilterTooComplex),
            Level = LogLevel.Error,
            Message = "The access filter of user {UserId} in tenant {TenantId} on {Resource}:{Action} exceeded the compile ceilings; the roles involved are {RoleIds}.")]
        public static partial void FilterTooComplex(ILogger logger, Exception exception, int tenantId, int userId, string resource, string action, string roleIds);
    }
}
