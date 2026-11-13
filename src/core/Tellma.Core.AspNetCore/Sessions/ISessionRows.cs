// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore.Sessions
{
    /// <summary>A live session row, as the primary-key read returns it.</summary>
    /// <param name="Subject">The identity subject.</param>
    /// <param name="Sid">The identity server's session id, if the sign-in named one.</param>
    /// <param name="Ticket">The protected ticket.</param>
    /// <param name="TokensVersion">The refresh compare-and-set counter.</param>
    /// <param name="ExpiresAt">When the session expires.</param>
    internal sealed record SessionRow(string Subject, string? Sid, byte[] Ticket, int TokensVersion, DateTimeOffset ExpiresAt);

    /// <summary>A session a revocation deleted.</summary>
    /// <param name="Key">The session's key, to evict it from this instance's cache; never passed on.</param>
    /// <param name="Handle">The session's handle, for the listeners.</param>
    /// <param name="Subject">The session's subject, for the listeners.</param>
    internal sealed record EndedSession(string Key, string Handle, string Subject);

    /// <summary>
    ///     The session rows in the catalog: one statement per operation, each the exact shape the
    ///     catalog's session table is written with. A seam so the store's logic runs without a database
    ///     in the unit suite.
    /// </summary>
    internal interface ISessionRows
    {
        /// <summary>Inserts a new session, at tokens version 0.</summary>
        /// <param name="key">The key.</param>
        /// <param name="handle">The handle.</param>
        /// <param name="subject">The subject.</param>
        /// <param name="sid">The identity server's session id.</param>
        /// <param name="ticket">The protected ticket.</param>
        /// <param name="expiresAt">When the session expires.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>A task that completes when the row is written.</returns>
        Task InsertAsync(string key, string handle, string subject, string? sid, byte[] ticket, DateTimeOffset expiresAt, CancellationToken cancellationToken);

        /// <summary>Reads a session that has not expired.</summary>
        /// <param name="key">The key.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The row, or null when there is none or it expired.</returns>
        Task<SessionRow?> ReadAsync(string key, CancellationToken cancellationToken);

        /// <summary>Writes a renewal, unless a refresh moved the tokens version since the ticket was read.</summary>
        /// <param name="key">The key.</param>
        /// <param name="ticket">The protected ticket.</param>
        /// <param name="expiresAt">The new expiry.</param>
        /// <param name="tokensVersion">The version the ticket was read at.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>True when the row was written; false when the version moved or the row is gone.</returns>
        Task<bool> RenewAsync(string key, byte[] ticket, DateTimeOffset expiresAt, int tokensVersion, CancellationToken cancellationToken);

        /// <summary>
        ///     Writes a refreshed ticket with the compare-and-set: only at the expected version, which it
        ///     increments.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="ticket">The protected ticket.</param>
        /// <param name="expiresAt">The expiry.</param>
        /// <param name="expectedVersion">The version the refresh started from.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>The new version, or null when the compare-and-set was lost.</returns>
        Task<int?> ReplaceAsync(string key, byte[] ticket, DateTimeOffset expiresAt, int expectedVersion, CancellationToken cancellationToken);

        /// <summary>Deletes a session.</summary>
        /// <param name="key">The key.</param>
        /// <param name="cancellationToken">Cancels the delete.</param>
        /// <returns>True when a row was deleted.</returns>
        Task<bool> DeleteAsync(string key, CancellationToken cancellationToken);

        /// <summary>Deletes every session of an identity server session.</summary>
        /// <param name="sid">The identity server's session id.</param>
        /// <param name="cancellationToken">Cancels the delete.</param>
        /// <returns>The sessions deleted.</returns>
        Task<IReadOnlyList<EndedSession>> DeleteBySidAsync(string sid, CancellationToken cancellationToken);

        /// <summary>Deletes every session of a subject.</summary>
        /// <param name="subject">The subject.</param>
        /// <param name="cancellationToken">Cancels the delete.</param>
        /// <returns>The sessions deleted.</returns>
        Task<IReadOnlyList<EndedSession>> DeleteBySubjectAsync(string subject, CancellationToken cancellationToken);

        /// <summary>Deletes one batch of the sessions that expired more than a day ago.</summary>
        /// <param name="cancellationToken">Cancels the delete.</param>
        /// <returns>How many rows the batch deleted.</returns>
        Task<int> SweepBatchAsync(CancellationToken cancellationToken);
    }
}
