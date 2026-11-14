// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Caching;
using Tellma.Core.Queryex;

namespace Tellma.Core.Tests.Caching
{
    /// <summary>
    ///     The versioned cache base: hits by tag and nothing else, one load per key under concurrent
    ///     callers, failed and cancelled loads forgotten, values stored under the load's own tag or not
    ///     at all, bounds and eviction reasons, tenant-first keys, and every cache instrument.
    /// </summary>
    public sealed class VersionedCacheTests : IDisposable
    {
        /// <summary>How long any wait of these tests may take before the test fails instead of hanging.</summary>
        private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

        private static readonly VersionTag First = new(new Guid("00000001-0000-0000-0000-000000000000"));

        private static readonly VersionTag Second = new(new Guid("00000002-0000-0000-0000-000000000000"));

        private static readonly TenantUserCacheKey Key = new(1, 7);

        /// <summary>The meter factory the caches report through.</summary>
        private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().BuildServiceProvider();

        /// <inheritdoc />
        public void Dispose()
        {
            _services.Dispose();
        }

        // ===================== Hits, misses and stale entries =====================

        [Fact]
        public async Task An_entry_hits_only_under_its_own_tag_and_another_tag_reloads_it_as_stale()
        {
            using TestCache cache = Cache();
            using MetricCollector<long> requests = Collector(CacheTelemetryNames.Requests);
            cache.Loads = (_, _, _) => Task.FromResult(CacheLoad.Loaded(new Box("loaded"), Second));
            Box stored = new("stored");
            cache.Set(Key, stored, First);

            // --- The entry's own tag hits without loading ---
            CacheResult<Box> hit = await cache.GetAsync(Key, First, NoDatabase.Instance, Token);
            Assert.Equal((stored, First, CacheOutcome.Hit, 0), (hit.Value, hit.Tag, hit.Outcome, cache.LoadCount));

            // --- A mismatch is a miss of the stale kind: it loads and stores under the load's tag ---
            CacheResult<Box> stale = await cache.GetAsync(Key, Second, NoDatabase.Instance, Token);
            Assert.Equal(("loaded", Second, CacheOutcome.Stale, 1), (stale.Value!.Name, stale.Tag, stale.Outcome, cache.LoadCount));
            Assert.Same(stale.Value, cache.Peek(Key, Second));

            Assert.Equal(["hit", "stale", "hit"], Outcomes(requests));
        }

        [Fact]
        public async Task No_entry_is_a_miss_and_an_unknown_tag_always_loads()
        {
            using TestCache cache = Cache();
            cache.Loads = (_, _, _) => Task.FromResult(CacheLoad.Loaded(new Box("loaded"), First));

            CacheResult<Box> miss = await cache.GetAsync(Key, First, NoDatabase.Instance, Token);
            Assert.Equal((CacheOutcome.Miss, 1), (miss.Outcome, cache.LoadCount));

            // The stored entry is never matched by VersionTag.None, a tag the caller's snapshot does not know.
            CacheResult<Box> unknown = await cache.GetAsync(Key, VersionTag.None, NoDatabase.Instance, Token);
            Assert.Equal((CacheOutcome.Stale, First, 2), (unknown.Outcome, unknown.Tag, cache.LoadCount));
            Assert.Null(cache.Peek(Key, VersionTag.None));
        }

        [Fact]
        public async Task A_load_whose_prelude_found_no_tag_is_served_uncached_and_stores_nothing()
        {
            using TestCache cache = Cache();
            using MetricCollector<long> requests = Collector(CacheTelemetryNames.Requests);
            cache.Loads = (_, _, _) => Task.FromResult(CacheLoad.Loaded(new Box("loaded"), VersionTag.None));

            CacheResult<Box> first = await cache.GetAsync(Key, First, NoDatabase.Instance, Token);
            CacheResult<Box> second = await cache.GetAsync(Key, First, NoDatabase.Instance, Token);

            Assert.Equal(("loaded", VersionTag.None, CacheOutcome.Uncached), (first.Value!.Name, first.Tag, first.Outcome));
            Assert.Equal((CacheOutcome.Uncached, 2), (second.Outcome, cache.LoadCount));
            Assert.Equal(0, cache.Count);
            Assert.Equal(["uncached", "uncached"], Outcomes(requests));
        }

        [Fact]
        public async Task An_oversized_load_has_no_value_and_stores_nothing()
        {
            using TestCache cache = Cache();
            using MetricCollector<long> requests = Collector(CacheTelemetryNames.Requests);
            cache.Loads = (_, _, _) => Task.FromResult(CacheLoad.Oversized<Box>(First));

            CacheResult<Box> result = await cache.GetAsync(Key, First, NoDatabase.Instance, Token);

            Assert.Equal((null, First, CacheOutcome.Oversized), (result.Value, result.Tag, result.Outcome));
            Assert.Equal(0, cache.Count);
            Assert.Equal(["oversized"], Outcomes(requests));
        }

        [Fact]
        public async Task The_loader_runs_on_the_handle_the_caller_passed()
        {
            using TestCache cache = Cache();
            NoDatabase database = new();
            ITenantDatabase? passed = null;
            cache.Loads = (_, handle, _) =>
            {
                passed = handle;
                return Task.FromResult(CacheLoad.Loaded(new Box("loaded"), First));
            };

            _ = await cache.GetAsync(Key, First, database, Token);

            Assert.Same(database, passed);
        }

        [Fact]
        public async Task A_kind_fed_through_set_does_not_load()
        {
            using SetOnlyCache cache = new(CacheKinds.Permissions, 10, MeterFactory);

            _ = await Assert.ThrowsAsync<NotSupportedException>(() => cache.GetAsync(Key, First, NoDatabase.Instance, Token));
        }

        // ===================== Peek =====================

        [Fact]
        public void Peek_answers_a_current_entry_and_counts_each_call_without_loading()
        {
            using TestCache cache = Cache();
            using MetricCollector<long> requests = Collector(CacheTelemetryNames.Requests);
            Box stored = new("stored");

            Assert.Null(cache.Peek(Key, First));
            cache.Set(Key, stored, First);
            Assert.Same(stored, cache.Peek(Key, First));
            Assert.Null(cache.Peek(Key, Second));

            Assert.Equal(["miss", "hit", "stale"], Outcomes(requests));
            Assert.Equal(0, cache.LoadCount);
        }

        [Fact]
        public void Peek_with_a_predicate_answers_an_entry_stored_under_no_tag_while_the_predicate_accepts_it()
        {
            // The permissions kind's way: stored under VersionTag.None, valid while two tags match.
            using SetOnlyCache cache = new(CacheKinds.Permissions, 10, MeterFactory);
            using MetricCollector<long> requests = Collector(CacheTelemetryNames.Requests);
            Box stored = new("current");

            Assert.Null(cache.Peek(Key, static _ => true));
            cache.Set(Key, stored, VersionTag.None);
            Assert.Same(stored, cache.Peek(Key, static box => box.Name == "current"));
            Assert.Null(cache.Peek(Key, static box => box.Name == "other"));

            Assert.Equal(["miss", "hit", "stale"], Outcomes(requests));
        }

        // ===================== Single flight =====================

        [Fact]
        public async Task One_loader_runs_for_64_concurrent_callers_and_they_all_get_its_value()
        {
            using TestCache cache = Cache();
            using MetricCollector<long> requests = Collector(CacheTelemetryNames.Requests);
            TaskCompletionSource<CacheLoad<Box>> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            cache.Loads = (_, _, _) => gate.Task;

            // --- Every call runs synchronously up to its wait, so all 64 are armed before the load completes ---
            Task<CacheResult<Box>>[] calls = [.. Enumerable.Range(0, 64).Select(_ => cache.GetAsync(Key, First, NoDatabase.Instance, Token))];
            Assert.Equal(1, cache.LoadCount);
            Assert.DoesNotContain(calls, static call => call.IsCompleted);

            Box loaded = new("loaded");
            gate.SetResult(CacheLoad.Loaded(loaded, First));
            CacheResult<Box>[] results = await Task.WhenAll(calls).WaitAsync(Bound, Token);

            Assert.Equal(1, cache.LoadCount);
            Assert.All(results, result => Assert.Equal((loaded, CacheOutcome.Miss), (result.Value, result.Outcome)));
            Assert.Equal(64, Outcomes(requests).Count(static outcome => outcome == "miss"));
        }

        [Fact]
        public async Task Callers_asking_under_different_tags_do_not_share_a_load()
        {
            using TestCache cache = Cache();
            TaskCompletionSource<CacheLoad<Box>> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            cache.Loads = (_, _, _) => gate.Task;

            // A caller that knows a newer tag never accepts a load started under an older one.
            Task<CacheResult<Box>> older = cache.GetAsync(Key, First, NoDatabase.Instance, Token);
            Task<CacheResult<Box>> newer = cache.GetAsync(Key, Second, NoDatabase.Instance, Token);
            Assert.Equal(2, cache.LoadCount);

            gate.SetResult(CacheLoad.Loaded(new Box("loaded"), Second));
            _ = await Task.WhenAll(older, newer).WaitAsync(Bound, Token);
        }

        [Fact]
        public async Task A_faulted_load_reaches_every_caller_that_shared_it_and_the_next_caller_loads_afresh()
        {
            using TestCache cache = Cache();
            TaskCompletionSource<CacheLoad<Box>> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            cache.Loads = (_, _, _) => gate.Task;

            Task<CacheResult<Box>> starter = cache.GetAsync(Key, First, NoDatabase.Instance, Token);
            Task<CacheResult<Box>> joiner = cache.GetAsync(Key, First, NoDatabase.Instance, Token);
            gate.SetException(new InvalidOperationException("The load failed."));

            Assert.Equal("The load failed.", (await Assert.ThrowsAsync<InvalidOperationException>(() => starter.WaitAsync(Bound, Token))).Message);
            Assert.Equal("The load failed.", (await Assert.ThrowsAsync<InvalidOperationException>(() => joiner.WaitAsync(Bound, Token))).Message);

            // --- The failed load was forgotten before the failure surfaced: the next call loads ---
            cache.Loads = (_, _, _) => Task.FromResult(CacheLoad.Loaded(new Box("retried"), First));
            CacheResult<Box> retried = await cache.GetAsync(Key, First, NoDatabase.Instance, Token);
            Assert.Equal(("retried", CacheOutcome.Miss, 2), (retried.Value!.Name, retried.Outcome, cache.LoadCount));
        }

        [Fact]
        public async Task A_caller_that_joined_a_load_its_starter_cancelled_starts_its_own()
        {
            using TestCache cache = Cache();
            using var starterToken = CancellationTokenSource.CreateLinkedTokenSource(Token);
            TaskCompletionSource<CacheLoad<Box>> firstGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<CacheLoad<Box>> secondGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource secondStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            cache.Loads = (_, _, cancellationToken) =>
            {
                if (cache.LoadCount == 1)
                {
                    return firstGate.Task.WaitAsync(cancellationToken);
                }

                secondStarted.SetResult();
                return secondGate.Task;
            };

            Task<CacheResult<Box>> starter = cache.GetAsync(Key, First, NoDatabase.Instance, starterToken.Token);
            Task<CacheResult<Box>> joiner = cache.GetAsync(Key, First, NoDatabase.Instance, Token);

            // --- The starter gives up; the joiner, still waiting, loads on its own ---
            await starterToken.CancelAsync();
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starter.WaitAsync(Bound, Token));
            await secondStarted.Task.WaitAsync(Bound, Token);
            secondGate.SetResult(CacheLoad.Loaded(new Box("second"), First));

            CacheResult<Box> result = await joiner.WaitAsync(Bound, Token);
            Assert.Equal(("second", 2), (result.Value!.Name, cache.LoadCount));
        }

        [Fact]
        public async Task A_caller_cancelling_its_own_wait_leaves_the_shared_load_running()
        {
            using TestCache cache = Cache();
            using var joinerToken = CancellationTokenSource.CreateLinkedTokenSource(Token);
            TaskCompletionSource<CacheLoad<Box>> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            cache.Loads = (_, _, _) => gate.Task;

            Task<CacheResult<Box>> starter = cache.GetAsync(Key, First, NoDatabase.Instance, Token);
            Task<CacheResult<Box>> joiner = cache.GetAsync(Key, First, NoDatabase.Instance, joinerToken.Token);
            await joinerToken.CancelAsync();
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => joiner.WaitAsync(Bound, Token));

            gate.SetResult(CacheLoad.Loaded(new Box("loaded"), First));
            Assert.Equal("loaded", (await starter.WaitAsync(Bound, Token)).Value!.Name);
            Assert.Equal(1, cache.LoadCount);
        }

        [Fact]
        public async Task A_load_cancelled_by_a_token_its_starter_never_passed_stores_nothing_and_a_joiner_loads_its_own()
        {
            using TestCache cache = Cache();
            using CancellationTokenSource scope = new();
            TaskCompletionSource<CacheLoad<Box>> first = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<CacheLoad<Box>> second = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource secondStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            cache.Loads = (_, _, _) =>
            {
                if (cache.LoadCount == 1)
                {
                    return first.Task;
                }

                secondStarted.SetResult();
                return second.Task;
            };

            // --- The starter's load is cancelled by a token its batch linked (its scope's), not the one it passed ---
            Task<CacheResult<Box>> starter = cache.GetAsync(Key, First, NoDatabase.Instance, Token);
            Task<CacheResult<Box>> joiner = cache.GetAsync(Key, First, NoDatabase.Instance, Token);
            Assert.Equal(1, cache.LoadCount);
            await scope.CancelAsync();
            _ = first.TrySetCanceled(scope.Token);
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starter.WaitAsync(Bound, Token));

            // --- The joiner was not cancelled: it starts its own load, and the cancelled one stored nothing ---
            await secondStarted.Task.WaitAsync(Bound, Token);
            Assert.Null(cache.Peek(Key, First));
            second.SetResult(CacheLoad.Loaded(new Box("reloaded"), First));
            CacheResult<Box> result = await joiner.WaitAsync(Bound, Token);
            Assert.Equal(("reloaded", CacheOutcome.Miss), (result.Value!.Name, result.Outcome));
            Assert.Equal(2, cache.LoadCount);
        }

        // ===================== Set, Remove, bounds and eviction reasons =====================

        [Fact]
        public async Task Set_meters_the_entry_it_replaces_as_replaced_or_aged_and_remove_as_removed()
        {
            using TestCache cache = Cache();
            using MetricCollector<long> evictions = Collector(CacheTelemetryNames.Evictions);

            cache.Set(Key, new Box("first"), First);
            cache.Set(Key, new Box("second"), Second);
            cache.Set(Key, new Box("rebuilt"), Second, aged: true);
            cache.Remove(Key);

            // A key with no entry drops nothing.
            cache.Remove(Key);

            // The runtime reports evictions on the thread pool.
            await evictions.WaitForMeasurementsAsync(3, Bound);
            Assert.Equal(["age", "removed", "replaced"], [.. evictions.GetMeasurementSnapshot().Select(static measurement => (string)measurement.Tags[CacheTelemetryNames.ReasonTag]!).Order(StringComparer.Ordinal)]);
            Assert.All(evictions.GetMeasurementSnapshot(), static measurement => Assert.Equal(CacheKinds.Preferences, measurement.Tags[CacheTelemetryNames.KindTag]));
            Assert.Null(cache.Peek(Key, Second));
        }

        [Fact]
        public async Task A_full_cache_stays_within_its_limit_and_meters_what_it_drops_as_capacity()
        {
            using TestCache cache = Cache(sizeLimit: 2);
            using MetricCollector<long> evictions = Collector(CacheTelemetryNames.Evictions);

            for (int user = 1; user <= 3; user++)
            {
                cache.Set(new TenantUserCacheKey(1, user), new Box("user"), First);
            }

            // The third entry does not fit: refused, or room made by compacting; either is a capacity eviction.
            await evictions.WaitForMeasurementsAsync(1, Bound);
            Assert.All(evictions.GetMeasurementSnapshot(), static measurement => Assert.Equal("capacity", measurement.Tags[CacheTelemetryNames.ReasonTag]));
            Assert.InRange(cache.Count, 0, 2);
        }

        [Fact]
        public void Keys_begin_with_the_tenant_so_one_user_id_in_two_tenants_never_shares_an_entry()
        {
            using TestCache cache = Cache();
            cache.Set(new TenantUserCacheKey(1, 7), new Box("tenant 1"), First);

            Assert.Null(cache.Peek(new TenantUserCacheKey(2, 7), First));
            Assert.Equal("tenant 1", cache.Peek(new TenantUserCacheKey(1, 7), First)!.Name);

            // Every key type of the kinds leads with the tenant.
            foreach (Type key in new[] { typeof(TenantCacheKey), typeof(TenantUserCacheKey), typeof(CacheableEntityKey) })
            {
                Assert.True(typeof(ITenantCacheKey).IsAssignableFrom(key));
                Assert.Equal(nameof(ITenantCacheKey.TenantId), key.GetConstructors().Single().GetParameters()[0].Name);
            }
        }

        // ===================== Kinds and instruments =====================

        [Fact]
        public void Each_kind_has_one_store_per_meter_and_a_disposed_store_frees_its_kind()
        {
            foreach (string kind in new[] { CacheKinds.Settings, CacheKinds.Preferences, CacheKinds.Permissions, CacheKinds.Entities })
            {
                SetOnlyCache store = new(kind, 10, MeterFactory);
                Assert.Equal((kind, 10L), (store.Kind, store.SizeLimit));
                _ = Assert.Throws<InvalidOperationException>(() => new SetOnlyCache(kind, 10, MeterFactory));
                store.Dispose();
                new SetOnlyCache(kind, 10, MeterFactory).Dispose();
            }

            _ = Assert.Throws<ArgumentException>(() => new SetOnlyCache("lists", 10, MeterFactory));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new SetOnlyCache(CacheKinds.Settings, 0, MeterFactory));
        }

        [Fact]
        public async Task The_gauges_report_entries_size_and_limit_per_kind_and_loads_are_timed()
        {
            using TestCache cache = Cache(sizeLimit: 50);
            using SetOnlyCache settings = new(CacheKinds.Settings, 20, MeterFactory);
            using MetricCollector<long> entries = Collector(CacheTelemetryNames.Entries);
            using MetricCollector<long> size = Collector(CacheTelemetryNames.Size);
            using MetricCollector<long> limit = Collector(CacheTelemetryNames.Limit);
            using MetricCollector<double> duration = new(MeterFactory, CacheTelemetryNames.MeterName, CacheTelemetryNames.LoadDuration);
            cache.Loads = (_, _, _) => Task.FromResult(CacheLoad.Loaded(new Box("loaded", Size: 5), First));

            _ = await cache.GetAsync(new TenantUserCacheKey(1, 1), First, NoDatabase.Instance, Token);
            cache.Set(new TenantUserCacheKey(1, 2), new Box("set", Size: 3), First);

            // --- One measurement per live store, tagged with its kind alone ---
            entries.RecordObservableInstruments();
            size.RecordObservableInstruments();
            limit.RecordObservableInstruments();
            Assert.Equal(2, Gauge(entries, CacheKinds.Preferences));
            Assert.Equal(8, Gauge(size, CacheKinds.Preferences));
            Assert.Equal(50, Gauge(limit, CacheKinds.Preferences));
            Assert.Equal((0, 20), (Gauge(entries, CacheKinds.Settings), Gauge(limit, CacheKinds.Settings)));

            CollectedMeasurement<double> timed = Assert.Single(duration.GetMeasurementSnapshot());
            Assert.Equal(CacheKinds.Preferences, Assert.Single(timed.Tags).Value);
            Assert.True(timed.Value >= 0);
        }

        // ===================== Helpers =====================

        /// <summary>The test's cancellation token.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>The meter factory.</summary>
        private IMeterFactory MeterFactory => _services.GetRequiredService<IMeterFactory>();

        /// <summary>The outcomes a request collector recorded, in order.</summary>
        /// <param name="requests">The collector.</param>
        /// <returns>The outcomes.</returns>
        private static List<string> Outcomes(MetricCollector<long> requests)
        {
            return [.. requests.GetMeasurementSnapshot().Select(static measurement => (string)measurement.Tags[CacheTelemetryNames.OutcomeTag]!)];
        }

        /// <summary>The last value a gauge reported for a kind.</summary>
        /// <param name="gauge">The gauge's collector.</param>
        /// <param name="kind">The kind.</param>
        /// <returns>The value.</returns>
        private static long Gauge(MetricCollector<long> gauge, string kind)
        {
            return gauge.GetMeasurementSnapshot().Last(measurement => Equals(measurement.Tags[CacheTelemetryNames.KindTag], kind)).Value;
        }

        /// <summary>A preferences-kind cache whose loader the test sets.</summary>
        /// <param name="sizeLimit">The size limit.</param>
        /// <returns>The cache.</returns>
        private TestCache Cache(long sizeLimit = 100)
        {
            return new TestCache(sizeLimit, MeterFactory);
        }

        /// <summary>A collector of a long-valued cache instrument.</summary>
        /// <param name="instrument">The instrument's name.</param>
        /// <returns>The collector.</returns>
        private MetricCollector<long> Collector(string instrument)
        {
            return new MetricCollector<long>(MeterFactory, CacheTelemetryNames.MeterName, instrument);
        }

        /// <summary>A cached value.</summary>
        /// <param name="Name">What the test recognises it by.</param>
        /// <param name="Size">Its size in the kind's unit.</param>
        private sealed record Box(string Name, long Size = 1);

        /// <summary>A cache whose loader the test sets, sized by its values.</summary>
        /// <param name="sizeLimit">The size limit.</param>
        /// <param name="meterFactory">The meter factory.</param>
        private sealed class TestCache(long sizeLimit, IMeterFactory meterFactory)
            : VersionedCache<TenantUserCacheKey, Box>(CacheKinds.Preferences, sizeLimit, meterFactory)
        {
            /// <summary>How many loads started.</summary>
            private int _loads;

            /// <summary>The loader.</summary>
            public Func<TenantUserCacheKey, ITenantDatabase, CancellationToken, Task<CacheLoad<Box>>> Loads { get; set; } =
                static (_, _, _) => throw new InvalidOperationException("The test set no loader.");

            /// <summary>How many loads started.</summary>
            public int LoadCount => Volatile.Read(ref _loads);

            /// <summary>How many of tenant 1's users 0 to 10 have an entry, under any tag.</summary>
            public int Count => Enumerable.Range(0, 11).Count(user => Lookup(new TenantUserCacheKey(1, user), First, out _) != CacheOutcome.Miss);

            /// <inheritdoc />
            protected override Task<CacheLoad<Box>> LoadAsync(TenantUserCacheKey key, ITenantDatabase database, CancellationToken cancellationToken)
            {
                _ = Interlocked.Increment(ref _loads);
                return Loads(key, database, cancellationToken);
            }

            /// <inheritdoc />
            protected override long SizeOf(Box value)
            {
                return value.Size;
            }
        }

        /// <summary>A cache of any kind that never loads.</summary>
        /// <param name="kind">The kind.</param>
        /// <param name="sizeLimit">The size limit.</param>
        /// <param name="meterFactory">The meter factory.</param>
        private sealed class SetOnlyCache(string kind, long sizeLimit, IMeterFactory meterFactory)
            : VersionedCache<TenantUserCacheKey, Box>(kind, sizeLimit, meterFactory);

        /// <summary>A database handle no batch is created on.</summary>
        private sealed class NoDatabase : ITenantDatabase
        {
            /// <summary>A shared instance.</summary>
            public static NoDatabase Instance { get; } = new();

            /// <inheritdoc />
            public QueryexSchema Schema => throw new NotSupportedException();

            /// <inheritdoc />
            public QueryexContextValues Context => throw new NotSupportedException();

            /// <inheritdoc />
            public IDataBatch CreateBatch(BatchPurpose purpose)
            {
                throw new NotSupportedException();
            }
        }
    }
}
