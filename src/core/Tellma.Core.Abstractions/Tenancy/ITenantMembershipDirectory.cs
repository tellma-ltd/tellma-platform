// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     The catalog's membership hints: which tenants a subject may be shown. A navigation hint,
    ///     never an authorization source; the tenant's own database decides every entry.
    /// </summary>
    /// <remarks>
    ///     The directory answers from the catalog alone and never opens a tenant database. A stale
    ///     hint is harmless: at worst it shows a tenant that answers not-found, or hides one that is
    ///     reachable by its address, until the next reconciliation.
    /// </remarks>
    public interface ITenantMembershipDirectory
    {
        /// <summary>
        ///     The subject's memberships in one catalog query, retired tenants excluded, ordered by
        ///     tenant id.
        /// </summary>
        /// <param name="subject">The identity subject, or a service account's client id.</param>
        /// <param name="cancellationToken">Cancels the query.</param>
        /// <returns>The memberships; empty when the subject has none.</returns>
        /// <exception cref="ArgumentException"><paramref name="subject" /> is empty, longer than 255 characters, or not printable ASCII without spaces.</exception>
        Task<IReadOnlyList<TenantMembership>> ListAsync(string subject, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Records hints, inserting new ones and updating the activity of existing ones. Best
        ///     effort: a failure is logged and metered and never thrown, so it never fails the save
        ///     that wrote the users the hints describe.
        /// </summary>
        /// <param name="records">The hints; a later record for the same tenant and subject wins.</param>
        /// <param name="cancellationToken">Cancels the write; a cancellation is the one failure that is thrown.</param>
        /// <returns>A task that completes when the write succeeded or its failure was logged.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="records" /> or one of its entries is null.</exception>
        Task RecordAsync(IReadOnlyList<TenantMembershipRecord> records, CancellationToken cancellationToken = default);
    }
}
