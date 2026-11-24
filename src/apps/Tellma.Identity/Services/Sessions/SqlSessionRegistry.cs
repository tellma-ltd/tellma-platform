// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;

namespace Tellma.Identity.Services.Sessions
{
    /// <summary>The SQL-backed session registry.</summary>
    /// <param name="context">The identity store.</param>
    /// <param name="timeProvider">The clock.</param>
    public sealed class SqlSessionRegistry(TellmaIdentityDbContext context, TimeProvider timeProvider) : ISessionRegistry
    {
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
        public async Task RegisterClientAsync(
            string sid, string clientId, string? authorizationId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sid);
            ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

            DateTimeOffset now = timeProvider.GetUtcNow();

            IdentitySessionClient? registration =
                await context.Set<IdentitySessionClient>().FindAsync([sid, clientId], cancellationToken);
            IdentitySessionClient? added = null;
            if (registration is null)
            {
                added = new IdentitySessionClient
                {
                    Sid = sid,
                    ClientId = clientId,
                    AuthorizationId = authorizationId,
                    CreatedUtc = now,
                };
                context.Set<IdentitySessionClient>().Add(added);
            }
            else if (authorizationId is not null)
            {
                registration.AuthorizationId = authorizationId;
            }

            // The session was just observed.
            IdentitySession? session = await context.Set<IdentitySession>().FindAsync([sid], cancellationToken);
            session?.LastSeenUtc = now;

            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (added is not null && IsDuplicateKey(exception))
            {
                // Another grant of the same session (a second tab) registered the client between
                // the read above and this insert. Its row stands, and this grant's authorization
                // is recorded on it as a later registration's would be. The refused row is
                // forgotten first: left tracked, every later save on this context would insert
                // it again and fail the same way.
                context.Entry(added).State = EntityState.Detached;
                IdentitySessionClient? winner =
                    await context.Set<IdentitySessionClient>().FindAsync([sid, clientId], cancellationToken);
                if (winner is null)
                {
                    throw;
                }

                if (authorizationId is not null)
                {
                    winner.AuthorizationId = authorizationId;
                }

                await context.SaveChangesAsync(cancellationToken);
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<IdentitySessionClient>> TerminateAsync(string sid, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sid);

            IdentitySession? session = await context.Set<IdentitySession>()
                .Include(static s => s.Clients)
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

            return wasActive ? [.. session.Clients] : [];
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<IdentitySessionClient>> TerminateAllAsync(string userId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);

            List<IdentitySession> sessions = await context.Set<IdentitySession>()
                .Include(static s => s.Clients)
                .Where(s => s.UserId == userId && s.TerminatedUtc == null)
                .ToListAsync(cancellationToken);

            DateTimeOffset now = timeProvider.GetUtcNow();
            List<IdentitySessionClient> clients = [];
            foreach (IdentitySession session in sessions)
            {
                session.TerminatedUtc = now;
                clients.AddRange(session.Clients);
            }

            await context.SaveChangesAsync(cancellationToken);
            return clients;
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
