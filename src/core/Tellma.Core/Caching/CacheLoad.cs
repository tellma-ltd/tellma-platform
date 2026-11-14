// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;

namespace Tellma.Core.Caching
{
    /// <summary>
    ///     What a <see cref="VersionedCache{TKey, TValue}" /> loader read: a value and the tag its
    ///     batch's prelude returned, or the report that the source was too large to cache. Created
    ///     through <see cref="CacheLoad" />.
    /// </summary>
    /// <typeparam name="TValue">The kind's value type.</typeparam>
    public sealed class CacheLoad<TValue>
        where TValue : class
    {
        /// <summary>Creates a load.</summary>
        /// <param name="value">The value, or null when oversized.</param>
        /// <param name="tag">The tag.</param>
        internal CacheLoad(TValue? value, VersionTag tag)
        {
            Value = value;
            Tag = tag ?? throw new ArgumentNullException(nameof(tag));
        }

        /// <summary>The value; null when the load was oversized.</summary>
        public TValue? Value { get; }

        /// <summary>
        ///     The tag the load's own prelude returned for the kind's name; <see cref="VersionTag.None" />
        ///     when the database has no row for it, and the value is then served but not stored.
        /// </summary>
        public VersionTag Tag { get; }

        /// <summary>Whether the source was too large to cache: no value, and nothing is stored.</summary>
        public bool IsOversized => Value is null;
    }

    /// <summary>Creates the loads a <see cref="VersionedCache{TKey, TValue}" /> loader returns.</summary>
    public static class CacheLoad
    {
        /// <summary>A loaded value.</summary>
        /// <typeparam name="TValue">The kind's value type.</typeparam>
        /// <param name="value">The value.</param>
        /// <param name="tag">The tag the load's prelude returned.</param>
        /// <returns>The load.</returns>
        public static CacheLoad<TValue> Loaded<TValue>(TValue value, VersionTag tag)
            where TValue : class
        {
            ArgumentNullException.ThrowIfNull(value);
            return new CacheLoad<TValue>(value, tag);
        }

        /// <summary>A source too large to cache.</summary>
        /// <typeparam name="TValue">The kind's value type.</typeparam>
        /// <param name="tag">The tag the load's prelude returned.</param>
        /// <returns>The load.</returns>
        public static CacheLoad<TValue> Oversized<TValue>(VersionTag tag)
            where TValue : class
        {
            return new CacheLoad<TValue>(null, tag);
        }
    }
}
