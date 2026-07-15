// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

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
            if (registration is null)
            {
                context.Set<IdentitySessionClient>().Add(new IdentitySessionClient
                {
                    Sid = sid,
                    ClientId = clientId,
                    AuthorizationId = authorizationId,
                    CreatedUtc = now,
                });
            }
            else if (authorizationId is not null)
            {
                registration.AuthorizationId = authorizationId;
            }

            // The session was just observed.
            IdentitySession? session = await context.Set<IdentitySession>().FindAsync([sid], cancellationToken);
            session?.LastSeenUtc = now;

            await context.SaveChangesAsync(cancellationToken);
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

            session.TerminatedUtc ??= timeProvider.GetUtcNow();
            await context.SaveChangesAsync(cancellationToken);

            return [.. session.Clients];
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

        /// <summary>Bounds free-text columns to their configured lengths.</summary>
        private static string? Truncate(string? value, int maxLength)
        {
            return value is null || value.Length <= maxLength ? value : value[..maxLength];
        }
    }
}
