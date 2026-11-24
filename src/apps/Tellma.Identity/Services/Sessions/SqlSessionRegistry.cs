// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;

namespace Tellma.Identity.Services.Sessions
{
    /// <summary>The SQL-backed session registry.</summary>
    /// <param name="context">The identity store.</param>
    /// <param name="timeProvider">The clock.</param>
    public sealed class SqlSessionRegistry(TellmaIdentityDbContext context, TimeProvider timeProvider) : ISessionRegistry
    {
        /// <summary>
        ///     How many times a registration saves before a lost insert fails it. Each lost insert
        ///     is a row another grant wrote, which the next attempt reads instead, and a registration
        ///     inserts at most two rows, so the third attempt has nothing left to lose.
        /// </summary>
        private const int MaxRegistrationAttempts = 3;

        /// <inheritdoc />
        public async Task UpsertSessionAsync(
            string sid, string userId, string? userAgent, string? ipAddress, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sid);
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);

            DateTimeOffset now = timeProvider.GetUtcNow();
            IdentitySession? session = await context.Set<IdentitySession>().FindAsync([sid], cancellationToken);
            if (session is null)
            {
                context.Set<IdentitySession>().Add(new IdentitySession
                {
                    Sid = sid,
                    UserId = userId,
                    CreatedUtc = now,
                    LastSeenUtc = now,
                    UserAgent = Truncate(userAgent, 256),
                    IpAddress = Truncate(ipAddress, 64),
                });
            }
            else
            {
                session.LastSeenUtc = now;
                session.TerminatedUtc = null;
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<bool> RegisterClientAsync(
            string sid, string clientId, string? authorizationId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sid);
            ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

            // The registration runs in a transaction of its own, which a host that configures a
            // retrying execution strategy needs to run as one unit.
            DateTimeOffset now = timeProvider.GetUtcNow();
            IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(
                token => RegisterInTransactionAsync(sid, clientId, authorizationId, now, token),
                cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<IdentitySessionClient>> TerminateAsync(string sid, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sid);

            IdentitySession? session = await context.Set<IdentitySession>()
                .FirstOrDefaultAsync(s => s.Sid == sid, cancellationToken);
            if (session is null)
            {
                return [];
            }

            // Idempotent: a second termination of the same session must not re-revoke grants or
            // re-deliver logout tokens, mirroring TerminateAllAsync's active-only filter.
            bool wasActive = session.TerminatedUtc is null;
            session.TerminatedUtc ??= timeProvider.GetUtcNow();
            await context.SaveChangesAsync(cancellationToken);

            // Read only now that the session is marked: a registration in flight either committed
            // before this write could take the session's row, and is read here, or is refused.
            return wasActive ? await ReadRegistrationsAsync([sid], cancellationToken) : [];
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<IdentitySessionClient>> TerminateAllAsync(string userId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);

            List<IdentitySession> sessions = await context.Set<IdentitySession>()
                .Where(s => s.UserId == userId && s.TerminatedUtc == null)
                .ToListAsync(cancellationToken);
            if (sessions.Count == 0)
            {
                return [];
            }

            DateTimeOffset now = timeProvider.GetUtcNow();
            foreach (IdentitySession session in sessions)
            {
                session.TerminatedUtc = now;
            }

            // Read only now that the sessions are marked, as TerminateAsync does.
            await context.SaveChangesAsync(cancellationToken);
            return await ReadRegistrationsAsync([.. sessions.Select(static s => s.Sid)], cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<IdentitySession>> GetActiveSessionsAsync(string userId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);

            return await context.Set<IdentitySession>()
                .Where(s => s.UserId == userId && s.TerminatedUtc == null)
                .OrderByDescending(static s => s.LastSeenUtc)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task MarkNotifiedAsync(string sid, string clientId, CancellationToken cancellationToken)
        {
            await context.Set<IdentitySessionClient>()
                .Where(c => c.Sid == sid && c.ClientId == clientId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(static c => c.NotifiedUtc, timeProvider.GetUtcNow()),
                    cancellationToken);
        }

        /// <inheritdoc />
        public Task TouchAsync(string sid, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sid);

            // The terminated filter is the whole reason this is not a plain load-and-set: a
            // request arriving with a cookie for a session someone signed out of would otherwise
            // stamp it as freshly seen.
            return context.Set<IdentitySession>()
                .Where(s => s.Sid == sid && s.TerminatedUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(static s => s.LastSeenUtc, timeProvider.GetUtcNow()),
                    cancellationToken);
        }

        /// <inheritdoc />
        public async Task<SessionPruneResult> PruneAsync(
            DateTimeOffset idleSince, DateTimeOffset terminatedSince, CancellationToken cancellationToken)
        {
            // Both passes are set-based: a sweep can touch far more rows than the change tracker
            // should hold, and neither pass needs the entities themselves.
            int expired = await context.Set<IdentitySession>()
                .Where(s => s.TerminatedUtc == null && s.LastSeenUtc < idleSince)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(static s => s.TerminatedUtc, timeProvider.GetUtcNow()),
                    cancellationToken);

            // Ordered after the expiry pass on purpose, and safely so: rows this sweep just
            // terminated carry the current instant, which is never older than the retention
            // cutoff, so nothing is created and destroyed in the same run. The client
            // registrations cascade with the session row.
            int removed = await context.Set<IdentitySession>()
                .Where(s => s.TerminatedUtc != null && s.TerminatedUtc < terminatedSince)
                .ExecuteDeleteAsync(cancellationToken);

            return new SessionPruneResult(expired, removed);
        }

        /// <summary>
        ///     Records a registration, and commits it only while the session is still active.
        /// </summary>
        /// <returns>False when the session has ended or is gone; nothing is then recorded.</returns>
        private async Task<bool> RegisterInTransactionAsync(
            string sid, string clientId, string? authorizationId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            for (int attempt = 1; ; attempt++)
            {
                List<object> added = await StageRegistrationAsync(sid, clientId, authorizationId, now, cancellationToken);
                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                    break;
                }
                catch (DbUpdateException exception) when (
                    added.Count > 0 && attempt < MaxRegistrationAttempts && IsDuplicateKey(exception))
                {
                    // Another grant of the same session (a second tab) inserted one of these rows
                    // between the reads above and this save. Its row stands, so the next attempt
                    // reads it and inserts only what is still missing. The failed save rolled
                    // back to its own savepoint, so the transaction goes on. The refused rows are
                    // forgotten first: left tracked, every later save on this context would insert
                    // them again and fail the same way. Any other failure, a foreign key on a
                    // session deleted meanwhile among them, is not caught, so the grant fails.
                    foreach (object row in added)
                    {
                        context.Entry(row).State = EntityState.Detached;
                    }
                }
            }

            // The session was just observed, and it must still be active for the registration to
            // stand. The update locks the session's row until this transaction ends, and a logout
            // ends the session by writing that row before it reads the registrations to revoke. So
            // either the logout waits for this registration and then revokes what it recorded, or
            // the logout wrote first and this registration is refused: a grant that loses to a
            // logout gets no code, rather than an authorization no logout will ever reach.
            int active = await context.Set<IdentitySession>()
                .Where(s => s.Sid == sid && s.TerminatedUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(static s => s.LastSeenUtc, now), cancellationToken);
            if (active == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                ForgetRegistrations(sid);
                return false;
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        /// <summary>
        ///     Reads the client registrations of sessions, each with every authorization its grants
        ///     used. A logout calls it only after it has ended the sessions, so a registration either
        ///     committed before that, and is read here, or is refused.
        /// </summary>
        /// <returns>The registrations, tracked.</returns>
        private async Task<IReadOnlyList<IdentitySessionClient>> ReadRegistrationsAsync(
            IReadOnlyCollection<string> sids, CancellationToken cancellationToken)
        {
            return await context.Set<IdentitySessionClient>()
                .Include(static c => c.Authorizations)
                .Where(c => sids.Contains(c.Sid))
                .ToListAsync(cancellationToken);
        }

        /// <summary>
        ///     Stops tracking a session's registration rows after a refused registration rolled
        ///     back, so the context no longer holds rows the store does not.
        /// </summary>
        private void ForgetRegistrations(string sid)
        {
            List<EntityEntry> entries = [.. context.ChangeTracker.Entries().Where(entry => entry.Entity switch
            {
                IdentitySessionClient client => client.Sid == sid,
                IdentitySessionClientAuthorization authorization => authorization.Sid == sid,
                _ => false,
            })];
            foreach (EntityEntry entry in entries)
            {
                entry.State = EntityState.Detached;
            }
        }

        /// <summary>
        ///     Adds whichever of a registration's two rows is missing: the client under the session,
        ///     and the authorization under the client. The client also keeps the latest
        ///     authorization in its own column, the one the previous engine release reads.
        /// </summary>
        /// <returns>The rows added, still to be saved.</returns>
        private async Task<List<object>> StageRegistrationAsync(
            string sid, string clientId, string? authorizationId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            List<object> added = [];
            IdentitySessionClient? registered =
                await context.Set<IdentitySessionClient>().FindAsync([sid, clientId], cancellationToken);
            if (registered is null)
            {
                IdentitySessionClient client = new()
                {
                    Sid = sid,
                    ClientId = clientId,
                    AuthorizationId = authorizationId,
                    CreatedUtc = now,
                };
                context.Set<IdentitySessionClient>().Add(client);
                added.Add(client);
            }
            else if (authorizationId is not null)
            {
                registered.AuthorizationId = authorizationId;
            }

            // Every authorization the client's grants use is kept, never replaced: each backs
            // refresh tokens that a logout of this session must stop.
            if (authorizationId is not null
                && await context.Set<IdentitySessionClientAuthorization>().FindAsync(
                    [sid, clientId, authorizationId], cancellationToken) is null)
            {
                IdentitySessionClientAuthorization authorization = new()
                {
                    Sid = sid,
                    ClientId = clientId,
                    AuthorizationId = authorizationId,
                    CreatedUtc = now,
                };
                context.Set<IdentitySessionClientAuthorization>().Add(authorization);
                added.Add(authorization);
            }

            return added;
        }

        /// <summary>Whether a save failed on a row that another writer inserted first.</summary>
        private static bool IsDuplicateKey(DbUpdateException exception)
        {
            // 2627: a primary key or unique constraint; 2601: a unique index.
            return exception.InnerException is SqlException { Number: 2627 or 2601 };
        }

        /// <summary>Bounds free-text columns to their configured lengths.</summary>
        private static string? Truncate(string? value, int maxLength)
        {
            return value is null || value.Length <= maxLength ? value : value[..maxLength];
        }
    }
}
