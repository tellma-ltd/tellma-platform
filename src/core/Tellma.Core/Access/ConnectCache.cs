// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;

namespace Tellma.Core.Access
{
    // The cache is spec 0014 §6.3's connect cache: not a versioned-cache kind, since every batch's
    // prologue re-validates the premises its entry supplies; bounded in entries by the limit of the
    // permission sets it composes. §7.1 single-flights the cold connects of one key.
    /// <summary>
    ///     The process's connected users, by tenant and subject: what a request needs to compose its
    ///     first batch without a round trip of its own. Concurrent first requests of one subject on
    ///     one instance share one cold connect.
    /// </summary>
    internal sealed class ConnectCache : IDisposable
    {
        /// <summary>The entries.</summary>
        private readonly MemoryCache _cache;

        /// <summary>The cold connects in flight, by key; each removes itself before it completes.</summary>
        private readonly ConcurrentDictionary<ConnectKey, Task<ConnectedUser>> _flights = new();

        /// <summary>Creates the cache.</summary>
        /// <param name="options">The cache limits: <see cref="CacheOptions.PermissionsEntries" /> entries at most.</param>
        /// <exception cref="ArgumentNullException"><paramref name="options" /> is null.</exception>
        public ConnectCache(IOptions<CacheOptions> options)
        {
            ArgumentNullException.ThrowIfNull(options);
            _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.Value.PermissionsEntries });
        }

        /// <summary>The connected user of a subject in a tenant, when cached.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="subject">The subject.</param>
        /// <returns>The user, or null.</returns>
        public ConnectedUser? Find(int tenantId, string subject)
        {
            ArgumentNullException.ThrowIfNull(subject);
            return _cache.TryGetValue(new ConnectKey(tenantId, subject), out ConnectedUser? user) ? user : null;
        }

        /// <summary>Stores the connected user of a subject in a tenant, replacing any entry.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="subject">The subject.</param>
        /// <param name="user">The user.</param>
        public void Set(int tenantId, string subject, ConnectedUser user)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(user);
            _ = _cache.Set(new ConnectKey(tenantId, subject), user, new MemoryCacheEntryOptions { Size = 1 });
        }

        /// <summary>Drops the entry of a subject in a tenant, when present.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="subject">The subject.</param>
        public void Remove(int tenantId, string subject)
        {
            ArgumentNullException.ThrowIfNull(subject);
            _cache.Remove(new ConnectKey(tenantId, subject));
        }

        /// <summary>
        ///     Runs a cold connect of a subject once however many callers ask together: the first
        ///     caller runs it, the others wait for its user or its failure. A connect cancelled under
        ///     its starter fails only the starter: a caller still waiting runs one of its own.
        /// </summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="subject">The subject.</param>
        /// <param name="connect">The connect, run on the starter's scope.</param>
        /// <param name="cancellationToken">Cancels the caller's wait, and the connect when the caller started it.</param>
        /// <returns>The connected user.</returns>
        public async Task<ConnectedUser> ConnectOnceAsync(
            int tenantId, string subject, Func<CancellationToken, Task<ConnectedUser>> connect, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(connect);
            ConnectKey key = new(tenantId, subject);
            while (true)
            {
                // --- Join the flight in progress, or start one ---
                TaskCompletionSource<ConnectedUser> mine = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<ConnectedUser> flight = _flights.GetOrAdd(key, mine.Task);
                if (ReferenceEquals(flight, mine.Task))
                {
                    try
                    {
                        ConnectedUser user = await connect(cancellationToken).ConfigureAwait(false);
                        _ = _flights.TryRemove(new KeyValuePair<ConnectKey, Task<ConnectedUser>>(key, flight));
                        mine.SetResult(user);
                    }
                    catch (OperationCanceledException cancelled)
                    {
                        _ = _flights.TryRemove(new KeyValuePair<ConnectKey, Task<ConnectedUser>>(key, flight));
                        mine.SetCanceled(cancelled.CancellationToken);
                        throw;
                    }
                    catch (Exception failure)
                    {
                        _ = _flights.TryRemove(new KeyValuePair<ConnectKey, Task<ConnectedUser>>(key, flight));
                        mine.SetException(failure);

                        // Observed here, so a failure no joiner awaited is never reported as unobserved.
                        _ = mine.Task.Exception;
                        throw;
                    }

                    return await flight.ConfigureAwait(false);
                }

                // --- A joiner: the starter's cancellation is not the joiner's, which then starts its own ---
                try
                {
                    return await flight.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    continue;
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _cache.Dispose();
        }

        /// <summary>A connect cache key: the tenant first, since one subject belongs to several tenants.</summary>
        /// <param name="TenantId">The tenant.</param>
        /// <param name="Subject">The subject, compared byte for byte as the database compares it.</param>
        private readonly record struct ConnectKey(int TenantId, string Subject);
    }
}
