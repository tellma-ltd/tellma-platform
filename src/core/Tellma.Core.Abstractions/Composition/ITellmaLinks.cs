// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     Builds the links server code puts into an email, an invitation or a notification, so every
    ///     such link follows the client's route grammar and starts at the deployment's public origin.
    /// </summary>
    /// <remarks>A singleton, safe to call from any thread. It reads <c>Tellma:PublicOrigin</c> and nothing else.</remarks>
    public interface ITellmaLinks
    {
        /// <summary>The link to a tenant's home: <c>{PublicOrigin}/{tenantId}</c>.</summary>
        /// <param name="tenantId">The tenant: a positive id.</param>
        /// <returns>The absolute link.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tenantId" /> is less than 1.</exception>
        /// <exception cref="InvalidOperationException"><c>Tellma:PublicOrigin</c> is not configured.</exception>
        string Tenant(int tenantId);

        /// <summary>
        ///     The link to a record's details page,
        ///     <c>{PublicOrigin}/{tenantId}/{segment}/{id}</c>, when <paramref name="resource" />
        ///     names a stack that serves details; otherwise the link to the tenant's home.
        /// </summary>
        /// <param name="tenantId">The tenant: a positive id.</param>
        /// <param name="resource">The record's resource, such as <c>acme.SalesInvoice</c>; null when the link targets no record.</param>
        /// <param name="id">The record's id; null when the link targets no record.</param>
        /// <returns>
        ///     The absolute link. It is the tenant's home when <paramref name="resource" /> or
        ///     <paramref name="id" /> is null, when the resource names no stack or one without a
        ///     details page, and when the id is not positive.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tenantId" /> is less than 1.</exception>
        /// <exception cref="InvalidOperationException"><c>Tellma:PublicOrigin</c> is not configured.</exception>
        string Target(int tenantId, string? resource, long? id);
    }
}
