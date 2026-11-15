// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;
using Tellma.Identity.Options;

namespace Tellma.Identity.Services.ClientMetadata
{
    /// <summary>
    ///     The in-memory cache of validated documents, keyed by client id (compared ordinally,
    ///     since only a canonical client id is accepted). It holds at most as many entries as
    ///     there may be document-client rows; when full, the entry nearest its expiry makes room.
    ///     It is per instance, like the rate limit.
    /// </summary>
    /// <param name="options">The engine options.</param>
    public sealed class ClientMetadataCache(IOptions<TellmaIdentityOptions> options)
    {
        /// <summary>Guards <see cref="_entries" />.</summary>
        private readonly Lock _gate = new();

        /// <summary>The cached documents by client id.</summary>
        private readonly Dictionary<string, ClientMetadataDocument> _entries = new(StringComparer.Ordinal);

        /// <summary>The most entries held: <see cref="TellmaIdentityClientMetadataOptions.MaxClients" />, and at least one.</summary>
        public int Capacity => Math.Max(1, options.Value.ClientMetadata.MaxClients);

        /// <summary>How many entries are held, expired ones included until they are swept.</summary>
        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _entries.Count;
                }
            }
        }

        /// <summary>Looks up a client id's cached document, which may have expired.</summary>
        /// <param name="clientId">The client id.</param>
        /// <param name="document">The cached document.</param>
        /// <returns>Whether one is cached.</returns>
        public bool TryGet(string clientId, [NotNullWhen(true)] out ClientMetadataDocument? document)
        {
            ArgumentNullException.ThrowIfNull(clientId);

            lock (_gate)
            {
                return _entries.TryGetValue(clientId, out document);
            }
        }

        /// <summary>Caches a document, replacing the client id's previous one.</summary>
        /// <param name="document">The validated document.</param>
        /// <param name="now">The current time, which decides which entries have expired.</param>
        public void Set(ClientMetadataDocument document, DateTimeOffset now)
        {
            ArgumentNullException.ThrowIfNull(document);

            string key = document.ClientId.OriginalString;
            lock (_gate)
            {
                if (!_entries.ContainsKey(key) && _entries.Count >= Capacity)
                {
                    MakeRoom(now);
                }

                _entries[key] = document;
            }
        }

        /// <summary>Drops a client id's cached document.</summary>
        /// <param name="clientId">The client id.</param>
        public void Remove(string clientId)
        {
            ArgumentNullException.ThrowIfNull(clientId);

            lock (_gate)
            {
                _entries.Remove(clientId);
            }
        }

        /// <summary>
        ///     Sweeps the expired entries, or, when none has expired, evicts the one nearest its
        ///     expiry, which is the cheapest to fetch again soonest. Called under the lock.
        /// </summary>
        private void MakeRoom(DateTimeOffset now)
        {
            List<string> expired = [.. _entries.Where(entry => entry.Value.ExpiresAt <= now).Select(static entry => entry.Key)];
            foreach (string key in expired)
            {
                _entries.Remove(key);
            }

            if (expired.Count == 0)
            {
                string nearest = _entries.MinBy(static entry => entry.Value.ExpiresAt).Key;
                _entries.Remove(nearest);
            }
        }
    }
}
