// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Core.AspNetCore.Realtime
{
    /// <summary>
    ///     The names of the hub's groups. Every group is qualified by its tenant, so no send to one
    ///     tenant's group can reach another tenant's connections. Only the server assigns a connection
    ///     to groups, from the claims its front door minted; a client names none.
    /// </summary>
    /// <remarks>
    ///     The letter after the tenant's prefix names the family, so no two families share a name:
    ///     <c>u</c> a user, <c>s</c> a subject and <c>x</c> a session.
    /// </remarks>
    internal static class HubGroups
    {
        /// <summary>Every connection of a tenant: <c>t{tenantId}</c>.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The group's name.</returns>
        public static string Tenant(int tenantId)
        {
            return string.Create(CultureInfo.InvariantCulture, $"t{tenantId}");
        }

        /// <summary>One user's connections in a tenant: <c>t{tenantId}.u{userId}</c>.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="userId">The user's id in the tenant.</param>
        /// <returns>The group's name.</returns>
        public static string User(int tenantId, int userId)
        {
            return string.Create(CultureInfo.InvariantCulture, $"t{tenantId}.u{userId}");
        }

        /// <summary>One subject's connections in a tenant: <c>t{tenantId}.s{subject}</c>.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="subject">The identity's subject.</param>
        /// <returns>The group's name.</returns>
        public static string Subject(int tenantId, string subject)
        {
            return string.Create(CultureInfo.InvariantCulture, $"t{tenantId}.s{subject}");
        }

        /// <summary>One session's connections in a tenant: <c>t{tenantId}.x{handle}</c>.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="sessionHandle">The session's handle.</param>
        /// <returns>The group's name.</returns>
        public static string Session(int tenantId, string sessionHandle)
        {
            return string.Create(CultureInfo.InvariantCulture, $"t{tenantId}.x{sessionHandle}");
        }

        /// <summary>The groups a connection belongs to, in a fixed order.</summary>
        /// <param name="claims">The connection's claims.</param>
        /// <returns>Its tenant, user, subject and session groups.</returns>
        public static string[] Of(HubConnectionClaims claims)
        {
            return
            [
                Tenant(claims.TenantId),
                User(claims.TenantId, claims.UserId),
                Subject(claims.TenantId, claims.Subject),
                Session(claims.TenantId, claims.SessionHandle),
            ];
        }
    }
}
