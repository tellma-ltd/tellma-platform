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
    ///     there may be document-client rows. When it is full, expired entries are swept; when
    ///     none has expired, the entry used least recently among those of clients no signed-in
    ///     user has authorized makes room, so documents anyone can publish never evict an
    ///     authorized client's copy while one of theirs is left. It is per instance, like the rate
    ///     limit.
    /// </summary>
    /// <param name="options">The engine options.</param>
    public sealed class ClientMetadataCache(IOptions<TellmaIdentityOptions> options)
    {
        /// <summary>Guards <see cref="_index" /> and <see cref="_order" />.</summary>
        private readonly Lock _gate = new();

        /// <summary>The cached entries by client id.</summary>
        private readonly Dictionary<string, LinkedListNode<Entry>> _index = new(StringComparer.Ordinal);

        /// <summary>The cached entries, most recently used first.</summary>
        private readonly LinkedList<Entry> _order = new();

        /// <summary>The most entries held: <see cref="TellmaIdentityClientMetadataOptions.MaxClients" />, and at least one.</summary>
        public int Capacity => Math.Max(1, options.Value.ClientMetadata.MaxClients);

        /// <summary>How many entries are held, expired ones included until they are swept.</summary>
        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _index.Count;
                }
            }
        }

        /// <summary>Looks up a client id's cached document, which may have expired, and marks it used.</summary>
        /// <param name="clientId">The client id.</param>
        /// <param name="document">The cached document.</param>
        /// <returns>Whether one is cached.</returns>
        public bool TryGet(string clientId, [NotNullWhen(true)] out ClientMetadataDocument? document)
        {
            ArgumentNullException.ThrowIfNull(clientId);

            lock (_gate)
            {
                if (_index.TryGetValue(clientId, out LinkedListNode<Entry>? node))
                {
                    Touch(node);
                    document = node.Value.Document;
                    return true;
                }

                document = null;
                return false;
            }
        }

        /// <summary>Whether a client id's cached copy was stored as that of an authorized client.</summary>
        /// <param name="clientId">The client id.</param>
        /// <returns>True when a copy is cached and marked authorized.</returns>
        public bool IsAuthorized(string clientId)
        {
            ArgumentNullException.ThrowIfNull(clientId);

            lock (_gate)
            {
                return _index.TryGetValue(clientId, out LinkedListNode<Entry>? node) && node.Value.Authorized;
            }
        }

        /// <summary>Caches the document of a client not known to be authorized, replacing the client id's previous one.</summary>
        /// <param name="document">The validated document.</param>
        /// <param name="now">The current time, which decides which entries have expired.</param>
        public void Set(ClientMetadataDocument document, DateTimeOffset now)
        {
            Set(document, now, authorized: false);
        }

        /// <summary>Caches a document, replacing the client id's previous one, and marks it used.</summary>
        /// <param name="document">The validated document.</param>
        /// <param name="now">The current time, which decides which entries have expired.</param>
        /// <param name="authorized">Whether a signed-in user has authorized the client, which keeps its copy from eviction while another's can go.</param>
        public void Set(ClientMetadataDocument document, DateTimeOffset now, bool authorized)
        {
            ArgumentNullException.ThrowIfNull(document);

            string key = document.ClientId.OriginalString;
            lock (_gate)
            {
                if (_index.TryGetValue(key, out LinkedListNode<Entry>? node))
                {
                    node.Value = new Entry(key, document, authorized);
                    Touch(node);
                    return;
                }

                if (_index.Count >= Capacity)
                {
                    MakeRoom(now);
                }

                _index[key] = _order.AddFirst(new Entry(key, document, authorized));
            }
        }

        /// <summary>Drops a client id's cached document.</summary>
        /// <param name="clientId">The client id.</param>
        public void Remove(string clientId)
        {
            ArgumentNullException.ThrowIfNull(clientId);

            lock (_gate)
            {
                if (_index.Remove(clientId, out LinkedListNode<Entry>? node))
                {
                    _order.Remove(node);
                }
            }
        }

        /// <summary>
        ///     Sweeps the expired entries, or, when none has expired, evicts the entry used least
        ///     recently among the unauthorized ones, and only when there is none of those the
        ///     authorized one used least recently. Recency, not nearness to expiry, decides: a
        ///     publisher can choose its documents' lifetimes, but not how often real users sign in
        ///     with its clients. Called under the lock.
        /// </summary>
        private void MakeRoom(DateTimeOffset now)
        {
            List<LinkedListNode<Entry>> expired = [];
            for (LinkedListNode<Entry>? node = _order.First; node is not null; node = node.Next)
            {
                if (node.Value.Document.ExpiresAt <= now)
                {
                    expired.Add(node);
                }
            }

            foreach (LinkedListNode<Entry> node in expired)
            {
                Evict(node);
            }

            if (expired.Count > 0)
            {
                return;
            }

            // From the least recently used end: the first unauthorized entry, else the last entry.
            LinkedListNode<Entry>? victim = _order.Last;
            while (victim is not null && victim.Value.Authorized)
            {
                victim = victim.Previous;
            }

            Evict(victim ?? _order.Last!);
        }

        /// <summary>Removes an entry. Called under the lock.</summary>
        private void Evict(LinkedListNode<Entry> node)
        {
            _ = _index.Remove(node.Value.Key);
            _order.Remove(node);
        }

        /// <summary>Moves an entry to the most recently used end. Called under the lock.</summary>
        private void Touch(LinkedListNode<Entry> node)
        {
            if (!ReferenceEquals(_order.First, node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
            }
        }

        /// <summary>One cached document.</summary>
        /// <param name="Key">The client id.</param>
        /// <param name="Document">The validated document.</param>
        /// <param name="Authorized">Whether a signed-in user had authorized the client when it was cached.</param>
        private sealed record Entry(string Key, ClientMetadataDocument Document, bool Authorized);
    }
}
