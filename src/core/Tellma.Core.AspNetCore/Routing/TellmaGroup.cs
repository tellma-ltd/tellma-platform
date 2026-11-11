// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore.Routing
{
    /// <summary>The platform's route groups, as the endpoint audit tells their endpoints apart.</summary>
    internal enum TellmaGroup
    {
        /// <summary><c>/{tenantId}/api/web</c>.</summary>
        Web,

        /// <summary><c>/{tenantId}/api/v{version}</c>.</summary>
        Api,

        /// <summary><c>/{tenantId}/hub</c>.</summary>
        Hub,

        /// <summary><c>/{tenantId}/blobs</c>.</summary>
        Blobs,

        /// <summary><c>/api</c>, for a distribution's tenantless endpoints.</summary>
        Tenantless,
    }

    /// <summary>Stamps an endpoint with the platform group it was mapped on.</summary>
    /// <param name="Group">The group.</param>
    internal sealed record TellmaGroupMetadata(TellmaGroup Group);

    /// <summary>
    ///     Stamps an endpoint the platform itself mapped, which the audit's rules on a distribution's
    ///     route words do not apply to.
    /// </summary>
    internal sealed class PlatformEndpointMetadata
    {
        /// <summary>The one instance.</summary>
        public static readonly PlatformEndpointMetadata Instance = new();

        /// <summary>Prevents other instances.</summary>
        private PlatformEndpointMetadata()
        {
        }
    }

    /// <summary>
    ///     What <c>WithMutation</c> stamped on an endpoint whose group had not yet stamped its
    ///     <see cref="TenantEndpointMetadata" />; the group's final convention folds it in.
    /// </summary>
    /// <param name="IsMutation">Whether the endpoint changes data.</param>
    internal sealed record MutationStamp(bool IsMutation);

    /// <summary>
    ///     Marks a tenant endpoint that stated neither <c>WithMutation</c> nor its own
    ///     <see cref="TenantEndpointMetadata" />. The audit refuses it, so whether it changes data is
    ///     always a decision someone made.
    /// </summary>
    internal sealed class MutationUnstatedMetadata
    {
        /// <summary>The one instance.</summary>
        public static readonly MutationUnstatedMetadata Instance = new();

        /// <summary>Prevents other instances.</summary>
        private MutationUnstatedMetadata()
        {
        }
    }

    /// <summary>
    ///     Stamped by the access guard's wrapper on the endpoint it wraps: the request delegate it
    ///     produced, which the audit compares with the endpoint's own to prove nothing replaced it.
    /// </summary>
    /// <param name="Guarded">The guarded request delegate.</param>
    internal sealed record TenantGuardMetadata(Microsoft.AspNetCore.Http.RequestDelegate Guarded);

    /// <summary>
    ///     Stamped by a surface's own front door on the endpoints it guards outside the platform's
    ///     groups, as the MCP feature's request filter does for its endpoint.
    /// </summary>
    /// <param name="Surface">The surface whose front door runs the access guard.</param>
    internal sealed record TenantFrontDoorMetadata(TenantSurface Surface);
}
