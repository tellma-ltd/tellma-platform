// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;
using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     A job's progress message or display label: a resource key and the raw arguments its text
    ///     renders from, never rendered text. Each reader renders it in their own language from the
    ///     string pack; the worker renders it in English for the log.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A handler's keys live in its own assembly's string resources. The key fits
    ///         <see cref="JobLimits.MaxKeyLength" /> characters and the arguments, serialised under the
    ///         platform's JSON rules, <see cref="JobLimits.MaxArgumentsLength" />; the members that
    ///         store a message refuse a wider one with <see cref="ArgumentException" />. An argument named
    ///         <c>entity</c> holding an entity name renders as that entity's plural label.
    ///     </para>
    ///     <para>
    ///         Both members are get-only and copied on construction, and two messages are equal when
    ///         their keys and their arguments (name by name, each value by
    ///         <see cref="object.Equals(object, object)" />) are.
    ///     </para>
    /// </remarks>
    /// <param name="Key">The message's resource key; not blank.</param>
    /// <param name="Arguments">The raw values its text renders from; empty when it has none.</param>
    public sealed record JobMessage(string Key, IReadOnlyDictionary<string, object?> Arguments)
    {
        /// <summary>Creates a message without arguments.</summary>
        /// <param name="key">The message's resource key; not blank.</param>
        /// <exception cref="ArgumentException"><paramref name="key" /> is blank.</exception>
        public JobMessage(string key)
            : this(key, ReadOnlyDictionary<string, object?>.Empty)
        {
        }

        /// <inheritdoc cref="JobMessage(string, IReadOnlyDictionary{string, object?})" />
        public string Key { get; } = ValidateKey(Key);

        /// <inheritdoc cref="JobMessage(string, IReadOnlyDictionary{string, object?})" />
        public IReadOnlyDictionary<string, object?> Arguments { get; } = CopyArguments(Arguments);

        /// <summary>Whether another message carries an equal key and equal arguments.</summary>
        /// <param name="other">The other message.</param>
        /// <returns>True when the two are equal.</returns>
        public bool Equals(JobMessage? other)
        {
            // The argument bag is compared by content: each message holds its own copy, so the
            // reference comparison a record would synthesise could never find two messages equal.
            return other is not null
                && string.Equals(Key, other.Key, StringComparison.Ordinal)
                && ArgumentBags.Equal(Arguments, other.Arguments);
        }

        /// <summary>A hash consistent with <see cref="Equals(JobMessage)" />.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(StringComparer.Ordinal.GetHashCode(Key), Arguments.Count);
        }

        /// <summary>Refuses a blank key.</summary>
        /// <param name="key">The key.</param>
        /// <returns>The key.</returns>
        private static string ValidateKey(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            return key;
        }

        /// <summary>Refuses a null argument bag and copies the rest.</summary>
        /// <param name="arguments">The arguments.</param>
        /// <returns>A read-only copy.</returns>
        private static IReadOnlyDictionary<string, object?> CopyArguments(IReadOnlyDictionary<string, object?> arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            return ReadOnlyCopies.Of(arguments);
        }
    }
}
