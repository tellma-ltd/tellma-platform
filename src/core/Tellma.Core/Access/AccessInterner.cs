// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;

namespace Tellma.Core.Access
{
    // Interning is spec 0014 §6.2: a role's filter is one object however many members it has.
    /// <summary>
    ///     Shares one instance of each filter text and role name across the permission sets this
    ///     process builds, so a role's filter is one string however many members' sets hold it.
    ///     Bounded: when full, it starts over rather than growing.
    /// </summary>
    /// <remarks>
    ///     Interning saves memory and never decides anything: an equal string is returned or the
    ///     string itself is, so a reset or a race costs a duplicate, never a wrong value.
    /// </remarks>
    internal sealed class AccessInterner
    {
        /// <summary>The most distinct strings held before starting over.</summary>
        internal const int MaxEntries = 65536;

        /// <summary>The most characters held, across every string, before starting over: 8 MiB of text.</summary>
        internal const long MaxCharacters = 4L * 1024 * 1024;

        /// <summary>The shared instances, keyed by their own value.</summary>
        private readonly ConcurrentDictionary<string, string> _strings = new(StringComparer.Ordinal);

        /// <summary>The characters the shared instances hold, approximately under concurrency.</summary>
        private long _characters;

        /// <summary>How many distinct strings are held.</summary>
        internal int Count => _strings.Count;

        /// <summary>The shared instance of a string, which becomes the shared one when none is held.</summary>
        /// <param name="value">The string.</param>
        /// <returns>An instance equal to it.</returns>
        public string Intern(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_strings.TryGetValue(value, out string? shared))
            {
                return shared;
            }

            // --- Full: start over rather than grow without bound ---
            if (_strings.Count >= MaxEntries || Interlocked.Read(ref _characters) + value.Length > MaxCharacters)
            {
                _strings.Clear();
                _ = Interlocked.Exchange(ref _characters, 0);
            }

            shared = _strings.GetOrAdd(value, value);
            if (ReferenceEquals(shared, value))
            {
                _ = Interlocked.Add(ref _characters, value.Length);
            }

            return shared;
        }
    }
}
