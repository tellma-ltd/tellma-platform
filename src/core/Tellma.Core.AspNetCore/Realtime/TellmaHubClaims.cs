// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Claims;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Authentication;

namespace Tellma.Core.AspNetCore.Realtime
{
    /// <summary>
    ///     The claims the hub's front door mints for a connection once the tenant's access guard has
    ///     passed: the tenant, the user and the session's handle. With the identity's subject they are
    ///     everything the hub knows about a connection, and its groups are computed from them alone.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Self-hosted, the connection's principal is the connect request's: the session cookie's
    ///         principal plus the identity <see cref="Create" /> builds. With Azure SignalR Service the
    ///         connect never reaches the instance, so the negotiate copies <see cref="Select" /> into
    ///         the service's token and the service rebuilds the principal from it.
    ///     </para>
    ///     <para>
    ///         The values are ids, never secrets: the handle already rides the readable profile cookie.
    ///     </para>
    /// </remarks>
    public static class TellmaHubClaims
    {
        /// <summary>The claim type of the tenant's id.</summary>
        public const string TenantId = "tellma_tid";

        /// <summary>The claim type of the user's id in the tenant.</summary>
        public const string UserId = "tellma_uid";

        /// <summary>The claim type of the session's handle.</summary>
        public const string SessionHandle = "tellma_sh";

        /// <summary>The authentication type of the identity <see cref="Create" /> builds.</summary>
        public const string AuthenticationType = "Tellma.Hub";

        /// <summary>The claim types the hub reads, the subject first.</summary>
        private static readonly string[] Types = [TellmaClaimTypes.Subject, TenantId, UserId, SessionHandle];

        /// <summary>The identity of a connection, built from a context the tenant's access guard bound.</summary>
        /// <param name="context">The bound context: a tenant, a user and a session handle.</param>
        /// <returns>An identity holding exactly the three hub claims.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="context" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The context lacks a tenant, a user or a session handle.</exception>
        public static ClaimsIdentity Create(RequestContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // The guard binds all three for a cookie session's member; a context without one was not
            // bound by the guard, and the hub would compute groups from nothing.
            int tenantId = context.Tenant?.Id
                ?? throw new InvalidOperationException("The hub's claims are created from a context bound to a tenant.");
            int userId = context.UserId
                ?? throw new InvalidOperationException("The hub's claims are created from a context whose user the access guard connected.");
            string handle = string.IsNullOrEmpty(context.SessionHandle)
                ? throw new InvalidOperationException("The hub's claims are created from a context authenticated by a session cookie, which names the session's handle.")
                : context.SessionHandle;

            return new ClaimsIdentity(
                [
                    new Claim(TenantId, tenantId.ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer32),
                    new Claim(UserId, userId.ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer32),
                    new Claim(SessionHandle, handle),
                ],
                AuthenticationType);
        }

        /// <summary>
        ///     The claims Azure SignalR Service carries to the hub: the identity's subject and the three
        ///     hub claims, each once, and nothing else of the principal.
        /// </summary>
        /// <param name="user">The negotiate request's principal, which the front door completed.</param>
        /// <returns>The subject and the hub claims present on the principal, fresh and in a fixed order.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="user" /> is null.</exception>
        public static IEnumerable<Claim> Select(ClaimsPrincipal user)
        {
            ArgumentNullException.ThrowIfNull(user);

            // Fresh claims: nothing of the source identity (its issuer, its properties) rides along,
            // and a claim the principal carries twice is copied once.
            List<Claim> selected = [];
            foreach (string type in Types)
            {
                if (user.FindFirst(type) is { } claim)
                {
                    selected.Add(new Claim(type, claim.Value));
                }
            }

            return selected;
        }

        /// <summary>
        ///     Whether a principal carries any hub claim, which only the front door may add: a
        ///     principal that does before the door ran was not built by the platform.
        /// </summary>
        /// <param name="user">The principal.</param>
        /// <returns>True when it carries any of the three hub claims.</returns>
        internal static bool Carries(ClaimsPrincipal user)
        {
            return user.HasClaim(static claim => claim.Type is TenantId or UserId or SessionHandle);
        }

        /// <summary>
        ///     Reads a connection's subject and hub claims, each of which the principal must carry
        ///     exactly once, with the ids positive integers.
        /// </summary>
        /// <param name="user">The connection's principal.</param>
        /// <param name="claims">The claims read, when they are sound.</param>
        /// <returns>True when every claim is present once and well formed.</returns>
        internal static bool TryRead(ClaimsPrincipal? user, [NotNullWhen(true)] out HubConnectionClaims? claims)
        {
            claims = null;
            if (user is null
                || Single(user, TellmaClaimTypes.Subject) is not { } subject
                || Single(user, TenantId) is not { } tenant
                || Single(user, UserId) is not { } userId
                || Single(user, SessionHandle) is not { } handle
                || !int.TryParse(tenant, NumberStyles.None, CultureInfo.InvariantCulture, out int tenantId) || tenantId <= 0
                || !int.TryParse(userId, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0)
            {
                return false;
            }

            claims = new HubConnectionClaims(tenantId, id, subject, handle);
            return true;
        }

        /// <summary>The value of a claim the principal carries exactly once, across its identities.</summary>
        /// <param name="user">The principal.</param>
        /// <param name="type">The claim type.</param>
        /// <returns>The value; null when the claim is absent, empty or carried more than once.</returns>
        private static string? Single(ClaimsPrincipal user, string type)
        {
            // Two values of one type would leave the groups ambiguous: refused, never resolved.
            string? value = null;
            foreach (Claim claim in user.FindAll(type))
            {
                if (value is not null)
                {
                    return null;
                }

                value = claim.Value;
            }

            return string.IsNullOrEmpty(value) ? null : value;
        }
    }

    /// <summary>What the hub knows about one connection, read from its principal's claims.</summary>
    /// <param name="TenantId">The tenant the connection belongs to.</param>
    /// <param name="UserId">The user's id in the tenant.</param>
    /// <param name="Subject">The identity's subject.</param>
    /// <param name="SessionHandle">The handle of the session the connection was opened under.</param>
    internal sealed record HubConnectionClaims(int TenantId, int UserId, string Subject, string SessionHandle);
}
