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
        ///     Re-fetching the document of a client a signed-in user has authorized (its row holds
        ///     an authorization) because the engine's copy is missing or has expired, at most once
        ///     per client id per window unless the refresh succeeds. Publishing documents alone
        ///     never puts a client id here: an anonymous caller cannot produce an authorization.
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
    ///         <item>
    ///             Per client id, at most one failed draw on the refresh budget per minute; a
    ///             refresh that succeeds gives its claim back.
    ///         </item>
    ///         <item>
    ///             Per budget, at most <see cref="MaxInFlightPerHost" /> resolutions in flight for
    ///             one host, and at most
    ///             <see cref="TellmaIdentityClientMetadataOptions.MaxConcurrentFetches" /> across
    ///             all hosts, so one slow host cannot hold more than its share of the slots. A
    ///             probe that finds no free slot is refused at once; a refresh waits for one up to
    ///             the fetch timeout.
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

        /// <summary>How many resolutions one host may have in flight in each budget at once.</summary>
        public const int MaxInFlightPerHost = 2;

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

        /// <summary>The refresh budget's per-host gates, held only while a resolution holds or awaits one.</summary>
        private readonly Dictionary<string, HostGate> _refreshGates = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The probe budget's per-host gates, held only while a resolution holds or awaits one.</summary>
        private readonly Dictionary<string, HostGate> _probeGates = new(StringComparer.OrdinalIgnoreCase);

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
        ///     Gives back a client id's refresh claim after the refresh succeeded: the claim exists
        ///     to stop a refresh that keeps failing, and a client evicted from the cache again
        ///     within the window must still refresh on the protected budget.
        /// </summary>
        /// <param name="clientId">The client id.</param>
        public void ReleaseRefresh(string clientId)
        {
            ArgumentException.ThrowIfNullOrEmpty(clientId);

            lock (_gate)
            {
                _refreshClaims.Remove(clientId);
            }
        }

        /// <summary>
        ///     Takes a resolution slot in a budget for a host: first one of the host's own slots,
        ///     then one of the budget's shared ones. A probe takes them only when both are free
        ///     now; a refresh waits for both, together within the fetch timeout. A taken slot is
        ///     returned with <see cref="Exit" />.
        /// </summary>
        /// <param name="budget">The budget.</param>
        /// <param name="host">The host the resolution fetches from.</param>
        /// <returns>True when a slot was taken.</returns>
        public async Task<bool> TryEnterAsync(ClientMetadataFetchBudget budget, string host)
        {
            ArgumentException.ThrowIfNullOrEmpty(host);

            HostGate gate = JoinGate(budget, host);
            bool entered = false;
            try
            {
                if (budget is ClientMetadataFetchBudget.Probe)
                {
                    if (gate.Slots.Wait(0))
                    {
                        entered = _probeSlots.Wait(0);
                        if (!entered)
                        {
                            _ = gate.Slots.Release();
                        }
                    }

                    return entered;
                }

                // The waits run on the engine's clock, like the fetch timeout they are bounded by.
                using CancellationTokenSource timeout = new(_options.Value.ClientMetadata.Timeout, _timeProvider);
                bool hostEntered = false;
                try
                {
                    await gate.Slots.WaitAsync(timeout.Token);
                    hostEntered = true;
                    await _refreshSlots.WaitAsync(timeout.Token);
                    entered = true;
                }
                catch (OperationCanceledException)
                {
                    if (hostEntered)
                    {
                        _ = gate.Slots.Release();
                    }
                }

                return entered;
            }
            finally
            {
                // A caller that holds no slot no longer needs the host's gate.
                if (!entered)
                {
                    LeaveGate(budget, host, gate);
                }
            }
        }

        /// <summary>Returns a slot taken with <see cref="TryEnterAsync" />.</summary>
        /// <param name="budget">The budget the slot was taken from.</param>
        /// <param name="host">The host it was taken for.</param>
        public void Exit(ClientMetadataFetchBudget budget, string host)
        {
            ArgumentException.ThrowIfNullOrEmpty(host);

            _ = (budget is ClientMetadataFetchBudget.Probe ? _probeSlots : _refreshSlots).Release();
            HostGate gate;
            lock (_gate)
            {
                gate = GatesOf(budget)[host];
            }

            _ = gate.Slots.Release();
            LeaveGate(budget, host, gate);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _refreshSlots.Dispose();
            _probeSlots.Dispose();
            lock (_gate)
            {
                foreach (HostGate gate in _refreshGates.Values.Concat(_probeGates.Values))
                {
                    gate.Slots.Dispose();
                }
            }
        }

        /// <summary>A budget's per-host gates. Called under the lock.</summary>
        private Dictionary<string, HostGate> GatesOf(ClientMetadataFetchBudget budget)
        {
            return budget is ClientMetadataFetchBudget.Probe ? _probeGates : _refreshGates;
        }

        /// <summary>Takes a reference on a host's gate in a budget, creating the gate if none is held.</summary>
        private HostGate JoinGate(ClientMetadataFetchBudget budget, string host)
        {
            lock (_gate)
            {
                Dictionary<string, HostGate> gates = GatesOf(budget);
                if (!gates.TryGetValue(host, out HostGate? gate))
                {
                    gate = new HostGate();
                    gates[host] = gate;
                }

                gate.References++;
                return gate;
            }
        }

        /// <summary>Drops a reference on a host's gate, forgetting the gate when no one holds or awaits it.</summary>
        private void LeaveGate(ClientMetadataFetchBudget budget, string host, HostGate gate)
        {
            lock (_gate)
            {
                if (--gate.References == 0)
                {
                    _ = GatesOf(budget).Remove(host);
                    gate.Slots.Dispose();
                }
            }
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

            /// <summary>Forgets a key, if it is held.</summary>
            /// <param name="key">The key.</param>
            public void Remove(string key)
            {
                if (_index.Remove(key, out LinkedListNode<(string Key, TValue Value)>? node))
                {
                    _order.Remove(node);
                }
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

        /// <summary>
        ///     One host's slots in one budget, with how many resolutions hold or await them; the
        ///     limiter forgets the gate when that count reaches zero, so the gates in memory never
        ///     outnumber the resolutions in flight. Its count is read and written under the
        ///     limiter's lock.
        /// </summary>
        private sealed class HostGate
        {
            /// <summary>The host's slots.</summary>
            public SemaphoreSlim Slots { get; } = new(MaxInFlightPerHost, MaxInFlightPerHost);

            /// <summary>How many resolutions hold or await a slot.</summary>
            public int References { get; set; }
        }
    }
}
