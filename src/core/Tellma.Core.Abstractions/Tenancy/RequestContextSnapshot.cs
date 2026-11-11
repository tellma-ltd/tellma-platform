// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     The serialisable copy of a request context a background caller hands to
    ///     <see cref="ITenantScopeFactory.CreateScopeAsync" />: the tenant, the principal, the client
    ///     and the trace parent of the request the work came from.
    /// </summary>
    /// <remarks>
    ///     It never carries a locale. The scope resolves its language, calendar and zones afresh,
    ///     from the run-as user's stored preferences and then the tenant's settings, so background
    ///     work renders text as its user reads it now, whatever the enqueuing request negotiated.
    /// </remarks>
    /// <param name="TenantId">The tenant the work runs against: a positive id.</param>
    /// <param name="Kind">What kind of principal the work runs for.</param>
    /// <param name="Subject">The principal's stable identifier; null when the user is named by <paramref name="UserId" /> alone.</param>
    /// <param name="UserId">The run-as user in the tenant; null when the scope resolves it from the principal.</param>
    /// <param name="Client">The name of the client surface the work runs as, such as <c>worker</c>.</param>
    /// <param name="TraceParent">The trace parent of the request that enqueued the work, for log correlation only; null when there is none.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="TenantId" /> is less than 1, or <paramref name="Kind" /> is not a member of its enumeration.</exception>
    /// <exception cref="ArgumentException"><paramref name="Client" /> is null, empty or white space.</exception>
    public sealed record RequestContextSnapshot(
        int TenantId, PrincipalKind Kind, string? Subject, int? UserId, string Client, string? TraceParent)
    {
        /// <inheritdoc cref="RequestContextSnapshot(int, PrincipalKind, string?, int?, string, string?)" path="/param[@name='TenantId']" />
        public int TenantId
        {
            get;
            init => field = ValidTenantId(value);
        } = ValidTenantId(TenantId);

        /// <inheritdoc cref="RequestContextSnapshot(int, PrincipalKind, string?, int?, string, string?)" path="/param[@name='Kind']" />
        public PrincipalKind Kind
        {
            get;
            init => field = ValidKind(value);
        } = ValidKind(Kind);

        /// <inheritdoc cref="RequestContextSnapshot(int, PrincipalKind, string?, int?, string, string?)" path="/param[@name='Client']" />
        public string Client
        {
            get;
            init => field = ValidClient(value);
        } = ValidClient(Client);

        /// <summary>Refuses a tenant id that is not positive.</summary>
        /// <param name="tenantId">The tenant id.</param>
        /// <returns>The tenant id.</returns>
        private static int ValidTenantId(int tenantId)
        {
            return tenantId >= 1
                ? tenantId
                : throw new ArgumentOutOfRangeException(nameof(tenantId), tenantId, "A tenant id is a positive integer.");
        }

        /// <summary>Refuses a kind outside the enumeration.</summary>
        /// <param name="kind">The kind.</param>
        /// <returns>The kind.</returns>
        private static PrincipalKind ValidKind(PrincipalKind kind)
        {
            return Enum.IsDefined(kind)
                ? kind
                : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a principal kind.");
        }

        /// <summary>Refuses a blank client name.</summary>
        /// <param name="client">The client name.</param>
        /// <returns>The client name.</returns>
        private static string ValidClient(string client)
        {
            return string.IsNullOrWhiteSpace(client)
                ? throw new ArgumentException("A snapshot names the client surface its work runs as.", nameof(client))
                : client;
        }
    }
}
