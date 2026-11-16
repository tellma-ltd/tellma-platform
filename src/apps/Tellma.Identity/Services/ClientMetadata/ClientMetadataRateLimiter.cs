// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Tellma.Identity.Options;

namespace Tellma.Identity.Services.ClientMetadata
{
    /// <summary>
    ///     Which of a host's two fetch budgets a fetch draws on. Anyone can name a client id, so
    ///     the fetches anyone can cause are kept apart from the ones that keep the engine's own
    ///     clients working: spending one budget never spends the other.
    /// </summary>
    public enum ClientMetadataFetchBudget
    {
        /// <summary>
        ///     Re-fetching the document of a client the engine already holds (a cached copy or a
        ///     stored row) because its copy is missing or has expired, at most once per client id
        ///     per window. Only a host that publishes valid documents can have such clients.
        /// </summary>
        Refresh,

        /// <summary>
        ///     Every other fetch: a client id seen for the first time, an early re-fetch for a
        ///     redirect URI the cached copy does not list, and a refresh repeated inside its window.
        /// </summary>
        Probe,
    }

    /// <summary>
    ///     The fetch limits, all per instance.
    ///     <list type="bullet">
    ///         <item>
    ///             Per host and budget, at most
    ///             <see cref="TellmaIdentityClientMetadataOptions.FetchesPerHostPerMinute" />
    ///             fetches in any sliding minute, documents and key sets together.
    ///         </item>
    ///         <item>Per client id, at most one draw on the refresh budget per minute.</item>
    ///         <item>
    ///             Per budget, at most
    ///             <see cref="TellmaIdentityClientMetadataOptions.MaxConcurrentFetches" />
    ///             resolutions in flight across all hosts. A probe that finds no free slot is
    ///             refused at once; a refresh waits for one up to the fetch timeout.
    ///         </item>
    ///     </list>
    ///     The tables are bounded: when one is full, the entry used least recently is forgotten,
    ///     which only ever gives that host or client id a fresh budget, never a refusal.
    /// </summary>
    public sealed class ClientMetadataRateLimiter : IDisposable
    {
        /// <summary>The window the limits count over.</summary>
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

        /// <summary>How many hosts each budget's table tracks, and how many client ids the refresh claims track.</summary>
        public const int MaxTracked = 10_000;

        /// <summary>The engine options.</summary>
        private readonly IOptions<TellmaIdentityOptions> _options;

        /// <summary>The clock the windows slide on and slot waits time out on.</summary>
        private readonly TimeProvider _timeProvider;

        /// <summary>Guards the tables.</summary>
        private readonly Lock _gate = new();

        /// <summary>Each host's refresh fetch times inside the window, oldest first.</summary>
        private readonly LruTable<Queue<DateTimeOffset>> _refreshHosts = new(MaxTracked, StringComparer.OrdinalIgnoreCase);

        /// <summary>Each host's probe fetch times inside the window, oldest first.</summary>
        private readonly LruTable<Queue<DateTimeOffset>> _probeHosts = new(MaxTracked, StringComparer.OrdinalIgnoreCase);

        /// <summary>When each client id last drew on the refresh budget.</summary>
        private readonly LruTable<DateTimeOffset> _refreshClaims = new(MaxTracked, StringComparer.Ordinal);

        /// <summary>The refresh budget's resolution slots.</summary>
        private readonly SemaphoreSlim _refreshSlots;

        /// <summary>The probe budget's resolution slots.</summary>
        private readonly SemaphoreSlim _probeSlots;

        /// <summary>Creates the limits from the options.</summary>
        /// <param name="options">The engine options.</param>
        /// <param name="timeProvider">The clock the windows slide on.</param>
        public ClientMetadataRateLimiter(IOptions<TellmaIdentityOptions> options, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(timeProvider);

            _options = options;
            _timeProvider = timeProvider;
            int slots = Math.Max(1, options.Value.ClientMetadata.MaxConcurrentFetches);
            _refreshSlots = new SemaphoreSlim(slots, slots);
            _probeSlots = new SemaphoreSlim(slots, slots);
        }

        /// <summary>How many hosts a budget's table tracks now.</summary>
        /// <param name="budget">The budget.</param>
        /// <returns>The count.</returns>
        internal int TrackedHosts(ClientMetadataFetchBudget budget)
        {
            lock (_gate)
            {
                return HostsOf(budget).Count;
            }
        }

        /// <summary>Takes one fetch from a host's quota in a budget.</summary>
        /// <param name="budget">The budget the fetch draws on.</param>
        /// <param name="host">The host the fetch is sent to.</param>
        /// <returns>True when the fetch may be sent; false when the host's quota for the window is spent.</returns>
        public bool TryAcquire(ClientMetadataFetchBudget budget, string host)
        {
            ArgumentException.ThrowIfNullOrEmpty(host);

            int limit = _options.Value.ClientMetadata.FetchesPerHostPerMinute;
            DateTimeOffset now = _timeProvider.GetUtcNow();
            DateTimeOffset windowStart = now - Window;

            lock (_gate)
            {
                Queue<DateTimeOffset> fetches = HostsOf(budget).GetOrAdd(host, static () => new Queue<DateTimeOffset>());
                while (fetches.Count > 0 && fetches.Peek() <= windowStart)
                {
                    fetches.Dequeue();
                }

                if (fetches.Count >= limit)
                {
                    return false;
                }

                fetches.Enqueue(now);
                return true;
            }
        }

        /// <summary>
        ///     Claims a client id's one refresh for the window. A client id whose refresh keeps
        ///     failing would otherwise let anyone who names it spend its host's refresh budget.
        /// </summary>
        /// <param name="clientId">The client id.</param>
        /// <returns>True when the id has not drawn on the refresh budget inside the window, which it now has.</returns>
        public bool TryClaimRefresh(string clientId)
        {
            ArgumentException.ThrowIfNullOrEmpty(clientId);

            DateTimeOffset now = _timeProvider.GetUtcNow();
            lock (_gate)
            {
                if (_refreshClaims.TryGet(clientId, out DateTimeOffset claimed) && claimed > now - Window)
                {
                    return false;
                }

                _refreshClaims.Set(clientId, now);
                return true;
            }
        }

        /// <summary>
        ///     Takes a resolution slot in a budget: a probe only when one is free now, a refresh
        ///     within the fetch timeout. A taken slot is returned with <see cref="Exit" />.
        /// </summary>
        /// <param name="budget">The budget.</param>
        /// <returns>True when a slot was taken.</returns>
        public async Task<bool> TryEnterAsync(ClientMetadataFetchBudget budget)
        {
            if (budget is ClientMetadataFetchBudget.Probe)
            {
                return _probeSlots.Wait(0);
            }

            // The wait runs on the engine's clock, like the fetch timeout it is bounded by.
            using CancellationTokenSource timeout = new(_options.Value.ClientMetadata.Timeout, _timeProvider);
            try
            {
                await _refreshSlots.WaitAsync(timeout.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        /// <summary>Returns a slot taken with <see cref="TryEnterAsync" />.</summary>
        /// <param name="budget">The budget the slot was taken from.</param>
        public void Exit(ClientMetadataFetchBudget budget)
        {
            _ = (budget is ClientMetadataFetchBudget.Probe ? _probeSlots : _refreshSlots).Release();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _refreshSlots.Dispose();
            _probeSlots.Dispose();
        }

        /// <summary>A budget's host table. Called under the lock.</summary>
        private LruTable<Queue<DateTimeOffset>> HostsOf(ClientMetadataFetchBudget budget)
        {
            return budget is ClientMetadataFetchBudget.Probe ? _probeHosts : _refreshHosts;
        }

        /// <summary>
        ///     A bounded map that forgets the entry used least recently when it is full. Not
        ///     thread-safe; the limiter calls it under its lock.
        /// </summary>
        /// <typeparam name="TValue">The value held per key.</typeparam>
        /// <param name="capacity">The most entries held.</param>
        /// <param name="comparer">How keys compare.</param>
        private sealed class LruTable<TValue>(int capacity, StringComparer comparer)
        {
            /// <summary>The entries by key.</summary>
            private readonly Dictionary<string, LinkedListNode<(string Key, TValue Value)>> _index = new(comparer);

            /// <summary>The entries, most recently used first.</summary>
            private readonly LinkedList<(string Key, TValue Value)> _order = new();

            /// <summary>How many entries are held.</summary>
            public int Count => _index.Count;

            /// <summary>Looks a key up, marking it used.</summary>
            /// <param name="key">The key.</param>
            /// <param name="value">Its value.</param>
            /// <returns>Whether the key is held.</returns>
            public bool TryGet(string key, out TValue value)
            {
                if (_index.TryGetValue(key, out LinkedListNode<(string Key, TValue Value)>? node))
                {
                    Touch(node);
                    value = node.Value.Value;
                    return true;
                }

                value = default!;
                return false;
            }

            /// <summary>Returns a key's value, adding a new one when the key is not held.</summary>
            /// <param name="key">The key.</param>
            /// <param name="create">Creates the new value.</param>
            /// <returns>The value.</returns>
            public TValue GetOrAdd(string key, Func<TValue> create)
            {
                if (!TryGet(key, out TValue value))
                {
                    value = create();
                    Set(key, value);
                }

                return value;
            }

            /// <summary>Sets a key's value and marks it used, forgetting the least recently used key when full.</summary>
            /// <param name="key">The key.</param>
            /// <param name="value">The value.</param>
            public void Set(string key, TValue value)
            {
                if (_index.TryGetValue(key, out LinkedListNode<(string Key, TValue Value)>? node))
                {
                    node.Value = (key, value);
                    Touch(node);
                    return;
                }

                if (_index.Count >= capacity && _order.Last is { } oldest)
                {
                    _order.RemoveLast();
                    _index.Remove(oldest.Value.Key);
                }

                _index[key] = _order.AddFirst((key, value));
            }

            /// <summary>Moves an entry to the front.</summary>
            private void Touch(LinkedListNode<(string Key, TValue Value)> node)
            {
                if (!ReferenceEquals(_order.First, node))
                {
                    _order.Remove(node);
                    _order.AddFirst(node);
                }
            }
        }
    }
}
