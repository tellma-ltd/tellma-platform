// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Caching;

namespace Tellma.Core.Caching
{
    // The instruments are spec 0013 §11's tellma.cache.* rows.
    /// <summary>
    ///     The cache instruments of one meter, shared by every <see cref="VersionedCache{TKey, TValue}" />
    ///     on it: each instrument is created once per meter, and the observable gauges report one
    ///     measurement per live cache, tagged with its kind. No instrument carries a tenant, a user,
    ///     an entity or a key.
    /// </summary>
    internal sealed class CacheInstruments
    {
        /// <summary>The instruments of each meter, created on first use and released with the meter.</summary>
        private static readonly ConditionalWeakTable<Meter, CacheInstruments> PerMeter = [];

        /// <summary>The live caches, by kind.</summary>
        private readonly ConcurrentDictionary<string, CacheGauges> _caches = new(StringComparer.Ordinal);

        /// <summary>Creates the instruments on a meter.</summary>
        /// <param name="meter">The meter.</param>
        private CacheInstruments(Meter meter)
        {
            Requests = meter.CreateCounter<long>(
                CacheTelemetryNames.Requests,
                "{request}",
                "Cache lookups, by cache kind and outcome.");

            LoadDuration = meter.CreateHistogram<double>(
                CacheTelemetryNames.LoadDuration,
                "s",
                "The duration of the loads a cache ran on a miss, by cache kind.");

            Evictions = meter.CreateCounter<long>(
                CacheTelemetryNames.Evictions,
                "{entry}",
                "Cache entries dropped, by cache kind and reason.");

            _ = meter.CreateObservableGauge(
                CacheTelemetryNames.Entries,
                () => Observe(static gauges => gauges.Entries()),
                "{entry}",
                "The entries a cache holds, by cache kind.");

            _ = meter.CreateObservableGauge(
                CacheTelemetryNames.Size,
                () => Observe(static gauges => gauges.Size()),
                "{unit}",
                "The size a cache holds, in its kind's unit, by cache kind.");

            _ = meter.CreateObservableGauge(
                CacheTelemetryNames.Limit,
                () => Observe(static gauges => gauges.Limit),
                "{unit}",
                "The size a cache may hold, in its kind's unit, by cache kind.");
        }

        /// <summary>Cache lookups.</summary>
        public Counter<long> Requests { get; }

        /// <summary>Load durations.</summary>
        public Histogram<double> LoadDuration { get; }

        /// <summary>Dropped entries.</summary>
        public Counter<long> Evictions { get; }

        /// <summary>The instruments of a meter.</summary>
        /// <param name="meter">The meter.</param>
        /// <returns>The instruments, the same for every call with one meter.</returns>
        public static CacheInstruments For(Meter meter)
        {
            ArgumentNullException.ThrowIfNull(meter);
            return PerMeter.GetValue(meter, static created => new CacheInstruments(created));
        }

        /// <summary>Adds a live cache to the gauges.</summary>
        /// <param name="kind">The cache's kind.</param>
        /// <param name="gauges">Reads the cache's gauges.</param>
        /// <exception cref="InvalidOperationException">A live cache of the kind already reports on this meter.</exception>
        public void Register(string kind, CacheGauges gauges)
        {
            ArgumentNullException.ThrowIfNull(kind);
            ArgumentNullException.ThrowIfNull(gauges);

            // Two stores of one kind would report two series under one tag set, which no reader can tell apart.
            if (!_caches.TryAdd(kind, gauges))
            {
                throw new InvalidOperationException(
                    $"A cache of the kind '{kind}' already reports on this meter. Each kind has one store per process; dispose the other before creating another.");
            }
        }

        /// <summary>Removes a cache from the gauges.</summary>
        /// <param name="kind">The cache's kind.</param>
        /// <param name="gauges">The gauges it registered, so a later cache of the kind is never removed by an earlier one.</param>
        public void Unregister(string kind, CacheGauges gauges)
        {
            _ = _caches.TryRemove(new KeyValuePair<string, CacheGauges>(kind, gauges));
        }

        /// <summary>One measurement per live cache.</summary>
        /// <param name="read">Reads the value of one cache.</param>
        /// <returns>The measurements.</returns>
        private IEnumerable<Measurement<long>> Observe(Func<CacheGauges, long> read)
        {
            foreach (KeyValuePair<string, CacheGauges> cache in _caches)
            {
                yield return new Measurement<long>(read(cache.Value), new KeyValuePair<string, object?>(CacheTelemetryNames.KindTag, cache.Key));
            }
        }
    }

    /// <summary>What a live cache reports through the observable gauges; compared by reference.</summary>
    /// <param name="entries">Reads how many entries the cache holds.</param>
    /// <param name="size">Reads the size the cache holds, in its kind's unit.</param>
    /// <param name="limit">The size the cache may hold, in its kind's unit.</param>
    internal sealed class CacheGauges(Func<long> entries, Func<long> size, long limit)
    {
        /// <summary>Reads how many entries the cache holds.</summary>
        public Func<long> Entries { get; } = entries;

        /// <summary>Reads the size the cache holds, in its kind's unit.</summary>
        public Func<long> Size { get; } = size;

        /// <summary>The size the cache may hold, in its kind's unit.</summary>
        public long Limit { get; } = limit;
    }
}
