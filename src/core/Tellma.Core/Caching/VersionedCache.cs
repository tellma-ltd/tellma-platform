// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Caching
{
    // The base is spec 0013 §3: one private bounded MemoryCache per kind, entries validated by a
    // version tag, single-flight loads, and the tellma.cache.* instruments of §11.
    /// <summary>
    ///     The process-wide store of one cache kind: a private, bounded memory cache whose entries
    ///     carry the version tag they are valid for, loaded once per key however many callers miss
    ///     together. A singleton that holds no scope: the caller passes its own database handle, and a
    ///     scoped façade passes the current tag.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A lookup hits when the entry's tag equals the current tag; <see cref="VersionTag.None" />,
    ///         a tag the snapshot does not know, never hits. There is no age limit: the tag is the only
    ///         invalidation channel, and the runtime's compaction (priority, then least recently used)
    ///         bounds the store in the kind's own size unit.
    ///     </para>
    ///     <para>
    ///         Values are shared by reference with every caller and must be immutable.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TKey">The key, which begins with the tenant id.</typeparam>
    /// <typeparam name="TValue">The cached value.</typeparam>
    public abstract class VersionedCache<TKey, TValue> : IDisposable
        where TKey : notnull, ITenantCacheKey
        where TValue : class
    {
        /// <summary>The <c>cache.reason</c> of an entry the runtime compacted, or refused for want of room.</summary>
        internal const string CapacityReason = "capacity";

        /// <summary>The <c>cache.reason</c> of an entry a newer value replaced.</summary>
        internal const string ReplacedReason = "replaced";

        /// <summary>The <c>cache.reason</c> of an entry <see cref="Remove" /> dropped.</summary>
        internal const string RemovedReason = "removed";

        /// <summary>The <c>cache.reason</c> of an entry an aged <see cref="Set" /> replaced.</summary>
        internal const string AgeReason = "age";

        /// <summary>The eviction callback, shared by every entry; its state is the cache.</summary>
        private static readonly PostEvictionDelegate OnEvicted = static (_, value, reason, state) =>
            ((VersionedCache<TKey, TValue>)state!).Evicted(value, reason);

        /// <summary>The entries.</summary>
        private readonly MemoryCache _cache;

        /// <summary>The loads in flight, by key and the tag their callers asked for; each removes itself when it ends.</summary>
        private readonly ConcurrentDictionary<(TKey Key, VersionTag Tag), Flight> _flights = new();

        /// <summary>The instruments shared by every cache on the meter.</summary>
        private readonly CacheInstruments _instruments;

        /// <summary>What this cache reports through the shared gauges.</summary>
        private readonly CacheGauges _gauges;

        /// <summary>The <c>cache.kind</c> tag, built once.</summary>
        private readonly KeyValuePair<string, object?> _kindTag;

        /// <summary>Whether the cache was disposed.</summary>
        private bool _disposed;

        /// <summary>Creates the store of a kind.</summary>
        /// <param name="kind">The kind, one of <see cref="CacheKinds" />.</param>
        /// <param name="sizeLimit">The most the store holds, in the kind's unit; at least 1.</param>
        /// <param name="meterFactory">The host's meter factory, which owns the meter's lifetime.</param>
        /// <exception cref="ArgumentException">The kind is not one of <see cref="CacheKinds" />.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The size limit is below 1.</exception>
        /// <exception cref="InvalidOperationException">A live store of the kind already reports on the factory's meter.</exception>
        protected VersionedCache(string kind, long sizeLimit, IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(kind);
            ArgumentOutOfRangeException.ThrowIfLessThan(sizeLimit, 1);
            ArgumentNullException.ThrowIfNull(meterFactory);
            if (!CacheKinds.All.Contains(kind))
            {
                throw new ArgumentException(
                    $"The cache kind '{kind}' is not one of {string.Join(", ", CacheKinds.All.Order(StringComparer.Ordinal))}; the instruments' cache.kind tag is a closed set.",
                    nameof(kind));
            }

            Kind = kind;
            SizeLimit = sizeLimit;
            _kindTag = new KeyValuePair<string, object?>(CacheTelemetryNames.KindTag, kind);

            // --- A private cache, never the container's: its own size unit, and statistics for the size gauge ---
            _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = sizeLimit, TrackStatistics = true });

            // --- The shared instruments; the gauges read this cache until it is disposed ---
            _instruments = CacheInstruments.For(meterFactory.Create(CacheTelemetryNames.MeterName));
            _gauges = new CacheGauges(
                () => _cache.Count,
                () => _cache.GetCurrentStatistics()?.CurrentEstimatedSize ?? 0,
                sizeLimit);
            try
            {
                _instruments.Register(kind, _gauges);
            }
            catch
            {
                _cache.Dispose();
                throw;
            }
        }

        /// <summary>The kind.</summary>
        public string Kind { get; }

        /// <summary>The most the store holds, in the kind's unit.</summary>
        public long SizeLimit { get; }

        /// <summary>
        ///     The entry when its tag is <paramref name="currentTag" /> (<see cref="CacheOutcome.Hit" />);
        ///     otherwise loads through <see cref="LoadAsync" /> on <paramref name="database" />, stores
        ///     the value under the tag the load's prelude returned, and reports
        ///     <see cref="CacheOutcome.Miss" /> (no entry) or <see cref="CacheOutcome.Stale" /> (an entry
        ///     with another tag). A load whose tag is <see cref="VersionTag.None" /> stores nothing and
        ///     reports <see cref="CacheOutcome.Uncached" />; an oversized load stores nothing and
        ///     reports <see cref="CacheOutcome.Oversized" /> with no value.
        /// </summary>
        /// <remarks>
        ///     Callers that miss one key under one current tag together share one load. The load runs
        ///     on the handle and token of the caller that started it; another caller cancelling its
        ///     own wait ends only that wait, never the load. A load cancelled under its starter, by
        ///     the token passed here or by any token its batch links (the scope's, say), stores
        ///     nothing, and a caller still waiting on it starts a load of its own. A failed load is
        ///     forgotten before its exception reaches the callers that shared it, so the next caller
        ///     loads afresh.
        /// </remarks>
        /// <param name="key">The key.</param>
        /// <param name="currentTag">The tag the caller's snapshot holds; <see cref="VersionTag.None" /> always loads.</param>
        /// <param name="database">The caller's scoped handle, on which the load creates its batch.</param>
        /// <param name="cancellationToken">Cancels the caller's wait, and the load when the caller started it.</param>
        /// <returns>The value, the tag it is valid for, and the outcome.</returns>
        public async Task<CacheResult<TValue>> GetAsync(TKey key, VersionTag currentTag, ITenantDatabase database, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(currentTag);
            ArgumentNullException.ThrowIfNull(database);
            ObjectDisposedException.ThrowIf(_disposed, this);

            // --- A current entry answers ---
            Entry? entry = Find(key);
            if (entry is not null && Matches(entry.Tag, currentTag))
            {
                RecordRequest(CacheOutcome.Hit);
                return new CacheResult<TValue>(entry.Value, entry.Tag, CacheOutcome.Hit);
            }

            // --- Otherwise one load per key and tag, whatever the number of callers ---
            CacheOutcome missed = entry is null ? CacheOutcome.Miss : CacheOutcome.Stale;
            CacheLoad<TValue> load = await LoadOnceAsync(key, currentTag, database, cancellationToken).ConfigureAwait(false);
            CacheOutcome outcome = load.IsOversized
                ? CacheOutcome.Oversized
                : load.Tag == VersionTag.None ? CacheOutcome.Uncached : missed;
            RecordRequest(outcome);
            return new CacheResult<TValue>(load.Value, load.Tag, outcome);
        }

        /// <summary>The entry when present and its tag is <paramref name="currentTag" />; otherwise null. Never loads.</summary>
        /// <param name="key">The key.</param>
        /// <param name="currentTag">The tag the caller's snapshot holds; <see cref="VersionTag.None" /> never matches.</param>
        /// <returns>The value, or null.</returns>
        public TValue? Peek(TKey key, VersionTag currentTag)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(currentTag);
            ObjectDisposedException.ThrowIf(_disposed, this);

            CacheOutcome outcome = Lookup(key, currentTag, out TValue? value);
            RecordRequest(outcome);
            return value;
        }

        /// <summary>
        ///     The entry when present and <paramref name="isCurrent" /> accepts it; otherwise null. Never
        ///     loads. For a kind whose validity is not one tag, such as permissions, stored under
        ///     <see cref="VersionTag.None" /> and valid while two tags match.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="isCurrent">Whether the stored value is current.</param>
        /// <returns>The value, or null.</returns>
        public TValue? Peek(TKey key, Func<TValue, bool> isCurrent)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(isCurrent);
            ObjectDisposedException.ThrowIf(_disposed, this);

            Entry? entry = Find(key);
            CacheOutcome outcome = entry is null ? CacheOutcome.Miss : isCurrent(entry.Value) ? CacheOutcome.Hit : CacheOutcome.Stale;
            RecordRequest(outcome);
            return outcome == CacheOutcome.Hit ? entry!.Value : null;
        }

        /// <summary>
        ///     Stores a value under the tag it is valid for, replacing the entry of the key; the
        ///     replaced entry is metered <c>replaced</c>, or <c>age</c> when <paramref name="aged" />.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value, immutable from now on.</param>
        /// <param name="tag">The tag it is valid for.</param>
        /// <param name="aged">Whether the replaced entry is dropped for its age rather than replaced by a newer value.</param>
        public void Set(TKey key, TValue value, VersionTag tag, bool aged = false)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(tag);
            ObjectDisposedException.ThrowIf(_disposed, this);
            Store(key, value, tag, aged ? AgeReason : ReplacedReason);
        }

        /// <summary>Drops the entry of a key when present; metered <c>removed</c>.</summary>
        /// <param name="key">The key.</param>
        public void Remove(TKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            ObjectDisposedException.ThrowIf(_disposed, this);
            _cache.Remove(key);
        }

        /// <summary>Releases the entries and stops the gauges reporting this store.</summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Releases the entries and stops the gauges reporting this store.</summary>
        /// <param name="disposing">Whether called from <see cref="Dispose()" />.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (disposing)
            {
                _instruments.Unregister(Kind, _gauges);
                _cache.Dispose();
            }
        }

        /// <summary>
        ///     Loads the value of a key: exactly one <see cref="BatchPurpose.Read" /> batch created on
        ///     <paramref name="database" />, returning the value with the tag that batch's prelude
        ///     returned. The base loads nothing: a kind fed by <see cref="Set" /> never calls
        ///     <see cref="GetAsync" />.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="database">The handle the batch is created on.</param>
        /// <param name="cancellationToken">Cancels the load.</param>
        /// <returns>The load.</returns>
        /// <exception cref="NotSupportedException">The kind does not load.</exception>
        protected virtual Task<CacheLoad<TValue>> LoadAsync(TKey key, ITenantDatabase database, CancellationToken cancellationToken)
        {
            throw new NotSupportedException($"The '{Kind}' cache does not load on a miss; it is fed through Set and read through Peek.");
        }

        /// <summary>The size of a value in the kind's unit; one entry unless the kind counts otherwise.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The size, at least 1.</returns>
        protected virtual long SizeOf(TValue value)
        {
            return 1;
        }

        /// <summary>
        ///     Looks a key up under the current tag without counting the request: for a kind that
        ///     answers a lookup outside <see cref="GetAsync" /> and counts it with the outcome it
        ///     settles to (<see cref="RecordRequest" />). Never loads.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="currentTag">The current tag; <see cref="VersionTag.None" /> never matches.</param>
        /// <param name="value">The value on a hit; otherwise null.</param>
        /// <returns><see cref="CacheOutcome.Hit" />, <see cref="CacheOutcome.Miss" /> (no entry) or <see cref="CacheOutcome.Stale" /> (an entry with another tag).</returns>
        protected CacheOutcome Lookup(TKey key, VersionTag currentTag, out TValue? value)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(currentTag);
            ObjectDisposedException.ThrowIf(_disposed, this);

            Entry? entry = Find(key);
            CacheOutcome outcome = entry is null ? CacheOutcome.Miss : Matches(entry.Tag, currentTag) ? CacheOutcome.Hit : CacheOutcome.Stale;
            value = outcome == CacheOutcome.Hit ? entry!.Value : null;
            return outcome;
        }

        /// <summary>Counts one lookup with its outcome, for a lookup the kind answers outside <see cref="GetAsync" />.</summary>
        /// <param name="outcome">The outcome.</param>
        protected void RecordRequest(CacheOutcome outcome)
        {
            _instruments.Requests.Add(1, _kindTag, new KeyValuePair<string, object?>(CacheTelemetryNames.OutcomeTag, OutcomeName(outcome)));
        }

        /// <summary>Whether an entry's tag is the current one; an unknown current tag matches nothing.</summary>
        /// <param name="stored">The entry's tag.</param>
        /// <param name="current">The current tag.</param>
        /// <returns>True on a match.</returns>
        private static bool Matches(VersionTag stored, VersionTag current)
        {
            return current != VersionTag.None && stored == current;
        }

        /// <summary>The <c>cache.outcome</c> value of an outcome.</summary>
        /// <param name="outcome">The outcome.</param>
        /// <returns>The value.</returns>
        private static string OutcomeName(CacheOutcome outcome)
        {
            return outcome switch
            {
                CacheOutcome.Hit => "hit",
                CacheOutcome.Miss => "miss",
                CacheOutcome.Stale => "stale",
                CacheOutcome.Oversized => "oversized",
                CacheOutcome.Uncached => "uncached",
                _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a cache outcome."),
            };
        }

        /// <summary>The entry of a key, or null.</summary>
        /// <param name="key">The key.</param>
        /// <returns>The entry.</returns>
        private Entry? Find(TKey key)
        {
            return _cache.TryGetValue(key, out object? found) ? (Entry?)found : null;
        }

        /// <summary>
        ///     Joins the load in flight for a key and tag, or starts one. The starter runs it, stores
        ///     what it loaded, removes the flight, and only then releases the callers that joined.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="currentTag">The tag the caller asked for.</param>
        /// <param name="database">The caller's handle.</param>
        /// <param name="cancellationToken">The caller's token.</param>
        /// <returns>The load.</returns>
        private async Task<CacheLoad<TValue>> LoadOnceAsync(TKey key, VersionTag currentTag, ITenantDatabase database, CancellationToken cancellationToken)
        {
            (TKey, VersionTag) flightKey = (key, currentTag);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Flight mine = new();
                Flight flight = _flights.GetOrAdd(flightKey, mine);

                // --- A joiner waits on its own token; a load its starter cancelled is started afresh ---
                if (!ReferenceEquals(flight, mine))
                {
                    try
                    {
                        return await flight.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && flight.Completion.Task.IsCanceled)
                    {
                        continue;
                    }
                }

                // --- The starter loads on its own handle and token ---
                try
                {
                    long started = Stopwatch.GetTimestamp();
                    CacheLoad<TValue> load = await LoadAsync(key, database, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidOperationException($"The '{Kind}' cache's loader returned no load.");
                    _instruments.LoadDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, _kindTag);

                    // An oversized load, or one whose prelude found no tag, is served and never stored.
                    if (load.Value is TValue value && load.Tag != VersionTag.None)
                    {
                        Store(key, value, load.Tag, ReplacedReason);
                    }

                    _ = _flights.TryRemove(new KeyValuePair<(TKey, VersionTag), Flight>(flightKey, mine));
                    _ = mine.Completion.TrySetResult(load);
                    return load;
                }
                catch (OperationCanceledException exception)
                {
                    // The starter's load was cancelled, by its own token or by one its batch linked
                    // (the starter's scope): no caller that joined was, so each still waiting starts
                    // a load of its own rather than failing with the starter's cancellation.
                    _ = _flights.TryRemove(new KeyValuePair<(TKey, VersionTag), Flight>(flightKey, mine));
                    _ = mine.Completion.TrySetCanceled(exception.CancellationToken);
                    throw;
                }
                catch (Exception exception)
                {
                    // Forgotten before any caller sees the failure, so the next caller loads afresh.
                    _ = _flights.TryRemove(new KeyValuePair<(TKey, VersionTag), Flight>(flightKey, mine));
                    _ = mine.Completion.TrySetException(exception);

                    // The starter rethrows the exception itself; reading it marks the shared task observed.
                    _ = mine.Completion.Task.Exception;
                    throw;
                }
            }
        }

        /// <summary>Stores a value, marking the entry it replaces with the reason it is metered under.</summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <param name="tag">The tag it is valid for.</param>
        /// <param name="displacement">The reason a replaced entry is metered under.</param>
        private void Store(TKey key, TValue value, VersionTag tag, string displacement)
        {
            long size = SizeOf(value);
            if (size < 1)
            {
                throw new InvalidOperationException($"The '{Kind}' cache sized a value at {size}; every value takes at least one unit.");
            }

            // --- The replaced entry first: removed, then the new one added, so the size check sees only the new one ---
            if (Find(key) is Entry replaced)
            {
                replaced.Displacement = displacement;
                _cache.Remove(key);
            }

            // --- The new entry; one the runtime refuses for want of room is metered as a capacity eviction ---
            MemoryCacheEntryOptions options = new() { Size = size };
            options.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration { EvictionCallback = OnEvicted, State = this });
            _ = _cache.Set(key, new Entry(tag, value), options);
        }

        /// <summary>Meters an entry the cache dropped, under the reason it was dropped for.</summary>
        /// <param name="value">The entry.</param>
        /// <param name="reason">Why the runtime dropped it.</param>
        private void Evicted(object? value, EvictionReason reason)
        {
            string? name = reason switch
            {
                EvictionReason.Capacity => CapacityReason,
                EvictionReason.Removed => (value as Entry)?.Displacement ?? RemovedReason,
                EvictionReason.Replaced => ReplacedReason,

                // No entry expires: the tag is the only invalidation channel.
                EvictionReason.None or EvictionReason.Expired or EvictionReason.TokenExpired => null,
                _ => null,
            };

            if (name is not null)
            {
                _instruments.Evictions.Add(1, _kindTag, new KeyValuePair<string, object?>(CacheTelemetryNames.ReasonTag, name));
            }
        }

        /// <summary>One stored value and the tag it is valid for.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="value">The value.</param>
        private sealed class Entry(VersionTag tag, TValue value)
        {
            /// <summary>The tag.</summary>
            public VersionTag Tag { get; } = tag;

            /// <summary>The value.</summary>
            public TValue Value { get; } = value;

            /// <summary>The reason the entry is metered under when a newer value displaces it; null when it is removed.</summary>
            public string? Displacement
            {
                get => Volatile.Read(ref field);
                set => Volatile.Write(ref field, value);
            }
        }

        /// <summary>One load in flight, completed by its starter for the callers that joined it.</summary>
        private sealed class Flight
        {
            /// <summary>The load's completion; continuations run asynchronously, never on the starter's stack.</summary>
            public TaskCompletionSource<CacheLoad<TValue>> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
