// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using Tellma.Core.AspNetCore.Authentication;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Observability;
using Tellma.Core.Tenancy;

namespace Tellma.Core.AspNetCore.Sessions
{
    /// <summary>A session ticket as the store read it.</summary>
    /// <param name="Ticket">The ticket, with the handle and the row's tokens version stamped.</param>
    /// <param name="Subject">The row's subject.</param>
    /// <param name="TokensVersion">The row's tokens version.</param>
    /// <param name="ExpiresAt">When the row expires.</param>
    internal sealed record StoredSession(AuthenticationTicket Ticket, string Subject, int TokensVersion, DateTimeOffset ExpiresAt);

    /// <summary>
    ///     The BFF's session store: rows in the catalog, a bounded per-instance cache in front of the
    ///     primary-key read, the tokens-version guard on renewals, and the revocations.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The cookie handler calls the overloads that take the request, and the store uses it for
    ///         three things: the profile cookie, which it writes with every write of the session; the
    ///         current key, which the session events need to refresh the row; and the sign-in rotation.
    ///         A sign-in over an existing session never renews that session's row: the callback marks the
    ///         request, the existing session reads as absent, and the sign-in stores a fresh key and ends
    ///         the old session — so a key known before a sign-in never names the session after it, and a
    ///         row never changes subject.
    ///     </para>
    ///     <para>A key never reaches a log, a metric, a listener or a ticket's properties.</para>
    /// </remarks>
    internal sealed class CatalogSessionStore : ICatalogSessionStore, IDisposable
    {
        /// <summary>The Data Protection purpose of a stored ticket.</summary>
        public const string ProtectorPurpose = "Tellma.Core.AspNetCore.Session";

        /// <summary>The most sessions an instance's cache holds.</summary>
        public const int CacheEntries = 50_000;

        /// <summary>The request item that marks a sign-in in progress, set by the sign-in callback.</summary>
        private static readonly object SignInItem = new();

        /// <summary>The request item holding the session a sign-in replaces.</summary>
        private static readonly object ReplacedItem = new();

        /// <summary>The request item holding the key of the session the request authenticated.</summary>
        private static readonly object KeyItem = new();

        private readonly ISessionRows _rows;
        private readonly IDataProtector _protector;
        private readonly TellmaSessionOptions _options;
        private readonly TimeProvider _time;
        private readonly HostMetrics _metrics;
        private readonly SessionTerminationNotifier _notifier;
        private readonly ILogger<CatalogSessionStore> _logger;
        private readonly bool _secureCookies;
        private readonly MemoryCache _cache;

        /// <summary>Creates the store.</summary>
        /// <param name="rows">The session rows.</param>
        /// <param name="dataProtection">Protects the stored tickets.</param>
        /// <param name="options">The session options.</param>
        /// <param name="time">The clock.</param>
        /// <param name="metrics">The host's instruments.</param>
        /// <param name="notifier">Tells the listeners which sessions ended.</param>
        /// <param name="cookies">Whether the host's cookies are restricted to HTTPS.</param>
        /// <param name="logger">The logger.</param>
        public CatalogSessionStore(
            ISessionRows rows,
            IDataProtectionProvider dataProtection,
            IOptions<TellmaSessionOptions> options,
            TimeProvider time,
            HostMetrics metrics,
            SessionTerminationNotifier notifier,
            SessionCookieSecurity cookies,
            ILogger<CatalogSessionStore> logger)
        {
            ArgumentNullException.ThrowIfNull(dataProtection);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(cookies);
            _rows = rows;
            _protector = dataProtection.CreateProtector(ProtectorPurpose);
            _options = options.Value;
            _time = time;
            _metrics = metrics;
            _notifier = notifier;
            _logger = logger;
            _secureCookies = cookies.Secure;
            _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = CacheEntries });
        }

        // ------------------------------------------------------------------------------------------
        // The request items the sign-in callback and the session events share with the store
        // ------------------------------------------------------------------------------------------

        /// <summary>Marks a request as a sign-in, so the session it carries is replaced rather than renewed.</summary>
        /// <param name="context">The sign-in callback's request.</param>
        public static void MarkSignIn(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.Items[SignInItem] = true;
        }

        /// <summary>The key of the session a request authenticated, which only the session events read.</summary>
        /// <param name="context">The request.</param>
        /// <returns>The key, or null when the request authenticated no stored session.</returns>
        public static string? CurrentKey(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return context.Items.TryGetValue(KeyItem, out object? key) ? key as string : null;
        }

        // ------------------------------------------------------------------------------------------
        // ITicketStore: the cookie handler's calls
        // ------------------------------------------------------------------------------------------

        /// <inheritdoc />
        public Task<string> StoreAsync(AuthenticationTicket ticket)
        {
            return StoreAsync(ticket, httpContext: null, CancellationToken.None);
        }

        /// <inheritdoc />
        public Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
        {
            return StoreAsync(ticket, httpContext: null, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext? httpContext, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(ticket);

            // --- The session's names and its row ---
            string subject = ticket.Principal.FindFirst(TellmaClaimTypes.Subject)?.Value
                ?? throw new InvalidOperationException("A session's principal names no subject.");
            TenantMembershipDirectory.ValidateSubject(subject, nameof(ticket));
            string? sid = ticket.Principal.FindFirst(TellmaClaimTypes.SessionId)?.Value;
            string key = SessionKeys.NewKey();
            string handle = SessionKeys.HandleOf(key);

            ticket.Properties.Items[SessionProperties.Handle] = handle;
            SessionProperties.SetTokensVersion(ticket.Properties, 0);
            DateTimeOffset expiresAt = ticket.Properties.ExpiresUtc ?? (_time.GetUtcNow() + _options.IdleLifetime);

            byte[] payload = Serialize(ticket);
            await _rows.InsertAsync(key, handle, subject, sid, _protector.Protect(payload), expiresAt, cancellationToken).ConfigureAwait(false);
            Cache(key, new CachedSession(payload, subject, 0, expiresAt));
            _metrics.RecordSessionOperation(HostMetrics.StoreOperation, HostMetrics.DatabaseSource);

            if (httpContext is not null)
            {
                // --- A sign-in over an existing session ends that session ---
                if (httpContext.Items.Remove(ReplacedItem, out object? replaced) && replaced is EndedSession previous)
                {
                    await EndAsync(previous, cancellationToken).ConfigureAwait(false);
                }

                httpContext.Items[KeyItem] = key;
                ProfileCookie.Append(httpContext.Response, ticket.Principal, handle, expiresAt, _secureCookies);
            }

            return key;
        }

        /// <inheritdoc />
        public Task RenewAsync(string key, AuthenticationTicket ticket)
        {
            return RenewAsync(key, ticket, httpContext: null, CancellationToken.None);
        }

        /// <inheritdoc />
        public Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
        {
            return RenewAsync(key, ticket, httpContext: null, cancellationToken);
        }

        /// <inheritdoc />
        public async Task RenewAsync(string key, AuthenticationTicket ticket, HttpContext? httpContext, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(ticket);

            // --- The write, unless a refresh moved the row since this ticket was read ---
            int version = SessionProperties.GetTokensVersion(ticket.Properties) ?? 0;
            DateTimeOffset expiresAt = ticket.Properties.ExpiresUtc ?? (_time.GetUtcNow() + _options.IdleLifetime);
            string subject = ticket.Principal.FindFirst(TellmaClaimTypes.Subject)?.Value ?? string.Empty;
            byte[] payload = Serialize(ticket);
            bool written = await _rows.RenewAsync(key, _protector.Protect(payload), expiresAt, version, cancellationToken).ConfigureAwait(false);
            _metrics.RecordSessionOperation(HostMetrics.RenewOperation, HostMetrics.DatabaseSource);

            StoredSession? current;
            if (written)
            {
                Cache(key, new CachedSession(payload, subject, version, expiresAt));
                current = new StoredSession(ticket, subject, version, expiresAt);
            }
            else
            {
                // The row moved or went: what this instance cached is stale, and the row as it is now
                // is what the profile cookie describes.
                _cache.Remove(key);
                current = await ReadFreshAsync(key, cancellationToken).ConfigureAwait(false);
            }

            // --- The profile cookie travels with the session cookie ---
            if (httpContext is not null && current is not null && SessionProperties.GetHandle(current.Ticket.Properties) is string handle)
            {
                ProfileCookie.Append(httpContext.Response, current.Ticket.Principal, handle, expiresAt, _secureCookies);
            }
        }

        /// <inheritdoc />
        public Task<AuthenticationTicket?> RetrieveAsync(string key)
        {
            return RetrieveAsync(key, httpContext: null, CancellationToken.None);
        }

        /// <inheritdoc />
        public Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
        {
            return RetrieveAsync(key, httpContext: null, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<AuthenticationTicket?> RetrieveAsync(string key, HttpContext? httpContext, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(key);
            StoredSession? session = await ReadAsync(key, cancellationToken).ConfigureAwait(false);

            if (httpContext is not null)
            {
                // --- A sign-in replaces the session the request carries rather than renewing it ---
                if (httpContext.Items.ContainsKey(SignInItem))
                {
                    if (session is not null && SessionProperties.GetHandle(session.Ticket.Properties) is string handle)
                    {
                        httpContext.Items[ReplacedItem] = new EndedSession(key, handle, session.Subject);
                    }

                    return null;
                }

                if (session is not null)
                {
                    httpContext.Items[KeyItem] = key;
                }
            }

            return session?.Ticket;
        }

        /// <inheritdoc />
        public Task RemoveAsync(string key)
        {
            return RemoveAsync(key, httpContext: null, CancellationToken.None);
        }

        /// <inheritdoc />
        public Task RemoveAsync(string key, CancellationToken cancellationToken)
        {
            return RemoveAsync(key, httpContext: null, cancellationToken);
        }

        /// <inheritdoc />
        public async Task RemoveAsync(string key, HttpContext? httpContext, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(key);
            _cache.Remove(key);
            _ = await _rows.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
            _metrics.RecordSessionOperation(HostMetrics.RemoveOperation, HostMetrics.DatabaseSource);
        }

        // ------------------------------------------------------------------------------------------
        // ICatalogSessionStore: the revocations and the sweep
        // ------------------------------------------------------------------------------------------

        /// <inheritdoc />
        public async Task RevokeBySidAsync(string sid, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(sid);
            await RevokeAsync(await _rows.DeleteBySidAsync(sid, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task RevokeBySubjectAsync(string subject, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(subject);
            await RevokeAsync(await _rows.DeleteBySubjectAsync(subject, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
        {
            // Batches bound each statement's locks; a short batch is the last one.
            int total = 0;
            int deleted;
            do
            {
                deleted = await _rows.SweepBatchAsync(cancellationToken).ConfigureAwait(false);
                total += deleted;
            }
            while (deleted >= SqlSessionRows.SweepBatchSize);

            _metrics.RecordSessionOperation(HostMetrics.SweepOperation, HostMetrics.DatabaseSource);
            return total;
        }

        // ------------------------------------------------------------------------------------------
        // The refresh's operations, which the session events call
        // ------------------------------------------------------------------------------------------

        /// <summary>Reads a session from the catalog, bypassing the cache, and caches what it read.</summary>
        /// <param name="key">The key.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The session, or null when it is gone, expired or unreadable.</returns>
        public async Task<StoredSession?> ReadFreshAsync(string key, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(key);
            _cache.Remove(key);
            return await ReadAsync(key, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        ///     Writes a refreshed ticket with the compare-and-set, and on success stamps the new tokens
        ///     version into the ticket and caches it.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="ticket">The refreshed ticket.</param>
        /// <param name="expiresAt">The session's expiry, unchanged by a refresh.</param>
        /// <param name="expectedVersion">The version the refresh started from.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>The new version, or null when another writer moved the row first.</returns>
        public async Task<int?> ReplaceAsync(string key, AuthenticationTicket ticket, DateTimeOffset expiresAt, int expectedVersion, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(ticket);
            byte[] payload = Serialize(ticket);
            int? version = await _rows.ReplaceAsync(key, _protector.Protect(payload), expiresAt, expectedVersion, cancellationToken).ConfigureAwait(false);
            if (version is int written)
            {
                SessionProperties.SetTokensVersion(ticket.Properties, written);
                string subject = ticket.Principal.FindFirst(TellmaClaimTypes.Subject)?.Value ?? string.Empty;
                Cache(key, new CachedSession(payload, subject, written, expiresAt));
            }
            else
            {
                _cache.Remove(key);
            }

            return version;
        }

        /// <summary>Ends one session: deletes its row, evicts it and tells the listeners.</summary>
        /// <param name="session">The session.</param>
        /// <param name="cancellationToken">Cancels the delete.</param>
        /// <returns>A task that completes when the session ended and the listeners were told.</returns>
        public async Task EndAsync(EndedSession session, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            _cache.Remove(session.Key);
            bool deleted = await _rows.DeleteAsync(session.Key, cancellationToken).ConfigureAwait(false);
            _metrics.RecordSessionOperation(HostMetrics.RemoveOperation, HostMetrics.DatabaseSource);
            if (deleted)
            {
                await _notifier.NotifyAsync(session.Subject, [session.Handle]).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _cache.Dispose();
        }

        // ------------------------------------------------------------------------------------------
        // Reading, caching and protecting tickets
        // ------------------------------------------------------------------------------------------

        /// <summary>The cache's entry for a session: the unprotected ticket bytes and the row's columns.</summary>
        /// <param name="Payload">The serialized ticket.</param>
        /// <param name="Subject">The row's subject.</param>
        /// <param name="TokensVersion">The row's tokens version.</param>
        /// <param name="ExpiresAt">When the row expires.</param>
        private sealed record CachedSession(byte[] Payload, string Subject, int TokensVersion, DateTimeOffset ExpiresAt);

        /// <summary>Reads a session: the cache, or else the primary-key read.</summary>
        /// <param name="key">The key.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The session, or null when it is gone, expired or unreadable.</returns>
        private async Task<StoredSession?> ReadAsync(string key, CancellationToken cancellationToken)
        {
            // Nothing a cookie carries reaches the catalog unless it has a key's shape.
            if (!SessionKeys.IsWellFormedKey(key))
            {
                return null;
            }

            DateTimeOffset now = _time.GetUtcNow();

            // --- The cache: a fresh ticket per read, because the handler changes the one it gets ---
            if (_cache.TryGetValue(key, out CachedSession? cached) && cached is not null)
            {
                if (cached.ExpiresAt > now)
                {
                    _metrics.RecordSessionOperation(HostMetrics.RetrieveOperation, HostMetrics.CacheSource);
                    return Materialize(cached.Payload, cached.Subject, cached.TokensVersion, cached.ExpiresAt);
                }

                _cache.Remove(key);
            }

            // --- The primary-key read ---
            SessionRow? row = await _rows.ReadAsync(key, cancellationToken).ConfigureAwait(false);
            _metrics.RecordSessionOperation(HostMetrics.RetrieveOperation, HostMetrics.DatabaseSource);
            if (row is null)
            {
                return null;
            }

            byte[] payload;
            try
            {
                payload = _protector.Unprotect(row.Ticket);
            }
            catch (CryptographicException exception)
            {
                // A ticket protected under a key ring this instance does not hold: the user signs in again.
                HostLog.SessionUnreadable(_logger, exception);
                return null;
            }

            StoredSession? session = Materialize(payload, row.Subject, row.TokensVersion, row.ExpiresAt);
            if (session is not null)
            {
                Cache(key, new CachedSession(payload, row.Subject, row.TokensVersion, row.ExpiresAt));
            }

            return session;
        }

        /// <summary>A ticket from its bytes, with the row's tokens version stamped.</summary>
        /// <param name="payload">The serialized ticket.</param>
        /// <param name="subject">The row's subject.</param>
        /// <param name="version">The row's tokens version.</param>
        /// <param name="expiresAt">When the row expires.</param>
        /// <returns>The session, or null when the bytes are not a ticket of the row's subject.</returns>
        private StoredSession? Materialize(byte[] payload, string subject, int version, DateTimeOffset expiresAt)
        {
            AuthenticationTicket? ticket = TicketSerializer.Default.Deserialize(payload);
            if (ticket is null || !string.Equals(ticket.Principal.FindFirst(TellmaClaimTypes.Subject)?.Value, subject, StringComparison.Ordinal))
            {
                HostLog.SessionUnreadable(_logger, exception: null);
                return null;
            }

            SessionProperties.SetTokensVersion(ticket.Properties, version);
            return new StoredSession(ticket, subject, version, expiresAt);
        }

        /// <summary>Caches a session for the cache's lifetime, or until it expires if that is sooner.</summary>
        /// <param name="key">The key.</param>
        /// <param name="session">The session.</param>
        private void Cache(string key, CachedSession session)
        {
            TimeSpan remaining = session.ExpiresAt - _time.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                _cache.Remove(key);
                return;
            }

            _ = _cache.Set(key, session, new MemoryCacheEntryOptions
            {
                Size = 1,
                AbsoluteExpirationRelativeToNow = remaining < _options.CacheTtl ? remaining : _options.CacheTtl,
            });
        }

        /// <summary>
        ///     The bytes a ticket is stored as: its serialization without the tokens version, which the
        ///     row's column holds and every read stamps.
        /// </summary>
        /// <param name="ticket">The ticket.</param>
        /// <returns>The serialized ticket.</returns>
        private static byte[] Serialize(AuthenticationTicket ticket)
        {
            AuthenticationProperties properties = ticket.Properties.Clone();
            _ = properties.Items.Remove(SessionProperties.TokensVersion);
            return TicketSerializer.Default.Serialize(new AuthenticationTicket(ticket.Principal, properties, ticket.AuthenticationScheme));
        }

        /// <summary>Ends revoked sessions on this instance and tells the listeners.</summary>
        /// <param name="ended">The sessions the revocation deleted.</param>
        /// <returns>A task that completes when the listeners were told.</returns>
        private async Task RevokeAsync(IReadOnlyList<EndedSession> ended)
        {
            foreach (EndedSession session in ended)
            {
                _cache.Remove(session.Key);
            }

            _metrics.RecordSessionOperation(HostMetrics.RevokeOperation, HostMetrics.DatabaseSource);
            await _notifier.NotifyAsync(ended).ConfigureAwait(false);
        }
    }
}
