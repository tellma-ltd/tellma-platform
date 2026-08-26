// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex.Caching
{
    /// <summary>
    ///     A cache that never grows past a stated size, discarding whatever was used longest ago.
    /// </summary>
    /// <remarks>
    ///     Bounded rather than merely large, and deliberately so. Everything cached here is keyed by
    ///     text a user supplied, so an unbounded cache would let anyone who can send a query send a
    ///     million different ones and take the process down with them. A cap turns that into wasted
    ///     effort instead of exhausted memory.
    /// </remarks>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The cached value type.</typeparam>
    internal sealed class BoundedCache<TKey, TValue>
        where TKey : notnull
        where TValue : class
    {
        /// <summary>The most a caller may store.</summary>
        private readonly int _capacity;

        /// <summary>Guards both collections, which have to move together.</summary>
        private readonly Lock _gate = new();

        /// <summary>The entries, by key.</summary>
        private readonly Dictionary<TKey, LinkedListNode<Entry>> _entries;

        /// <summary>The keys in use order, least recently used first.</summary>
        private readonly LinkedList<Entry> _order = new();

        /// <summary>Initializes a cache.</summary>
        /// <param name="capacity">The most it may hold. Zero or less disables caching entirely.</param>
        /// <param name="comparer">How keys are compared, or null for the default.</param>
        internal BoundedCache(int capacity, IEqualityComparer<TKey>? comparer = null)
        {
            _capacity = capacity;
            _entries = new Dictionary<TKey, LinkedListNode<Entry>>(comparer);
        }

        /// <summary>Looks a value up, marking it as recently used.</summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value, when it was there.</param>
        /// <returns>True when it was.</returns>
        internal bool TryGet(TKey key, out TValue? value)
        {
            if (_capacity <= 0)
            {
                value = null;
                return false;
            }

            lock (_gate)
            {
                if (!_entries.TryGetValue(key, out LinkedListNode<Entry>? node))
                {
                    value = null;
                    return false;
                }

                _order.Remove(node);
                _order.AddLast(node);
                value = node.Value.Value;
                return true;
            }
        }

        /// <summary>Stores a value, discarding the least recently used one if it has to.</summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        internal void Set(TKey key, TValue value)
        {
            if (_capacity <= 0)
            {
                return;
            }

            lock (_gate)
            {
                if (_entries.TryGetValue(key, out LinkedListNode<Entry>? existing))
                {
                    _order.Remove(existing);
                    _entries.Remove(key);
                }

                LinkedListNode<Entry> node = _order.AddLast(new Entry(key, value));
                _entries[key] = node;

                while (_entries.Count > _capacity)
                {
                    LinkedListNode<Entry>? oldest = _order.First;
                    if (oldest is null)
                    {
                        return;
                    }

                    _order.Remove(oldest);
                    _entries.Remove(oldest.Value.Key);
                }
            }
        }

        /// <summary>One cached value, with the key it is filed under.</summary>
        /// <param name="Key">The key.</param>
        /// <param name="Value">The value.</param>
        private readonly record struct Entry(TKey Key, TValue Value);
    }
}
